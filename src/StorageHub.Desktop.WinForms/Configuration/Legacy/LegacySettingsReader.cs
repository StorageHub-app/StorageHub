using System.Text.Json;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Configuration.Legacy;

/// <summary>
/// Reads the <c>settings.json</c> that StorageHub wrote before its settings moved to CodeLogic
/// configuration, so that an existing installation keeps everything it had.
/// </summary>
/// <remarks>
/// <para>
/// This is the original store's <c>Load</c>, unchanged, including its full ladder from schema
/// version 1 to 15. It was moved rather than rewritten on purpose: fifteen versions of defaulting
/// rules are exactly the kind of thing a rewrite gets subtly wrong, and every one of them is
/// already covered by tests. Nothing here will gain a version 16 -- new settings evolve
/// <see cref="DesktopConfigSchema"/> instead.
/// </para>
/// <para>
/// The write half is deliberately absent. After migration this file is renamed aside and never
/// written again; see <see cref="DesktopConfigStore"/>.
/// </para>
/// </remarks>
internal static class LegacySettingsSchema
{
    /// <summary>
    /// The last version this file shape ever reached. Frozen: the ladder above stops here, and
    /// anything newer belongs to <see cref="DesktopConfigSchema"/>.
    /// </summary>
    internal const int Final = 15;
}

internal sealed class LegacySettingsReader
{
    /// <summary>
    /// A settings file larger than this is discarded rather than read, unchanged from the store
    /// this came from.
    /// </summary>
    internal const int MaximumSettingsBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    internal LegacySettingsReader(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!Path.IsPathFullyQualified(filePath))
        {
            throw new ArgumentException("The desktop settings path must be absolute.", nameof(filePath));
        }

