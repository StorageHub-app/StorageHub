using System.Text.Json;
using CodeLogic.Core.Configuration;
using StorageHub.Desktop.Configuration.Legacy;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Configuration;

/// <summary>
/// What happened while the settings files were being made ready, for the shell to tell the user.
/// </summary>
/// <param name="Quarantined">
/// Files moved aside because they could not be trusted, by their new names.
/// </param>
/// <param name="Repaired">Whether any value had to be brought back inside its bounds.</param>
/// <param name="MigratedFrom">
/// The legacy <c>settings.json</c> that was imported, or null when there was nothing to import.
/// </param>
internal sealed record ConfigPreflightReport(
    IReadOnlyList<string> Quarantined,
    bool Repaired,
    string? MigratedFrom)
{
    internal static ConfigPreflightReport Empty { get; } = new([], false, null);

    internal bool HasFindings => Quarantined.Count > 0 || Repaired || MigratedFrom is not null;

    /// <summary>
    /// What happened, in the order it matters to somebody reading it: what was carried over, then
    /// what was thrown away, then what was corrected.
    /// </summary>
    internal IReadOnlyList<string> Describe()
    {
        var findings = new List<string>();
        if (MigratedFrom is { } migrated) findings.Add(Ui.Format(Ui.Dialogs.SettingsMigratedFormat, migrated));
        foreach (var rejected in Quarantined) findings.Add(Ui.Format(Ui.Dialogs.SettingsQuarantinedFormat, rejected));
        if (Repaired) findings.Add(Ui.Dialogs.SettingsRepaired);
        return findings;
    }

    /// <summary>
    /// What the shell tells the user once its window is open, or null when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// A warning when a file was set aside, because settings somebody chose are gone until they
    /// are chosen again; otherwise information.
    /// </remarks>
    internal DialogRequest? ToNotice() => HasFindings
        ? new DialogRequest
        {
            Title = Ui.Dialogs.StartupCheckCaption,
            Message = string.Join(Environment.NewLine + Environment.NewLine, Describe()),
            Severity = Quarantined.Count > 0 ? DialogSeverity.Warning : DialogSeverity.Information
        }
        : null;
}

/// <summary>
/// The desktop's settings, held in CodeLogic configuration models but written by this class.
/// </summary>
/// <remarks>
/// <para>
/// CodeLogic owns the shape of these files and loads them at startup. It does not own the writing.
/// Its <c>SaveAsync</c> is a plain whole-file write with no size limit, no reparse-point check and
/// no atomic replace, and its loader throws on a file it cannot parse -- which for user-editable
/// settings would mean a stray character stopping StorageHub from starting. So
/// <see cref="Preflight"/> screens and repairs every file before CodeLogic is allowed to read it,
/// and <see cref="Save"/> is the only write path. <c>IConfigurationManager.SaveAsync</c> is never
/// called.
/// </para>
/// <para>
/// After <see cref="Preflight"/> this class, not the configuration manager, is the authority on
/// what the settings are: <see cref="Save"/> updates its own snapshot rather than asking CodeLogic
/// to reload, which keeps the whole settings path synchronous on the UI thread. Nothing else reads
/// these models through the manager, so there is no second copy to go stale.
/// </para>
/// <para>
/// One thing the file it replaces did better: that was a single file, so a crash could not leave
/// half a settings change behind. Three files cannot be replaced as one. <see cref="Save"/> writes
/// them in a fixed order and skips the ones whose bytes have not changed, which makes the window
/// small, and the three are independent enough that a torn write costs a preference rather than a
/// broken state.
/// </para>
/// </remarks>
internal sealed class DesktopConfigStore
{
    /// <summary>
    /// The most a single settings file may be. Applied per file rather than to the settings as a
    /// whole, which is a real loosening compared with the one file that came before: three files
    /// means three budgets.
    /// </summary>
    internal const int MaximumFileBytes = 64 * 1024;

    internal const string GeneralFileName = "config.json";
    internal const string ShortcutsFileName = "config.shortcuts.json";
    internal const string WorkspacesFileName = "config.workspaces.json";
    internal const string LegacyFileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directory;
    private readonly Dictionary<string, byte[]> _written = new(StringComparer.OrdinalIgnoreCase);
    private DesktopUpdatePreferences _current = DesktopUpdatePreferences.Defaults;
    private string? _migratedFrom;
    private bool _loaded;

