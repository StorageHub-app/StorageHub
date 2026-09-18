using System.Text.Json;
using System.Text.Json.Nodes;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Framework;

/// <summary>
/// Where the desktop's own CodeLogic tree lives.
/// </summary>
/// <remarks>
/// The desktop deliberately gets a framework root of its own, beside the agent's rather than
/// inside it. The agent owns <c>{root}\Agent</c> under a single-instance lease; sharing that tree
/// would mean two processes scaffolding the same directories, and taking the lease would stop the
/// agent starting at all. Splitting at <c>{root}\Desktop</c> costs one extra directory and removes
/// the whole class of cross-process races.
/// </remarks>
internal sealed class DesktopFrameworkPaths
{
    internal const string DataRootEnvironmentVariable = "STORAGEHUB_DATA_ROOT";

    private DesktopFrameworkPaths(string dataRoot, string applicationRoot, string frameworkRoot)
    {
        DataRoot = dataRoot;
        ApplicationRoot = applicationRoot;
        FrameworkRoot = frameworkRoot;
    }

    /// <summary>The shared StorageHub data root. The agent owns a sibling directory under it.</summary>
    internal string DataRoot { get; }

    /// <summary>
    /// The desktop's application root: config, localization, logs and data. This is the directory
    /// that already holds <c>settings.json</c>, so adopting CodeLogic config migrates in place.
    /// </summary>
    internal string ApplicationRoot { get; }

    /// <summary>The desktop's framework root: <c>CodeLogic.json</c>, <c>Libraries/</c>, framework logs.</summary>
    internal string FrameworkRoot { get; }

    internal string LocalizationDirectory => Path.Combine(ApplicationRoot, "localization");

    /// <summary>
    /// Resolves the desktop's paths without creating anything. Mirrors the agent's root resolution
    /// but deliberately not its directory lease.
    /// </summary>
    internal static DesktopFrameworkPaths Resolve()
    {
        var configured = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                throw new InvalidOperationException(
                    "The current user's local application-data directory is unavailable.");
            }

