using System.Text.Json;

namespace StorageHub.Agent.Windows;

internal sealed record AgentConcurrencyConfiguration(
    bool Adaptive,
    int Minimum,
    int MaximumTransfers,
    int PerConnection,
    int MaximumSyncs)
{
    public static AgentConcurrencyConfiguration Defaults { get; } = new(true, 1, 4, 2, 2);

    /// <summary>
    /// Reads the concurrency policy the desktop chose, from whichever file the desktop currently
    /// keeps it in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two files are probed because a given installation may be on either side of the settings
    /// migration, and both are legitimate: <c>config.json</c> is what the desktop writes now, and
    /// <c>settings.json</c> is what it wrote before. New first, so a migrated installation never
    /// reads a stale file the desktop has stopped updating.
    /// </para>
    /// <para>
    /// Getting this wrong is silent -- the agent would simply run at default concurrency and
    /// nothing would say so -- which is why it probes rather than assuming.
    /// </para>
    /// <para>
    /// The size and reparse-point checks stay here rather than being taken on trust from the
    /// desktop. This is a different process reading a file it does not own.
    /// </para>
    /// </remarks>
    public static AgentConcurrencyConfiguration Load(string desktopDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopDirectory);
        return ReadFile(Path.Combine(desktopDirectory, "config.json"), minimumSchema: 1)
            ?? ReadFile(Path.Combine(desktopDirectory, "settings.json"), minimumSchema: 4)
            ?? Defaults;
    }

    private static AgentConcurrencyConfiguration? ReadFile(string settingsPath, int minimumSchema)
    {
        try
        {
            var file = new FileInfo(settingsPath);
            if (!file.Exists || file.Length is <= 0 or > 64 * 1024 ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            using var stream = new FileStream(
                settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var schema = ReadInt(root, "schemaVersion", 0);
            var candidate = new AgentConcurrencyConfiguration(
                ReadBool(root, "adaptiveConcurrency", Defaults.Adaptive),
                ReadInt(root, "minimumConcurrency", Defaults.Minimum),
                ReadInt(root, "maximumTransferConcurrency", Defaults.MaximumTransfers),
                ReadInt(root, "perConnectionConcurrency", Defaults.PerConnection),
                ReadInt(root, "maximumSyncConcurrency", Defaults.MaximumSyncs));
            // A file that is present but out of bounds resolves to the defaults here rather than
            // falling through to the older file: the desktop's current answer is "these", and a
            // superseded file is not a better one.
            return schema >= minimumSchema && candidate.HasValidBounds ? candidate : Defaults;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return Defaults;
        }
    }

    private bool HasValidBounds =>
        Minimum is >= 1 and <= 8 &&
        MaximumTransfers is >= 1 and <= 32 &&
        Minimum <= MaximumTransfers &&
        PerConnection is >= 1 and <= 16 &&
        MaximumSyncs is >= 1 and <= 8 &&
        Minimum <= MaximumSyncs;

    private static int ReadInt(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : fallback;

    private static bool ReadBool(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
}