    internal DesktopConfigStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("The desktop settings directory must be absolute.", nameof(directory));
        }

        _directory = Path.GetFullPath(directory);
    }

    /// <summary>Where these settings live, so an import backup lands beside them.</summary>
    internal string Directory => _directory;

    /// <summary>
    /// The general settings file. Named <c>FilePath</c> because callers that want somewhere to put
    /// a backup want a file to sit beside, and this is the one that means "the settings".
    /// </summary>
    internal string FilePath => GeneralPath;

    internal static DesktopConfigStore CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("The current user's local application-data directory is unavailable.");
        }

        return new DesktopConfigStore(Path.Combine(localAppData, "StorageHub", "Desktop"));
    }

    internal string GeneralPath => Path.Combine(_directory, GeneralFileName);

    internal string ShortcutsPath => Path.Combine(_directory, ShortcutsFileName);

    internal string WorkspacesPath => Path.Combine(_directory, WorkspacesFileName);

    internal string LegacyPath => Path.Combine(_directory, LegacyFileName);

    /// <summary>
    /// The settings as they currently stand, reading them from disk the first time if
    /// <see cref="Preflight"/> has not already run.
    /// </summary>
    /// <remarks>
    /// Reading lazily rather than requiring <see cref="Preflight"/> first means that forgetting to
    /// call it costs the repair report, not the user's settings. The read here writes nothing.
    /// </remarks>
    internal DesktopUpdatePreferences Load()
    {
        if (!_loaded)
        {
            _current = DesktopConfigRepair.Repair(ReadAll([]));
            _loaded = true;
        }

        return _current;
    }

    /// <summary>
    /// Makes the settings files safe for CodeLogic to read, and returns what had to be done.
    /// </summary>
    /// <remarks>
    /// Must run before <c>ConfigureAsync</c>. Every path out of it leaves three parseable files on
    /// disk and a usable <see cref="Load"/>, including the paths where the originals were garbage.
    /// </remarks>
    internal ConfigPreflightReport Preflight()
    {
        System.IO.Directory.CreateDirectory(_directory);

        var quarantined = new List<string>();
        var loaded = ReadAll(quarantined);
        var repaired = DesktopConfigRepair.Repair(loaded);

        _current = repaired;
        _loaded = true;

        // Captured before the write, which is what clears it.
        var migratedFrom = _migratedFrom;

        // Written unconditionally: this is what makes all three files parseable for CodeLogic,
        // whatever state they were in a moment ago.
        Write(force: true);

        return new ConfigPreflightReport(
            quarantined,
            DesktopConfigRepair.ChangedAnything(loaded, repaired),
            migratedFrom);
    }

    /// <summary>
    /// The settings as the files on disk describe them, migrating from the legacy file when there
    /// is nothing else to read. Writes nothing.
    /// </summary>
    private DesktopUpdatePreferences ReadAll(List<string> quarantined)
    {
        var general = Read<DesktopConfig>(GeneralPath, quarantined);
        var shortcuts = Read<DesktopShortcutsConfig>(ShortcutsPath, quarantined);
        var workspaces = Read<DesktopWorkspacesConfig>(WorkspacesPath, quarantined);

        // Only when there is no general file left at all, so a user who deletes it to start over
        // gets defaults rather than their old settings back, and so this cannot run twice.
        if (general is null && !File.Exists(GeneralPath))
        {
            var legacy = new LegacySettingsReader(LegacyPath);
            if (legacy.Exists)
            {
                _migratedFrom = LegacyFileName + ".migrated";
                return legacy.Load();
            }
        }

        return Compose(general, shortcuts, workspaces);
    }

    /// <summary>
    /// Persists a complete set of settings, or throws when they are not storable.
    /// </summary>
    internal void Save(DesktopUpdatePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (DesktopConfigRepair.Validate(preferences) is { } invalid)
        {
            throw new ArgumentException(invalid, nameof(preferences));
        }

        _current = DesktopConfigRepair.Repair(preferences);
        Write(force: false);
    }

    /// <summary>
    /// Declares the three models to CodeLogic so the framework's view agrees with ours.
    /// </summary>
    /// <remarks>
    /// The argument to <c>Register</c>, not a <c>ConfigSection</c> attribute, is what names the
    /// file -- the attribute is ignored by the configuration manager.
    /// </remarks>
    internal static void Register(IConfigurationManager configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Register<DesktopConfig>();
        configuration.Register<DesktopShortcutsConfig>("shortcuts");
        configuration.Register<DesktopWorkspacesConfig>("workspaces");
    }

    private static DesktopUpdatePreferences Compose(
        DesktopConfig? general,
        DesktopShortcutsConfig? shortcuts,
        DesktopWorkspacesConfig? workspaces)
    {
        general ??= new DesktopConfig();
        return new DesktopUpdatePreferences(
            general.CheckAutomatically,
            general.DownloadAutomatically,
            general.RestartAutomatically,
            general.IncludePrereleases,
            general.SshHostKeyDiscovery,
            general.ExternalEditorPath,
            general.MaximumEditableFileBytes,
            general.AdaptiveConcurrency,
            general.MinimumConcurrency,
            general.MaximumTransferConcurrency,
            general.PerConnectionConcurrency,
            general.MaximumSyncConcurrency,
            general.Appearance,
            general.WarnBeforeUnsafeExternalEdit,
            general.ConnectionDefaults,
            general.DefaultWorkspaceLayout,
            general.SshTerminal?.ToPreferences(),
            general.ReconnectRemotePanesAutomatically,
            general.ConfirmBeforeClearingTransferHistory,
            general.ConfirmBeforeDeletingItems,
            shortcuts?.Shortcuts is null ? null : ShortcutChord.Parse(shortcuts.Shortcuts),
            workspaces?.Pinned?.Select(entry => entry.ToEntry()).ToArray(),
            workspaces?.Recent?.Select(entry => entry.ToEntry()).ToArray(),
            general.DefaultWorkspacePaneCount,
            general.ConnectionsPanelWidth,
            general.ConnectionsPanelVisible,
            general.ConnectionsPanelSide,
            general.Language,
            // Named, because the parameters between here and the end of the record are not
            // persisted by this store and must keep their defaults.
            ToolbarItems: general.ToolbarItems,
            ToolbarLabels: Enum.IsDefined(general.ToolbarLabels)
                ? general.ToolbarLabels
                : ToolbarLabelStyle.IconsOnly,
            ConnectionGroups: ReadGroups(general.ConnectionGroups),
            TotalUploadBytesPerSecond: general.TotalUploadBytesPerSecond,
            TotalDownloadBytesPerSecond: general.TotalDownloadBytesPerSecond);
    }

    /// <summary>
    /// Turns the saved groups into an arrangement, dropping anything that is not one.
    /// </summary>
    /// <remarks>
    /// A member that will not parse as an id is skipped rather than failing the load: the panel
    /// reconciles its groups against the agent's connections anyway, so an unrecognisable id is
    /// already something it knows how to survive.
    /// </remarks>
    private static List<ConnectionGroupEntry>? ReadGroups(List<DesktopConnectionGroup>? saved)
    {
        if (saved is null) return null;

        var groups = new List<ConnectionGroupEntry>();
        foreach (var group in saved)
        {
            if (string.IsNullOrWhiteSpace(group.Name)) continue;
            var members = (group.Members ?? [])
                .Select(static member => Guid.TryParse(member, out var id) ? id : Guid.Empty)
                .Where(static id => id != Guid.Empty)
                .ToArray();
            groups.Add(new ConnectionGroupEntry(group.Name.Trim(), members));
        }

        return groups;
    }

    private void Write(bool force)
    {
        // Saving into a directory that does not exist yet is ordinary on a first run, so this is
        // part of writing rather than something the caller has to have done first.
        System.IO.Directory.CreateDirectory(_directory);

        var preferences = _current;
        var general = new DesktopConfig
        {
            CheckAutomatically = preferences.CheckAutomatically,
            DownloadAutomatically = preferences.DownloadAutomatically,
            RestartAutomatically = preferences.RestartAutomatically,
            IncludePrereleases = preferences.IncludePrereleases,
            SshHostKeyDiscovery = preferences.SshHostKeyDiscovery,
            ExternalEditorPath = preferences.ExternalEditorPath,
            MaximumEditableFileBytes = preferences.MaximumEditableFileBytes,
            AdaptiveConcurrency = preferences.AdaptiveConcurrency,
            MinimumConcurrency = preferences.MinimumConcurrency,
            MaximumTransferConcurrency = preferences.MaximumTransferConcurrency,
            PerConnectionConcurrency = preferences.PerConnectionConcurrency,
            MaximumSyncConcurrency = preferences.MaximumSyncConcurrency,
            Appearance = preferences.Appearance,
            Language = preferences.Language,
            WarnBeforeUnsafeExternalEdit = preferences.WarnBeforeUnsafeExternalEdit,
            ConnectionDefaults = preferences.ConnectionDefaults is null
                ? null
                : ConnectionDefaultSettings.Normalize(preferences.ConnectionDefaults),
            DefaultWorkspaceLayout = preferences.DefaultWorkspaceLayout,
            SshTerminal = preferences.SshTerminal is null
                ? null
                : DesktopSshTerminalConfig.From(preferences.SshTerminal),
            ReconnectRemotePanesAutomatically = preferences.ReconnectRemotePanesAutomatically,
            ConfirmBeforeClearingTransferHistory = preferences.ConfirmBeforeClearingTransferHistory,
            ConfirmBeforeDeletingItems = preferences.ConfirmBeforeDeletingItems,
            DefaultWorkspacePaneCount = preferences.DefaultWorkspacePaneCount,
            ConnectionsPanelWidth = preferences.ConnectionsPanelWidth,
            ConnectionsPanelVisible = preferences.ConnectionsPanelVisible,
            ConnectionsPanelSide = preferences.ConnectionsPanelSide,
            // Sanitised on the way out as well as in, so a layout edited by hand cannot
            // persist entries the toolbar would silently drop on every load.
            ToolbarItems = preferences.ToolbarItems is null
                ? null
                : ToolbarLayout.Sanitise(preferences.ToolbarItems).ToList(),
            ToolbarLabels = preferences.ToolbarLabels,
            ConnectionGroups = preferences.ConnectionGroups?
                .Select(static group => new DesktopConnectionGroup
                {
                    Name = group.Name,
                    Members = [.. group.Members.Select(static id => id.ToString("D"))]
                })
                .ToList(),
            TotalUploadBytesPerSecond = preferences.TotalUploadBytesPerSecond,
            TotalDownloadBytesPerSecond = preferences.TotalDownloadBytesPerSecond
        };

        var shortcuts = new DesktopShortcutsConfig
        {
            Shortcuts = preferences.Shortcuts is null ? null : ShortcutChord.Format(preferences.Shortcuts)
        };

        var workspaces = new DesktopWorkspacesConfig
        {
            Pinned = preferences.PinnedWorkspaces?.Select(DesktopWorkspaceEntry.From).ToList(),
            Recent = preferences.RecentWorkspaces?.Select(DesktopWorkspaceEntry.From).ToList()
        };

        // Fixed order, general first: it is the file that matters most, so it is the one written
        // while the least can have gone wrong.
        WriteFile(GeneralPath, general, force);
        WriteFile(ShortcutsPath, shortcuts, force);
        WriteFile(WorkspacesPath, workspaces, force);

        // Retired only once its contents are safely in the new files, so a failed write leaves the
        // legacy file where the next start will find it again.
        if (_migratedFrom is not null)
        {
            RetireLegacyFile();
            _migratedFrom = null;
        }
    }

    private void WriteFile<T>(string path, T model, bool force)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(model, JsonOptions);
        if (payload.Length > MaximumFileBytes)
        {
            throw new ArgumentException(Ui.Validation.TheseSettingsAreTooLargeToExport, nameof(model));
        }

        if (!force &&
            _written.TryGetValue(path, out var previous) &&
            previous.AsSpan().SequenceEqual(payload))
        {
            return;
        }

        RejectReparsePoint(path);
        var temporaryPath = Path.Combine(
            _directory,
            string.Concat(".", Path.GetFileName(path), ".", Guid.NewGuid().ToString("N"), ".tmp"));
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            RejectReparsePoint(path);
            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }

            _written[path] = payload;
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The settings file is already committed. A uniquely named leftover holds no
                // secrets and can be scavenged later.
            }
        }
    }

    /// <summary>
    /// Reads one settings file, moving it aside and returning null when it cannot be trusted.
    /// </summary>
    private T? Read<T>(string path, List<string> quarantined)
        where T : class
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return null;
            }

            if (file.Length is <= 0 or > MaximumFileBytes || IsReparsePoint(file))
            {
                quarantined.Add(Quarantine(path));
                return null;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            var model = JsonSerializer.Deserialize<T>(stream, JsonOptions);
            if (model is null)
            {
                quarantined.Add(Quarantine(path));
            }

            return model;
        }
        catch (Exception error) when (error is
            IOException or
            UnauthorizedAccessException or
            JsonException or
            NotSupportedException)
        {
            quarantined.Add(Quarantine(path));
            return null;
        }
    }

    /// <summary>
    /// Moves an untrustworthy settings file out of the way, keeping it so the user can see what
    /// was in it, and returns the name it now has.
    /// </summary>
    private string Quarantine(string path)
    {
        var name = string.Concat(
            Path.GetFileName(path),
            ".rejected-",
            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            File.Move(path, Path.Combine(_directory, name), overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // It could not be moved, so it is about to be overwritten instead. Losing a file that
            // was already unreadable is a smaller harm than refusing to start.
        }

        return name;
    }

    private void RetireLegacyFile()
    {
        try
        {
            File.Move(LegacyPath, Path.Combine(_directory, LegacyFileName + ".migrated"), overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The new files are already written. Leaving the old one in place is harmless: the
            // migration only runs when config.json is absent, which it no longer is.
        }
    }

    private static bool IsReparsePoint(FileSystemInfo file) =>
        (file.Attributes & FileAttributes.ReparsePoint) != 0;

    private static void RejectReparsePoint(string path)
    {
        if (File.Exists(path) && IsReparsePoint(new FileInfo(path)))
        {
            throw new IOException(Ui.Validation.StorageHubWillNotReadSettingsThroughA);
        }
    }
}
