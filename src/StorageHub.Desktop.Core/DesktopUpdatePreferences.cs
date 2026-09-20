using Avalonia.Input;
using System.Text.Json;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

public enum DesktopAppearance
{
    Light = 1,
    Dark = 2,
    System = 3
}

public enum SshHostKeyDiscoveryMode
{
    Manual = 1,
    AskBeforeFetching = 2,
    Automatic = 3
}

public enum WorkspaceLayout
{
    SideBySide = 1,
    TopAndBottom = 2
}

/// <summary>Which side of the shell the saved-connections panel is docked to.</summary>
public enum ConnectionsPanelSide
{
    Left = 1,
    Right = 2
}

internal sealed record SshTerminalPreferences(
    string TerminalName = "xterm-256color",
    string? StartupCommand = null,
    int KeepAliveSeconds = 30,
    string FontFamily = "Cascadia Mono",
    float FontSize = 10F,
    int ScrollbackLines = 2_000,
    int RefreshIntervalMilliseconds = 60,
    bool RenderBoldText = true)
{
    internal const int MaximumStartupCommandLength = 512;
    internal static SshTerminalPreferences Defaults { get; } = new();

    internal static SshTerminalPreferences Resolve(SshTerminalPreferences? value)
    {
        value ??= Defaults;
        var terminalName = IsTerminalName(value.TerminalName)
            ? value.TerminalName.Trim()
            : Defaults.TerminalName;
        var startupCommand = string.IsNullOrWhiteSpace(value.StartupCommand)
            ? null
            : IsStartupCommand(value.StartupCommand)
                ? value.StartupCommand.Trim()
                : null;
        var fontFamily = IsFontFamily(value.FontFamily)
            ? value.FontFamily.Trim()
            : Defaults.FontFamily;
        return new SshTerminalPreferences(
            terminalName,
            startupCommand,
            value.KeepAliveSeconds is >= 0 and <= 3_600
                ? value.KeepAliveSeconds : Defaults.KeepAliveSeconds,
            fontFamily,
            float.IsFinite(value.FontSize) && value.FontSize is >= 6F and <= 32F
                ? value.FontSize : Defaults.FontSize,
            value.ScrollbackLines is >= 100 and <= 20_000
                ? value.ScrollbackLines : Defaults.ScrollbackLines,
            value.RefreshIntervalMilliseconds is >= 16 and <= 500
                ? value.RefreshIntervalMilliseconds : Defaults.RefreshIntervalMilliseconds,
            value.RenderBoldText);
    }

    private static bool IsTerminalName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= SshTerminalIpcContract.MaximumTerminalNameLength &&
        !value.Any(static character => char.IsControl(character) || char.IsWhiteSpace(character));

    private static bool IsStartupCommand(string value) =>
        value.Length <= MaximumStartupCommandLength &&
        !value.Any(char.IsControl);

    private static bool IsFontFamily(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        !value.Any(char.IsControl);
}

internal sealed record DesktopUpdatePreferences(
    bool CheckAutomatically = true,
    bool DownloadAutomatically = true,
    bool RestartAutomatically = false,
    bool IncludePrereleases = true,
    SshHostKeyDiscoveryMode SshHostKeyDiscovery = SshHostKeyDiscoveryMode.AskBeforeFetching,
    string? ExternalEditorPath = null,
    int MaximumEditableFileBytes = EditableFileIpcContract.MaximumContentBytes,
    bool AdaptiveConcurrency = true,
    int MinimumConcurrency = 1,
    int MaximumTransferConcurrency = 4,
    int PerConnectionConcurrency = 2,
    int MaximumSyncConcurrency = 2,
    DesktopAppearance Appearance = DesktopAppearance.System,
    bool WarnBeforeUnsafeExternalEdit = true,
    IReadOnlyDictionary<string, string>? ConnectionDefaults = null,
    WorkspaceLayout DefaultWorkspaceLayout = WorkspaceLayout.SideBySide,
    SshTerminalPreferences? SshTerminal = null,
    bool ReconnectRemotePanesAutomatically = true,
    bool ConfirmBeforeClearingTransferHistory = true,
    bool ConfirmBeforeDeletingItems = true,
    IReadOnlyDictionary<string, KeyGesture?>? Shortcuts = null,
    IReadOnlyList<WorkspaceShortcutEntry>? PinnedWorkspaces = null,
    IReadOnlyList<WorkspaceShortcutEntry>? RecentWorkspaces = null,
    /// <summary>
    /// Panes to give a new workspace without asking. Null means ask each time, which is how new
    /// workspaces have always behaved and what Settings calls "Ask every time".
    /// </summary>
    int? DefaultWorkspacePaneCount = null,
    /// <summary>
    /// Width in pixels of the connections panel, measured on whichever side it is docked to rather
    /// than as a splitter position, so moving it across keeps its size.
    /// </summary>
    int ConnectionsPanelWidth = 300,
    bool ConnectionsPanelVisible = true,
    ConnectionsPanelSide ConnectionsPanelSide = ConnectionsPanelSide.Left,
    /// <summary>
    /// Which language the shell speaks: <c>auto</c> to follow Windows, or a shipped culture name.
    /// Never persisted by the legacy store, so an upgraded installation starts on auto.
    /// </summary>
    string Language = DesktopCulture.AutomaticLanguage,
    /// <summary>
    /// Whether a favourite is also listed under its own folder rather than only under Favourites.
    /// On by default: a favourite that vanishes from its folder is confusing when the folder is
    /// how you think about it, but with many folders the repetition adds up, so it can be turned
    /// off. Last in the list so adding it left every existing positional construction alone.
    /// </summary>
    bool ShowFavoritesInTheirFolders = true,
    /// <summary>
    /// Icons chosen for connection folders, keyed by the sidebar's group key ("storage/Team").
    /// Folders are derived from each connection's FolderPath rather than stored anywhere, so
    /// unlike a connection's own icon there is no profile to hang this on.
    /// </summary>
    IReadOnlyDictionary<string, string>? FolderIcons = null,
    /// <summary>
    /// How the connections panel is organised: groups somebody made, in the order they put them,
    /// each holding connection ids. Null means nothing has been arranged, and the panel groups by
    /// each connection's folder path -- which is what organised the sidebar before groups existed,
    /// so an upgraded installation reappears the way it was left.
    /// </summary>
    IReadOnlyList<ConnectionGroupEntry>? ConnectionGroups = null,
    /// <summary>
    /// The main toolbar's buttons, as command ids with "|" for a divider. Null means nothing was
    /// customised and the default preset is shown; an empty list means the same, because somebody
    /// who removes every button should get the toolbar back rather than lose the route to Settings
    /// that would let them undo it.
    /// </summary>
    IReadOnlyList<string>? ToolbarItems = null,
    /// <summary>
    /// Whether toolbar buttons show their label. Icons only is how the toolbar has always looked,
    /// so it stays the default.
    /// </summary>
    ToolbarLabelStyle ToolbarLabels = ToolbarLabelStyle.IconsOnly,
    /// <summary>
    /// The colour scheme's id, or null to follow <see cref="Appearance"/> with the house schemes.
    /// </summary>
    /// <remarks>
    /// Null rather than "storagehub-dark" so an installation that has never opened Settings keeps
    /// following the system, which is what it did before schemes existed. Last in the list, as
    /// every addition to this record has to be.
    /// </remarks>
    string? ColorScheme = null)
{
    /// <summary>Kept in step with the <c>ConnectionsPanelWidth</c> parameter default above.</summary>
    internal const int DefaultConnectionsPanelWidth = 300;
    internal const int MinimumConnectionsPanelWidth = 220;
    internal const int MaximumConnectionsPanelWidth = 640;

    public static DesktopUpdatePreferences Defaults { get; } = new();
}
