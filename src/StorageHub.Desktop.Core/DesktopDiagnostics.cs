using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using StorageHub.Desktop.Framework;
using StorageHub.Desktop.Localization;
using StorageHub.Diagnostics;

namespace StorageHub.Desktop;

/// <summary>
/// What Tools, Diagnostics reports about this installation, gathered once when it opens.
/// </summary>
/// <param name="AgentState">How the shell last heard the agent, or null before it has.</param>
/// <param name="QueueCounts">How many transfers the agent holds in each state, by state name.</param>
internal sealed record DiagnosticsFacts(
    string Version,
    string OperatingSystem,
    string Architecture,
    string Runtime,
    string Culture,
    string UiCulture,
    AgentConnectionState? AgentState,
    string? AgentDetail,
    int ActiveTransfers,
    int ActiveSyncRuns,
    DateTimeOffset? AgentObservedAtUtc,
    IReadOnlyDictionary<string, int> QueueCounts,
    DateTimeOffset CollectedAtUtc);

/// <summary>
/// The diagnostics report and support bundle, and the environment line Report issue fills in.
/// </summary>
/// <remarks>
/// <para>
/// 1.4 declared Diagnostics ("Collect a support bundle describing this installation") and left it
/// dead, though it shipped StorageHub.Diagnostics, the policy for what such a bundle may hold. This
/// is the bundle that policy was written for: every file goes through
/// <see cref="DiagnosticBundlePolicy.CreateManifest"/> before it is written, so a credential, a
/// database or a key cannot end up in one by a later change here.
/// </para>
/// <para>
/// Nothing in it names anybody. The desktop's error log is the one file that could, through the
/// paths in a stack trace, so the user's home folder, user name and machine name are replaced in
/// the copy that goes into the bundle; the log itself is left as it is.
/// </para>
/// </remarks>
internal static class DesktopDiagnostics
{
    /// <summary>The most of the error log a bundle carries: its newest megabyte, which is all it keeps.</summary>
    internal const int MaximumLogBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The facts as they are now.</summary>
    internal static DiagnosticsFacts Collect(
        AgentMonitorStatus? agent,
        IReadOnlyDictionary<string, int>? queueCounts = null,
        TimeProvider? clock = null) => new(
        DesktopApplicationVersion.Current,
        RuntimeInformation.OSDescription.Trim(),
        RuntimeInformation.OSArchitecture.ToString(),
        RuntimeInformation.FrameworkDescription,
        CultureInfo.CurrentCulture.Name,
        CultureInfo.CurrentUICulture.Name,
        agent?.State,
        agent?.Detail,
        agent?.ActiveTransfers ?? 0,
        agent?.ActiveSyncRuns ?? 0,
        agent?.ObservedAtUtc,
        queueCounts ?? new Dictionary<string, int>(),
        (clock ?? TimeProvider.System).GetUtcNow());

    /// <summary>
    /// The Environment box of the bug report form: "Linux 6.8 (X64); .NET 10.0.1; agent Connected".
    /// </summary>
    /// <remarks>Written in English, as the form and the project's tracker are.</remarks>
    internal static string IssueEnvironment(DiagnosticsFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{facts.OperatingSystem} ({facts.Architecture}); {facts.Runtime}; agent {facts.AgentState?.ToString() ?? "unknown"}");
    }

    /// <summary>The report the window shows and Copy copies, in the current language.</summary>
    internal static string Report(DiagnosticsFacts facts, string logFolder)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var text = new StringBuilder();
        Line(Ui.Shell.DiagnosticsVersion, facts.Version);
        Line(Ui.Shell.DiagnosticsOperatingSystem, $"{facts.OperatingSystem} ({facts.Architecture})");
        Line(Ui.Shell.DiagnosticsRuntime, facts.Runtime);
        Line(Ui.Shell.DiagnosticsLanguage, $"{facts.UiCulture} / {facts.Culture}");
        Line(Ui.Shell.DiagnosticsAgent, facts.AgentState is { } state
            ? string.IsNullOrWhiteSpace(facts.AgentDetail) ? state.ToString() : $"{state} - {facts.AgentDetail}"
            : Ui.Shell.DiagnosticsAgentNotHeard);
        Line(Ui.Shell.DiagnosticsRunning, Ui.Format(
            Ui.Shell.DiagnosticsRunningFormat, facts.ActiveTransfers, facts.ActiveSyncRuns));
        var counts = string.Join(", ", facts.QueueCounts
            .Where(static pair => pair.Value > 0)
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key} {pair.Value}")));
        Line(Ui.Shell.DiagnosticsQueue, facts.QueueCounts.Count == 0 ? Ui.Shell.DiagnosticsQueueNotRead
            : counts.Length == 0 ? Ui.Shell.DiagnosticsQueueEmpty
            : counts);
        Line(Ui.Shell.DiagnosticsLogFolder, logFolder);
        Line(Ui.Shell.DiagnosticsCollected, facts.CollectedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        return text.ToString();

        void Line(string label, string value) => text.Append(label).Append(": ").AppendLine(value);
    }