        _filePath = Path.GetFullPath(filePath);
    }

    internal string FilePath => _filePath;

    /// <summary>Whether there is a legacy file here worth reading at all.</summary>
    internal bool Exists => File.Exists(_filePath);

    internal DesktopUpdatePreferences Load()
    {
        try
        {
            var file = new FileInfo(_filePath);
            if (!file.Exists || file.Length is <= 0 or > MaximumSettingsBytes || IsReparsePoint(file))
            {
                return DesktopUpdatePreferences.Defaults;
            }

            using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            var document = JsonSerializer.Deserialize<DesktopUpdatePreferencesDocument>(stream, JsonOptions);
            if (document is null || document.SchemaVersion is < 1 or > LegacySettingsSchema.Final)
            {
                return DesktopUpdatePreferences.Defaults;
            }

            var discoveryMode = document.SchemaVersion == 1
                ? SshHostKeyDiscoveryMode.AskBeforeFetching
                : document.SshHostKeyDiscovery;
            var editorPath = document.SchemaVersion >= 3 && IsValidEditorPath(document.ExternalEditorPath)
                ? document.ExternalEditorPath
                : null;
            var maximumEditableBytes = document.SchemaVersion >= 3 &&
                document.MaximumEditableFileBytes is >= 1 and <= EditableFileIpcContract.MaximumContentBytes
                    ? document.MaximumEditableFileBytes
                    : EditableFileIpcContract.MaximumContentBytes;
            var concurrencyIsValid = document.SchemaVersion >= 4 &&
                document.MinimumConcurrency is >= 1 and <= 8 &&
                document.MaximumTransferConcurrency is >= 1 and <= 32 &&
                document.MinimumConcurrency <= document.MaximumTransferConcurrency &&
                document.PerConnectionConcurrency is >= 1 and <= 16 &&
                document.MaximumSyncConcurrency is >= 1 and <= 8 &&
                document.MinimumConcurrency <= document.MaximumSyncConcurrency;
            return Enum.IsDefined(discoveryMode)
                ? new DesktopUpdatePreferences(
                    document.CheckAutomatically,
                    document.DownloadAutomatically,
                    document.RestartAutomatically,
                    document.IncludePrereleases,
                    discoveryMode,
                    editorPath,
                    maximumEditableBytes,
                    concurrencyIsValid ? document.AdaptiveConcurrency : DesktopUpdatePreferences.Defaults.AdaptiveConcurrency,
                    concurrencyIsValid ? document.MinimumConcurrency : DesktopUpdatePreferences.Defaults.MinimumConcurrency,
                    concurrencyIsValid ? document.MaximumTransferConcurrency : DesktopUpdatePreferences.Defaults.MaximumTransferConcurrency,
                    concurrencyIsValid ? document.PerConnectionConcurrency : DesktopUpdatePreferences.Defaults.PerConnectionConcurrency,
                    concurrencyIsValid ? document.MaximumSyncConcurrency : DesktopUpdatePreferences.Defaults.MaximumSyncConcurrency,
                    document.SchemaVersion >= 5 && Enum.IsDefined(document.Appearance)
                        ? document.Appearance : DesktopAppearance.System,
                    document.WarnBeforeUnsafeExternalEdit,
                    document.SchemaVersion >= 6
                        ? document.ConnectionDefaults is null
                            ? null
                            : ConnectionDefaultSettings.Normalize(document.ConnectionDefaults)
                        : null,
                    document.SchemaVersion >= 7 && Enum.IsDefined(document.DefaultWorkspaceLayout)
                        ? document.DefaultWorkspaceLayout
                        : WorkspaceLayout.SideBySide,
                    document.SchemaVersion >= 8 && document.SshTerminal is not null
                        ? SshTerminalPreferences.Resolve(document.SshTerminal)
                        : null,
                    document.SchemaVersion < 9 || document.ReconnectRemotePanesAutomatically,
                    document.SchemaVersion < 10 || document.ConfirmBeforeClearingTransferHistory,
                    document.SchemaVersion < 11 || document.ConfirmBeforeDeletingItems,
                    document.SchemaVersion >= 12 && document.Shortcuts is not null
                        ? ShortcutSettings.Resolve(document.Shortcuts) : null,
                    document.SchemaVersion >= 13 && document.PinnedWorkspaces is not null
                        ? WorkspaceShortcutSettings.Resolve(
                            document.PinnedWorkspaces, WorkspaceShortcutSettings.MaximumPinned)
                        : null,
                    document.SchemaVersion >= 13 && document.RecentWorkspaces is not null
                        ? WorkspaceShortcutSettings.Resolve(
                            document.RecentWorkspaces, WorkspaceShortcutSettings.MaximumRecent)
                        : null,
                    document.SchemaVersion >= 14 &&
                        document.DefaultWorkspacePaneCount is >= 1 and <= WorkspaceLayoutModel.MaximumPanes
                            ? document.DefaultWorkspacePaneCount
                            : null,
                    document.SchemaVersion >= 15 &&
                        document.ConnectionsPanelWidth is
                            >= DesktopUpdatePreferences.MinimumConnectionsPanelWidth and
                            <= DesktopUpdatePreferences.MaximumConnectionsPanelWidth
                                ? document.ConnectionsPanelWidth
                                : DesktopUpdatePreferences.DefaultConnectionsPanelWidth,
                    document.SchemaVersion < 15 || document.ConnectionsPanelVisible,
                    document.SchemaVersion >= 15 && Enum.IsDefined(document.ConnectionsPanelSide)
                        ? document.ConnectionsPanelSide
                        : ConnectionsPanelSide.Left)
                : DesktopUpdatePreferences.Defaults;
        }
        catch (Exception error) when (error is
            IOException or
            UnauthorizedAccessException or
            JsonException or
            NotSupportedException)
        {
            return DesktopUpdatePreferences.Defaults;
        }
    }

    private static bool IsReparsePoint(FileSystemInfo file) =>
        (file.Attributes & FileAttributes.ReparsePoint) != 0;

    /// <summary>
    /// Whether an external editor path is storable. Internal so the settings importer applies the
    /// same rule rather than a lookalike of its own.
    /// </summary>
    internal static bool IsValidEditorPath(string? value) => value is null ||
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 2_048 &&
        !value.Any(char.IsControl) &&
        Path.IsPathFullyQualified(value);

    private sealed record DesktopUpdatePreferencesDocument(
        int SchemaVersion,
        bool CheckAutomatically,
        bool DownloadAutomatically,
        bool RestartAutomatically,
        bool IncludePrereleases,
        SshHostKeyDiscoveryMode SshHostKeyDiscovery = default,
        string? ExternalEditorPath = null,
        int MaximumEditableFileBytes = EditableFileIpcContract.MaximumContentBytes,
        bool AdaptiveConcurrency = true,
        int MinimumConcurrency = 1,
        int MaximumTransferConcurrency = 4,
        int PerConnectionConcurrency = 2,
        int MaximumSyncConcurrency = 2,
        DesktopAppearance Appearance = DesktopAppearance.System,
        bool WarnBeforeUnsafeExternalEdit = true,
        Dictionary<string, string>? ConnectionDefaults = null,
        WorkspaceLayout DefaultWorkspaceLayout = WorkspaceLayout.SideBySide,
        SshTerminalPreferences? SshTerminal = null,
        bool ReconnectRemotePanesAutomatically = true,
        bool ConfirmBeforeClearingTransferHistory = true,
        bool ConfirmBeforeDeletingItems = true,
        Dictionary<string, Keys>? Shortcuts = null,
        List<WorkspaceShortcutEntry>? PinnedWorkspaces = null,
        List<WorkspaceShortcutEntry>? RecentWorkspaces = null,
        int? DefaultWorkspacePaneCount = null,
        int ConnectionsPanelWidth = 300,
        bool ConnectionsPanelVisible = true,
        ConnectionsPanelSide ConnectionsPanelSide = ConnectionsPanelSide.Left);
}
