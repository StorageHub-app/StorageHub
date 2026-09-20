using Avalonia.Input;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>The top-level menus, in the order they appear.</summary>
internal enum UiMenuId
{
    Workspace,
    Edit,
    View,
    Go,
    Connections,
    Transfer,
    Sync,
    Tools,
    Help,
}

/// <summary>
/// A command as declared: identity and presentation, with its text still to be resolved.
/// </summary>
/// <remarks>
/// Label and description are selectors rather than strings so that the text can follow the
/// current language while the identity cannot. Picking the wrong property is a compile error,
/// which is what the previous dictionary-keyed-by-English-label could not offer.
/// </remarks>
internal sealed record UiCommandSpec(
    string Id,
    UiMenuId Menu,
    Func<CommandStrings, string> Label,
    Func<CommandStrings, string> Description,
    KeyGesture? Shortcut = null,
    UiGlyph? Glyph = null,
    UiIconTone Tone = UiIconTone.Text);

/// <summary>A command with its text resolved for the current language.</summary>
internal sealed record UiCommandDefinition(
    string Id,
    UiMenuId Menu,
    string Label,
    string Description,
    KeyGesture? Shortcut = null,
    UiGlyph? Glyph = null,
    UiIconTone Tone = UiIconTone.Text);

internal static class UiCommandCatalog
{
    /// <summary>
    /// Every command, declared once. Order is the order they appear in their menu.
    /// </summary>
    internal static IReadOnlyList<UiCommandSpec> Specs { get; } =
    [
        new(
            UiCommandIds.WorkspaceNewWorkspace,
            UiMenuId.Workspace,
            static strings => strings.WorkspaceNewWorkspace,
            static strings => strings.WorkspaceNewWorkspaceDescription,
            new KeyGesture(Key.T, KeyModifiers.Control),
            UiGlyph.Add,
            UiIconTone.Primary),
        new(
            UiCommandIds.WorkspaceOpenWorkspace,
            UiMenuId.Workspace,
            static strings => strings.WorkspaceOpenWorkspace,
            static strings => strings.WorkspaceOpenWorkspaceDescription,
            new KeyGesture(Key.O, KeyModifiers.Control),
            UiGlyph.Folder,
            UiIconTone.Text),
        new(
            UiCommandIds.WorkspaceSaveWorkspace,
            UiMenuId.Workspace,
            static strings => strings.WorkspaceSaveWorkspace,
            static strings => strings.WorkspaceSaveWorkspaceDescription,
            new KeyGesture(Key.S, KeyModifiers.Control),
            UiGlyph.Save,
            UiIconTone.Text),
        new(
            UiCommandIds.WorkspaceSaveWorkspaceAs,
            UiMenuId.Workspace,
            static strings => strings.WorkspaceSaveWorkspaceAs,
            static strings => strings.WorkspaceSaveWorkspaceAsDescription,
            new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift),
            UiGlyph.Save,
            UiIconTone.Text),
        new(
            UiCommandIds.WorkspaceRenameWorkspace,
            UiMenuId.Workspace,
            static strings => strings.WorkspaceRenameWorkspace,
            static strings => strings.WorkspaceRenameWorkspaceDescription,
            null,
            UiGlyph.Rename,
            UiIconTone.Text),
        new(
            UiCommandIds.WorkspaceCloseWorkspace,
            UiMenuId.Workspace,
            static strings => strings.WorkspaceCloseWorkspace,
            static strings => strings.WorkspaceCloseWorkspaceDescription,
            new KeyGesture(Key.W, KeyModifiers.Control),
            UiGlyph.Close,
            UiIconTone.Text),
        new(
            UiCommandIds.WorkspaceExit,
            UiMenuId.Workspace,
            static strings => strings.WorkspaceExit,
            static strings => strings.WorkspaceExitDescription,
            null,
            UiGlyph.Exit,
            UiIconTone.Text),
        new(
            UiCommandIds.EditNewFolder,
            UiMenuId.Edit,
            static strings => strings.EditNewFolder,
            static strings => strings.EditNewFolderDescription,
            new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Shift),
            UiGlyph.Folder,
            UiIconTone.Text),
        new(
            UiCommandIds.EditNewEmptyFile,
            UiMenuId.Edit,
            static strings => strings.EditNewEmptyFile,
            static strings => strings.EditNewEmptyFileDescription,
            new KeyGesture(Key.N, KeyModifiers.Control | KeyModifiers.Alt),
            UiGlyph.File,
            UiIconTone.Text),
        new(
            UiCommandIds.EditCut,
            UiMenuId.Edit,
            static strings => strings.EditCut,
            static strings => strings.EditCutDescription,
            new KeyGesture(Key.X, KeyModifiers.Control),
            UiGlyph.Cut,
            UiIconTone.Text),
        new(
            UiCommandIds.EditCopy,
            UiMenuId.Edit,
            static strings => strings.EditCopy,
            static strings => strings.EditCopyDescription,
            new KeyGesture(Key.C, KeyModifiers.Control),
            UiGlyph.Copy,
            UiIconTone.Text),
        new(
            UiCommandIds.EditPaste,
            UiMenuId.Edit,
            static strings => strings.EditPaste,
            static strings => strings.EditPasteDescription,
            new KeyGesture(Key.V, KeyModifiers.Control),
            UiGlyph.Paste,
            UiIconTone.Text),
        new(
            UiCommandIds.EditRename,
            UiMenuId.Edit,
            static strings => strings.EditRename,
            static strings => strings.EditRenameDescription,
            new KeyGesture(Key.F2),
            UiGlyph.Rename,
            UiIconTone.Text),
        new(
            UiCommandIds.EditBatchRename,
            UiMenuId.Edit,
            static strings => strings.EditBatchRename,
            static strings => strings.EditBatchRenameDescription,
            null,
            UiGlyph.Profiles,
            UiIconTone.Text),
        new(
            UiCommandIds.EditDelete,
            UiMenuId.Edit,
            static strings => strings.EditDelete,
            static strings => strings.EditDeleteDescription,
            new KeyGesture(Key.Delete),
            UiGlyph.Delete,
            UiIconTone.Danger),
        new(
            UiCommandIds.EditSelectAll,
            UiMenuId.Edit,
            static strings => strings.EditSelectAll,
            static strings => strings.EditSelectAllDescription,
            new KeyGesture(Key.A, KeyModifiers.Control),
            UiGlyph.SelectAll,
            UiIconTone.Text),
        new(
            UiCommandIds.EditInvertSelection,
            UiMenuId.Edit,
            static strings => strings.EditInvertSelection,
            static strings => strings.EditInvertSelectionDescription,
            new KeyGesture(Key.I, KeyModifiers.Control),
            UiGlyph.Invert,
            UiIconTone.Text),
        new(
            UiCommandIds.EditProperties,
            UiMenuId.Edit,
            static strings => strings.EditProperties,
            static strings => strings.EditPropertiesDescription,
            new KeyGesture(Key.Enter, KeyModifiers.Alt),
            UiGlyph.Properties,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewRefresh,
            UiMenuId.View,
            static strings => strings.ViewRefresh,
            static strings => strings.ViewRefreshDescription,
            new KeyGesture(Key.F5),
            UiGlyph.Refresh,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewConnectionsPanel,
            UiMenuId.View,
            static strings => strings.ViewConnectionsPanel,
            static strings => strings.ViewConnectionsPanelDescription,
            new KeyGesture(Key.B, KeyModifiers.Control),
            UiGlyph.Connections,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewMoveConnectionsPanel,
            UiMenuId.View,
            static strings => strings.ViewMoveConnectionsPanel,
            static strings => strings.ViewMoveConnectionsPanelDescription,
            null,
            UiGlyph.Layers,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewDirectoryTree,
            UiMenuId.View,
            static strings => strings.ViewDirectoryTree,
            static strings => strings.ViewDirectoryTreeDescription,
            null,
            UiGlyph.Tree,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewTransferQueue,
            UiMenuId.View,
            static strings => strings.ViewTransferQueue,
            static strings => strings.ViewTransferQueueDescription,
            null,
            UiGlyph.Queue,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewSessionLog,
            UiMenuId.View,
            static strings => strings.ViewSessionLog,
            static strings => strings.ViewSessionLogDescription,
            null,
            UiGlyph.Log,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewHiddenFiles,
            UiMenuId.View,
            static strings => strings.ViewHiddenFiles,
            static strings => strings.ViewHiddenFilesDescription,
            null,
            UiGlyph.Hidden,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewTheme,
            UiMenuId.View,
            static strings => strings.ViewTheme,
            static strings => strings.ViewThemeDescription,
            null,
            UiGlyph.Theme,
            UiIconTone.Text),
        new(
            UiCommandIds.GoBack,
            UiMenuId.Go,
            static strings => strings.GoBack,
            static strings => strings.GoBackDescription,
            new KeyGesture(Key.Left, KeyModifiers.Alt),
            UiGlyph.Back,
            UiIconTone.Text),
        new(
            UiCommandIds.GoForward,
            UiMenuId.Go,
            static strings => strings.GoForward,
            static strings => strings.GoForwardDescription,
            new KeyGesture(Key.Right, KeyModifiers.Alt),
            UiGlyph.Forward,
            UiIconTone.Text),
        new(
            UiCommandIds.GoUp,
            UiMenuId.Go,
            static strings => strings.GoUp,
            static strings => strings.GoUpDescription,
            new KeyGesture(Key.Up, KeyModifiers.Alt),
            UiGlyph.Up,
            UiIconTone.Text),
        new(
            UiCommandIds.GoFocusAddress,
            UiMenuId.Go,
            static strings => strings.GoFocusAddress,
            static strings => strings.GoFocusAddressDescription,
            new KeyGesture(Key.L, KeyModifiers.Control),
            UiGlyph.Link,
            UiIconTone.Text),
        new(
            UiCommandIds.GoNextPane,
            UiMenuId.Go,
            static strings => strings.GoNextPane,
            static strings => strings.GoNextPaneDescription,
            new KeyGesture(Key.F6),
            UiGlyph.Layers,
            UiIconTone.Text),
        new(
            UiCommandIds.GoHome,
            UiMenuId.Go,
            static strings => strings.GoHome,
            static strings => strings.GoHomeDescription,
            null,
            UiGlyph.Home,
            UiIconTone.Text),
        new(
            UiCommandIds.GoHistory,
            UiMenuId.Go,
            static strings => strings.GoHistory,
            static strings => strings.GoHistoryDescription,
            null,
            UiGlyph.History,
            UiIconTone.Text),
        new(
            UiCommandIds.GoFavorites,
            UiMenuId.Go,
            static strings => strings.GoFavorites,
            static strings => strings.GoFavoritesDescription,
            null,
            UiGlyph.Favorite,
            UiIconTone.Warning),
        new(
            UiCommandIds.ConnectionsNewConnection,
            UiMenuId.Connections,
            static strings => strings.ConnectionsNewConnection,
            static strings => strings.ConnectionsNewConnectionDescription,
            new KeyGesture(Key.M, KeyModifiers.Control | KeyModifiers.Shift),
            UiGlyph.Add,
            UiIconTone.Text),
        new(
            UiCommandIds.ConnectionsKeyStore,
            UiMenuId.Connections,
            static strings => strings.ConnectionsKeyStore,
            static strings => strings.ConnectionsKeyStoreDescription,
            new KeyGesture(Key.K, KeyModifiers.Control | KeyModifiers.Shift),
            UiGlyph.Key,
            UiIconTone.Text),
        new(
            UiCommandIds.ConnectionsQuickConnect,
            UiMenuId.Connections,
            static strings => strings.ConnectionsQuickConnect,
            static strings => strings.ConnectionsQuickConnectDescription,
            new KeyGesture(Key.K, KeyModifiers.Control),
            UiGlyph.Connect,
            UiIconTone.Text),
        new(
            UiCommandIds.ConnectionsReconnect,
            UiMenuId.Connections,
            static strings => strings.ConnectionsReconnect,
            static strings => strings.ConnectionsReconnectDescription,
            null,
            UiGlyph.Refresh,
            UiIconTone.Text),
        new(
            UiCommandIds.ConnectionsDisconnect,
            UiMenuId.Connections,
            static strings => strings.ConnectionsDisconnect,
            static strings => strings.ConnectionsDisconnectDescription,
            null,
            UiGlyph.Disconnect,
            UiIconTone.Text),
        new(
            UiCommandIds.ConnectionsTestConnection,
            UiMenuId.Connections,
            static strings => strings.ConnectionsTestConnection,
            static strings => strings.ConnectionsTestConnectionDescription,
            null,
            UiGlyph.Test,
            UiIconTone.Success),
        new(
            UiCommandIds.TransferStartQueue,
            UiMenuId.Transfer,
            static strings => strings.TransferStartQueue,
            static strings => strings.TransferStartQueueDescription,
            new KeyGesture(Key.F7),
            UiGlyph.Run,
            UiIconTone.Success),
        new(
            UiCommandIds.TransferPauseAll,
            UiMenuId.Transfer,
            static strings => strings.TransferPauseAll,
            static strings => strings.TransferPauseAllDescription,
            new KeyGesture(Key.F8),
            UiGlyph.Pause,
            UiIconTone.Text),
        new(
            UiCommandIds.TransferResumeAll,
            UiMenuId.Transfer,
            static strings => strings.TransferResumeAll,
            static strings => strings.TransferResumeAllDescription,
            null,
            UiGlyph.Run,
            UiIconTone.Text),
        new(
            UiCommandIds.TransferCancelSelected,
            UiMenuId.Transfer,
            static strings => strings.TransferCancelSelected,
            static strings => strings.TransferCancelSelectedDescription,
            null,
            UiGlyph.Stop,
            UiIconTone.Danger),
        new(
            UiCommandIds.TransferSpeedLimits,
            UiMenuId.Transfer,
            static strings => strings.TransferSpeedLimits,
            static strings => strings.TransferSpeedLimitsDescription,
            null,
            UiGlyph.Speed,
            UiIconTone.Text),
        new(
            UiCommandIds.SyncComparePanes,
            UiMenuId.Sync,
            static strings => strings.SyncComparePanes,
            static strings => strings.SyncComparePanesDescription,
            new KeyGesture(Key.D, KeyModifiers.Control),
            UiGlyph.Compare,
            UiIconTone.Text),
        new(
            UiCommandIds.SyncReviewRun,
            UiMenuId.Sync,
            static strings => strings.SyncReviewRun,
            static strings => strings.SyncReviewRunDescription,
            new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Shift),
            UiGlyph.Test,
            UiIconTone.Text),
        new(
            UiCommandIds.SyncSyncProfiles,
            UiMenuId.Sync,
            static strings => strings.SyncSyncProfiles,
            static strings => strings.SyncSyncProfilesDescription,
            null,
            UiGlyph.Profiles,
            UiIconTone.Text),
        new(
            UiCommandIds.SyncSchedules,
            UiMenuId.Sync,
            static strings => strings.SyncSchedules,
            static strings => strings.SyncSchedulesDescription,
            new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Alt),
            UiGlyph.Schedule,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsSearch,
            UiMenuId.Tools,
            static strings => strings.ToolsSearch,
            static strings => strings.ToolsSearchDescription,
            new KeyGesture(Key.F, KeyModifiers.Control),
            UiGlyph.Search,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsBackgroundAgent,
            UiMenuId.Tools,
            static strings => strings.ToolsBackgroundAgent,
            static strings => strings.ToolsBackgroundAgentDescription,
            null,
            UiGlyph.Server,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsChecksums,
            UiMenuId.Tools,
            static strings => strings.ToolsChecksums,
            static strings => strings.ToolsChecksumsDescription,
            null,
            UiGlyph.Checksum,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsSettings,
            UiMenuId.Tools,
            static strings => strings.ToolsSettings,
            static strings => strings.ToolsSettingsDescription,
            new KeyGesture(Key.OemComma, KeyModifiers.Control),
            UiGlyph.Settings,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsExportSettings,
            UiMenuId.Tools,
            static strings => strings.ToolsExportSettings,
            static strings => strings.ToolsExportSettingsDescription,
            null,
            UiGlyph.Upload,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsImportSettings,
            UiMenuId.Tools,
            static strings => strings.ToolsImportSettings,
            static strings => strings.ToolsImportSettingsDescription,
            null,
            UiGlyph.Download,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsLogs,
            UiMenuId.Tools,
            static strings => strings.ToolsLogs,
            static strings => strings.ToolsLogsDescription,
            null,
            UiGlyph.Log,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsDiagnostics,
            UiMenuId.Tools,
            static strings => strings.ToolsDiagnostics,
            static strings => strings.ToolsDiagnosticsDescription,
            null,
            UiGlyph.Diagnostics,
            UiIconTone.Text),
        new(
            UiCommandIds.HelpCheckForUpdates,
            UiMenuId.Help,
            static strings => strings.HelpCheckForUpdates,
            static strings => strings.HelpCheckForUpdatesDescription,
            null,
            UiGlyph.Refresh,
            UiIconTone.Text),
        new(
            UiCommandIds.HelpKeyboardShortcuts,
            UiMenuId.Help,
            static strings => strings.HelpKeyboardShortcuts,
            static strings => strings.HelpKeyboardShortcutsDescription,
            null,
            UiGlyph.Keyboard,
            UiIconTone.Text),
        new(
            UiCommandIds.HelpDocumentation,
            UiMenuId.Help,
            static strings => strings.HelpDocumentation,
            static strings => strings.HelpDocumentationDescription,
            null,
            UiGlyph.Documentation,
            UiIconTone.Text),
        new(
            UiCommandIds.HelpReportIssue,
            UiMenuId.Help,
            static strings => strings.HelpReportIssue,
            static strings => strings.HelpReportIssueDescription,
            null,
            UiGlyph.Bug,
            UiIconTone.Text),
        new(
            UiCommandIds.HelpAboutStorageHub,
            UiMenuId.Help,
            static strings => strings.HelpAboutStorageHub,
            static strings => strings.HelpAboutStorageHubDescription,
            null,
            UiGlyph.Info,
            UiIconTone.Text),
    ];

    /// <summary>The menus, in display order.</summary>
    internal static IReadOnlyList<UiMenuId> Menus { get; } = Enum.GetValues<UiMenuId>();

    /// <summary>
    /// Every command with its text resolved for the current language. Rebuilt on each call
    /// rather than cached, so a language change is picked up by the next menu that is built.
    /// </summary>
    internal static IReadOnlyList<UiCommandDefinition> Definitions => Resolve(Ui.Commands);

    /// <summary>The menu's title in the current language.</summary>
    internal static string MenuTitle(UiMenuId menu)
    {
        var shell = Ui.Shell;
        return menu switch
        {
            UiMenuId.Workspace => shell.MenuWorkspace,
            UiMenuId.Edit => shell.MenuEdit,
            UiMenuId.View => shell.MenuView,
            UiMenuId.Go => shell.MenuGo,
            UiMenuId.Connections => shell.MenuConnections,
            UiMenuId.Transfer => shell.MenuTransfer,
            UiMenuId.Sync => shell.MenuSync,
            UiMenuId.Tools => shell.MenuTools,
            UiMenuId.Help => shell.MenuHelp,
            _ => menu.ToString()
        };
    }

    /// <summary>The commands in one menu, in order.</summary>
    internal static IEnumerable<UiCommandDefinition> ForMenu(UiMenuId menu) =>
        Definitions.Where(definition => definition.Menu == menu);

    /// <summary>Looks a command up by its stable id.</summary>
    internal static UiCommandDefinition GetDefinition(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Definitions.FirstOrDefault(definition =>
                   string.Equals(definition.Id, id, StringComparison.Ordinal))
               ?? throw new KeyNotFoundException($"No command is registered with the id '{id}'.");
    }

    /// <summary>
    /// Whether a declared command is actually wired up yet.
    /// </summary>
    /// <remarks>
    /// Keyed on the id rather than the label, so the set does not change with the language,
    /// and so a command that is renamed keeps its place here.
    /// </remarks>
    internal static bool IsAvailable(string commandId) => commandId is
        UiCommandIds.WorkspaceNewWorkspace or
        UiCommandIds.WorkspaceOpenWorkspace or
        UiCommandIds.WorkspaceSaveWorkspace or
        UiCommandIds.WorkspaceSaveWorkspaceAs or
        UiCommandIds.WorkspaceRenameWorkspace or
        UiCommandIds.WorkspaceCloseWorkspace or
        UiCommandIds.WorkspaceExit or
        UiCommandIds.EditCut or
        UiCommandIds.EditCopy or
        UiCommandIds.EditPaste or
        UiCommandIds.EditNewFolder or
        UiCommandIds.EditNewEmptyFile or
        UiCommandIds.EditRename or
        UiCommandIds.EditBatchRename or
        UiCommandIds.EditDelete or
        UiCommandIds.EditSelectAll or
        UiCommandIds.EditInvertSelection or
        UiCommandIds.EditProperties or
        UiCommandIds.ViewRefresh or
        UiCommandIds.ViewConnectionsPanel or
        UiCommandIds.ViewMoveConnectionsPanel or
        UiCommandIds.GoBack or
        UiCommandIds.GoForward or
        UiCommandIds.GoUp or
        UiCommandIds.GoFocusAddress or
        UiCommandIds.GoNextPane or
        UiCommandIds.ConnectionsNewConnection or
        UiCommandIds.ConnectionsKeyStore or
        UiCommandIds.ToolsBackgroundAgent or
        UiCommandIds.SyncReviewRun or
        UiCommandIds.SyncSyncProfiles or
        UiCommandIds.SyncSchedules or
        UiCommandIds.ToolsSettings or
        UiCommandIds.ToolsExportSettings or
        UiCommandIds.ToolsImportSettings or
        UiCommandIds.HelpCheckForUpdates or
        UiCommandIds.HelpAboutStorageHub;

    internal static bool IsPaneCommand(UiCommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.Menu is UiMenuId.Edit or UiMenuId.Go || command.Id == UiCommandIds.ViewRefresh;
    }

    internal static bool CanDispatch(UiCommandDefinition command, bool sshFocused, bool textFocused, bool hasPane) =>
        !sshFocused && !textFocused && (!IsPaneCommand(command) || hasPane);

    private static IReadOnlyList<UiCommandDefinition> Resolve(CommandStrings strings) =>
        [.. Specs.Select(spec => new UiCommandDefinition(
            spec.Id,
            spec.Menu,
            spec.Label(strings),
            spec.Description(strings),
            spec.Shortcut,
            spec.Glyph,
            spec.Tone))];
}