    /// <summary>
    /// Writes the support bundle: a zip of the files <see cref="DiagnosticBundlePolicy"/> admits.
    /// </summary>
    /// <param name="logPath">The desktop's error log, or null to leave it out.</param>
    /// <returns>The paths written into the zip, in its order.</returns>
    internal static async Task<IReadOnlyList<string>> WriteBundleAsync(
        string zipPath,
        DiagnosticsFacts facts,
        string? logPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentNullException.ThrowIfNull(facts);

        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["environment/summary.json"] = JsonSerializer.SerializeToUtf8Bytes(new
            {
                facts.Version,
                facts.OperatingSystem,
                facts.Architecture,
                facts.Runtime,
                facts.Culture,
                facts.UiCulture,
                facts.CollectedAtUtc
            }, Json),
            ["health/latest.json"] = JsonSerializer.SerializeToUtf8Bytes(new
            {
                AgentState = facts.AgentState?.ToString(),
                facts.AgentDetail,
                facts.ActiveTransfers,
                facts.ActiveSyncRuns,
                facts.AgentObservedAtUtc,
                facts.QueueCounts
            }, Json),
            ["packages/inventory.json"] = JsonSerializer.SerializeToUtf8Bytes(Inventory(), Json)
        };

        if (logPath is not null && await ReadLogAsync(logPath, cancellationToken).ConfigureAwait(false) is { } log)
        {
            contents["logs/application.log"] = Encoding.UTF8.GetBytes(Redact(log));
        }

        // Every candidate is put to the policy, which drops anything it does not name.
        var manifest = DiagnosticBundlePolicy.CreateManifest(contents.Select(static pair => new DiagnosticArtifact(
            KindOf(pair.Key),
            pair.Key,
            pair.Value.LongLength,
            ContainsPrivateNames: false)));

        var directory = Path.GetDirectoryName(Path.GetFullPath(zipPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await using var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var artifact in manifest)
        {
            var entry = zip.CreateEntry(artifact.RelativePath, CompressionLevel.Optimal);
            await using var stream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(contents[artifact.RelativePath], cancellationToken).ConfigureAwait(false);
        }

        return [.. manifest.Select(static artifact => artifact.RelativePath)];
    }

    /// <summary>
    /// Takes the names out of a log: the home folder, then the user and machine names wherever they
    /// stand on their own, so a path or a host in a stack trace says nothing about whose it was.
    /// </summary>
    internal static string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 1) text = text.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
        foreach (var name in new[] { Environment.UserName, Environment.MachineName })
        {
            if (name.Length < 2) continue;
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                $@"(?<![\p{{L}}\p{{N}}]){System.Text.RegularExpressions.Regex.Escape(name)}(?![\p{{L}}\p{{N}}])",
                "<redacted>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(2));
        }

        return text;
    }

    /// <summary>The StorageHub, CodeLogic and Avalonia assemblies loaded, with their versions.</summary>
    private static IReadOnlyList<object> Inventory() =>
    [
        .. AppDomain.CurrentDomain.GetAssemblies()
            .Select(static assembly => assembly.GetName())
            .Where(static name => name.Name is { } value &&
                (value.StartsWith("StorageHub", StringComparison.Ordinal) ||
                 value.StartsWith("CodeLogic", StringComparison.Ordinal) ||
                 value.StartsWith("CL.", StringComparison.Ordinal) ||
                 value.StartsWith("Avalonia", StringComparison.Ordinal)))
            .DistinctBy(static name => name.Name, StringComparer.Ordinal)
            .OrderBy(static name => name.Name, StringComparer.Ordinal)
            .Select(static name => (object)new { name.Name, Version = name.Version?.ToString() })
    ];

    private static DiagnosticArtifactKind KindOf(string path) => path.Split('/')[0] switch
    {
        "logs" => DiagnosticArtifactKind.StructuredLog,
        "health" => DiagnosticArtifactKind.HealthSnapshot,
        "packages" => DiagnosticArtifactKind.PackageInventory,
        "environment" => DiagnosticArtifactKind.EnvironmentSummary,
        _ => DiagnosticArtifactKind.ConfigurationSchema
    };

    /// <summary>The newest megabyte of the log, or null when there is none or it cannot be read.</summary>
    private static async Task<string?> ReadLogAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path)) return null;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaximumLogBytes) stream.Seek(-MaximumLogBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Where the desktop's logs are: its error log's folder, which CodeLogic writes beside.</summary>
    internal static string LogFolder => Path.GetDirectoryName(DesktopErrorLog.FilePath)!;
}