            configured = Path.Combine(localAppData, "StorageHub");
        }

        return Create(configured);
    }

    /// <summary>Resolves the paths under an explicit data root. Exposed so tests need no environment.</summary>
    internal static DesktopFrameworkPaths Create(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        if (!Path.IsPathFullyQualified(dataRoot))
        {
            throw new ArgumentException("The StorageHub data root must be absolute.", nameof(dataRoot));
        }

        var root = Path.GetFullPath(dataRoot);
        var applicationRoot = Path.Combine(root, "Desktop");
        return new DesktopFrameworkPaths(
            root,
            applicationRoot,
            Path.Combine(applicationRoot, "CodeLogic"));
    }

    /// <summary>
    /// Creates the directories the framework expects before it is initialized. CodeLogic scaffolds
    /// these itself on first run, but doing it here keeps the failure — an unwritable profile, a
    /// redirected AppData — on our side of the boundary where it can be reported properly.
    /// </summary>
    internal void EnsureCreated()
    {
        _ = Directory.CreateDirectory(ApplicationRoot);
        _ = Directory.CreateDirectory(FrameworkRoot);
        _ = Directory.CreateDirectory(LocalizationDirectory);
    }

    /// <summary>Where the framework keeps its own configuration.</summary>
    private string FrameworkConfigPath => Path.Combine(FrameworkRoot, "Framework", "CodeLogic.json");

    /// <summary>
    /// Makes sure the framework will generate and load the languages StorageHub ships.
    /// </summary>
    /// <remarks>
    /// CodeLogic scaffolds CodeLogic.json with <c>supportedCultures: ["en-US"]</c> and only ever
    /// generates and loads the cultures named there, so shipping Danish and German means adding
    /// them to that list. The edit is additive: an operator may have added a culture of their own,
    /// and taking it away again on the next start would be its own bug. Doing nothing here is not
    /// fatal — an absent culture falls back to English — so every failure is swallowed.
    /// </remarks>
    internal void EnsureSupportedCultures()
    {
        try
        {
            var path = FrameworkConfigPath;
            if (!File.Exists(path))
            {
                // Not yet scaffolded. InitializeAsync writes it, and the next start patches it.
                return;
            }

            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
            {
                return;
            }

            if (root["localization"] is not JsonObject localization)
            {
                localization = [];
                root["localization"] = localization;
            }

            var cultures = localization["supportedCultures"] as JsonArray;
            if (cultures is null)
            {
                cultures = [];
                localization["supportedCultures"] = cultures;
            }

            var present = cultures
                .Select(node => node?.GetValue<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var added = false;
            foreach (var culture in DesktopCulture.SupportedCultures)
            {
                if (present.Add(culture))
                {
                    cultures.Add(culture);
                    added = true;
                }
            }

            if (added)
            {
                File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A framework config we cannot read or write still starts; it just speaks English.
        }
    }

    /// <summary>
    /// Copies the translations shipped with StorageHub into the localization directory.
    /// </summary>
    /// <remarks>
    /// The framework generates templates, but a generated template is the English text under a
    /// da-DK name — worse than nothing — and it never overwrites a file that already exists. So
    /// the real translations have to be placed here before the framework loads them.
    ///
    /// They are product assets rather than user data, so they are refreshed whenever the version
    /// changes: a translation file that never gains the strings added since it shipped is the more
    /// likely complaint. en-US is deliberately not seeded — it is generated from the models, which
    /// are the source of the English text.
    /// </remarks>
    internal void SeedShippedTranslations(string applicationVersion)
    {
        try
        {
            var source = Path.Combine(AppContext.BaseDirectory, "localization");
            if (!Directory.Exists(source))
            {
                return;
            }

            var marker = Path.Combine(LocalizationDirectory, ".seed-version");
            if (File.Exists(marker) &&
                string.Equals(File.ReadAllText(marker).Trim(), applicationVersion, StringComparison.Ordinal) &&
                !IsStale(source, marker))
            {
                return;
            }

            _ = Directory.CreateDirectory(LocalizationDirectory);
            foreach (var file in Directory.EnumerateFiles(source, "*.json"))
            {
                var name = Path.GetFileName(file);
                if (name.Contains("." + DesktopCulture.DefaultCulture + ".", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Copy(file, Path.Combine(LocalizationDirectory, name), overwrite: true);
            }

            DiscardGeneratedDefaults();
            File.WriteAllText(marker, applicationVersion);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Without the shipped files the framework generates English templates instead.
        }
    }

    /// <summary>
    /// Whether a shipped file has changed since the last seeding, regardless of the version.
    /// </summary>
    /// <remarks>
    /// The version alone is not enough during development, where the translations change many
    /// times between version bumps and the stale copies would be what the shell actually showed --
    /// which makes checking a translation report something it is not. Comparing write times costs
    /// one stat per file and removes a whole class of "but I fixed that".
    /// </remarks>
    private static bool IsStale(string source, string marker)
    {
        var seededAt = File.GetLastWriteTimeUtc(marker);
        return Directory
            .EnumerateFiles(source, "*.json")
            .Any(file => File.GetLastWriteTimeUtc(file) > seededAt);
    }

    /// <summary>
    /// Deletes the generated en-US files so the framework writes them again from the current build.
    /// </summary>
    /// <remarks>
    /// English is not shipped as a file: the framework generates it from the defaults compiled into
    /// the localization models. But it generates each file only once and never overwrites it, so
    /// after an upgrade that reworded something, the stale generated file would still win over the
    /// new default -- English, the language the strings are authored in, would be the one language
    /// that never got the correction. Deleting them on a version change is what keeps the generated
    /// cache a cache.
    /// </remarks>
    private void DiscardGeneratedDefaults()
    {
        foreach (var file in Directory.EnumerateFiles(
            LocalizationDirectory,
            "*." + DesktopCulture.DefaultCulture + ".json"))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A file that will not delete is one the framework will read instead. Stale English
                // in one section is not a reason to give up on the rest.
            }
        }
    }
}
