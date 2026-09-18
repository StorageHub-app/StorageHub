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
    Keys Shortcut = Keys.None,
    UiGlyph? Glyph = null,
    UiIconTone Tone = UiIconTone.Text);

/// <summary>A command with its text resolved for the current language.</summary>
internal sealed record UiCommandDefinition(
    string Id,
    UiMenuId Menu,
    string Label,
    string Description,
    Keys Shortcut = Keys.None,
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
            Keys.Control | Keys.T,
            UiGlyph.Add,
            UiIconTone.Primary),
        new(
            UiCommandIds.WorkspaceOpenWorkspace,
            UiMenuId.Workspace,
            static strings => strings.WorkspaceOpenWorkspace,
            static strings => strings.WorkspaceOpenWorkspaceDescription,
            Keys.Control | Keys.O,
            UiGlyph.Folder,
            UiIconTone.Text),
        new(
            UiCommandIds.WorkspaceSaveWorkspace,
            UiMenuId.Workspace,
            static strings => strings.WorkspaceSaveWorkspace,
            static strings => strings.WorkspaceSaveWorkspaceDescription,
            Keys.Control | Keys.S,
            UiGlyph.Save,
            UiIconTone.Text),
        new(
            UiCommandIds.WorkspaceSaveWorkspaceAs,
            UiMenuId.Workspace,
            static strings => strings.WorkspaceSaveWorkspaceAs,
            static strings => strings.WorkspaceSaveWorkspaceAsDescription,
            Keys.Control | Keys.Shift | Keys.S,
            UiGlyph.Save,
            UiIconTone.Text),
        new(
            UiCommandIds.WorkspaceRenameWorkspace,
            UiMenuId.Workspace,
            static strings => strings.WorkspaceRenameWorkspace,
            static strings => strings.WorkspaceRenameWorkspaceDescription,
            Keys.None,
            UiGlyph.Rename,
            UiIconTone.Text),
        new(
            UiCommandIds.WorkspaceCloseWorkspace,
            UiMenuId.Workspace,
            static strings => strings.WorkspaceCloseWorkspace,
            static strings => strings.WorkspaceCloseWorkspaceDescription,
            Keys.Control | Keys.W,
            UiGlyph.Close,
            UiIconTone.Text),
        new(
            UiCommandIds.WorkspaceExit,
            UiMenuId.Workspace,
            static strings => strings.WorkspaceExit,
            static strings => strings.WorkspaceExitDescription,
            Keys.None,
            UiGlyph.Exit,
            UiIconTone.Text),
        new(
            UiCommandIds.EditNewFolder,
            UiMenuId.Edit,
            static strings => strings.EditNewFolder,
            static strings => strings.EditNewFolderDescription,
            Keys.Control | Keys.Shift | Keys.N,
            UiGlyph.Folder,
            UiIconTone.Text),
        new(
            UiCommandIds.EditNewEmptyFile,
            UiMenuId.Edit,
            static strings => strings.EditNewEmptyFile,
            static strings => strings.EditNewEmptyFileDescription,
            Keys.Control | Keys.Alt | Keys.N,
            UiGlyph.File,
            UiIconTone.Text),
        new(
            UiCommandIds.EditCut,
            UiMenuId.Edit,
            static strings => strings.EditCut,
            static strings => strings.EditCutDescription,
            Keys.Control | Keys.X,
            UiGlyph.Cut,
            UiIconTone.Text),
        new(
            UiCommandIds.EditCopy,
            UiMenuId.Edit,
            static strings => strings.EditCopy,
            static strings => strings.EditCopyDescription,
            Keys.Control | Keys.C,
            UiGlyph.Copy,
            UiIconTone.Text),
        new(
            UiCommandIds.EditPaste,
            UiMenuId.Edit,
            static strings => strings.EditPaste,
            static strings => strings.EditPasteDescription,
            Keys.Control | Keys.V,
            UiGlyph.Paste,
            UiIconTone.Text),
        new(
            UiCommandIds.EditRename,
            UiMenuId.Edit,
            static strings => strings.EditRename,
            static strings => strings.EditRenameDescription,
            Keys.F2,
            UiGlyph.Rename,
            UiIconTone.Text),
        new(
            UiCommandIds.EditBatchRename,
            UiMenuId.Edit,
            static strings => strings.EditBatchRename,
            static strings => strings.EditBatchRenameDescription,
            Keys.None,
            UiGlyph.Profiles,
            UiIconTone.Text),
        new(
            UiCommandIds.EditDelete,
            UiMenuId.Edit,
            static strings => strings.EditDelete,
            static strings => strings.EditDeleteDescription,
            Keys.Delete,
            UiGlyph.Delete,
            UiIconTone.Danger),
        new(
            UiCommandIds.EditSelectAll,
            UiMenuId.Edit,
            static strings => strings.EditSelectAll,
            static strings => strings.EditSelectAllDescription,
            Keys.Control | Keys.A,
            UiGlyph.SelectAll,
            UiIconTone.Text),
        new(
            UiCommandIds.EditInvertSelection,
            UiMenuId.Edit,
            static strings => strings.EditInvertSelection,
            static strings => strings.EditInvertSelectionDescription,
            Keys.Control | Keys.I,
            UiGlyph.Invert,
            UiIconTone.Text),
        new(
            UiCommandIds.EditProperties,
            UiMenuId.Edit,
            static strings => strings.EditProperties,
            static strings => strings.EditPropertiesDescription,
            Keys.Alt | Keys.Enter,
            UiGlyph.Properties,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewRefresh,
            UiMenuId.View,
            static strings => strings.ViewRefresh,
            static strings => strings.ViewRefreshDescription,
            Keys.F5,
            UiGlyph.Refresh,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewConnectionsPanel,
            UiMenuId.View,
            static strings => strings.ViewConnectionsPanel,
            static strings => strings.ViewConnectionsPanelDescription,
            Keys.Control | Keys.B,
            UiGlyph.Connections,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewMoveConnectionsPanel,
            UiMenuId.View,
            static strings => strings.ViewMoveConnectionsPanel,
            static strings => strings.ViewMoveConnectionsPanelDescription,
            Keys.None,
            UiGlyph.Layers,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewDirectoryTree,
            UiMenuId.View,
            static strings => strings.ViewDirectoryTree,
            static strings => strings.ViewDirectoryTreeDescription,
            Keys.None,
            UiGlyph.Tree,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewTransferQueue,
            UiMenuId.View,
            static strings => strings.ViewTransferQueue,
            static strings => strings.ViewTransferQueueDescription,
            Keys.None,
            UiGlyph.Queue,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewSessionLog,
            UiMenuId.View,
            static strings => strings.ViewSessionLog,
            static strings => strings.ViewSessionLogDescription,
            Keys.None,
            UiGlyph.Log,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewHiddenFiles,
            UiMenuId.View,
            static strings => strings.ViewHiddenFiles,
            static strings => strings.ViewHiddenFilesDescription,
            Keys.None,
            UiGlyph.Hidden,
            UiIconTone.Text),
        new(
            UiCommandIds.ViewTheme,
            UiMenuId.View,
            static strings => strings.ViewTheme,
            static strings => strings.ViewThemeDescription,
            Keys.None,
            UiGlyph.Theme,
            UiIconTone.Text),
        new(
            UiCommandIds.GoBack,
            UiMenuId.Go,
            static strings => strings.GoBack,
            static strings => strings.GoBackDescription,
            Keys.Alt | Keys.Left,
            UiGlyph.Back,
            UiIconTone.Text),
        new(
            UiCommandIds.GoForward,
            UiMenuId.Go,
            static strings => strings.GoForward,
            static strings => strings.GoForwardDescription,
            Keys.Alt | Keys.Right,
            UiGlyph.Forward,
            UiIconTone.Text),
        new(
            UiCommandIds.GoUp,
            UiMenuId.Go,
            static strings => strings.GoUp,
            static strings => strings.GoUpDescription,
            Keys.Alt | Keys.Up,
            UiGlyph.Up,
            UiIconTone.Text),
        new(
            UiCommandIds.GoFocusAddress,
            UiMenuId.Go,
            static strings => strings.GoFocusAddress,
            static strings => strings.GoFocusAddressDescription,
            Keys.Control | Keys.L,
            UiGlyph.Link,
            UiIconTone.Text),
        new(
            UiCommandIds.GoNextPane,
            UiMenuId.Go,
            static strings => strings.GoNextPane,
            static strings => strings.GoNextPaneDescription,
            Keys.F6,
            UiGlyph.Layers,
            UiIconTone.Text),
        new(
            UiCommandIds.GoHome,
            UiMenuId.Go,
            static strings => strings.GoHome,
            static strings => strings.GoHomeDescription,
            Keys.None,
            UiGlyph.Home,
            UiIconTone.Text),
        new(
            UiCommandIds.GoHistory,
            UiMenuId.Go,
            static strings => strings.GoHistory,
            static strings => strings.GoHistoryDescription,
            Keys.None,
            UiGlyph.History,
            UiIconTone.Text),
        new(
            UiCommandIds.GoFavorites,
            UiMenuId.Go,
            static strings => strings.GoFavorites,
            static strings => strings.GoFavoritesDescription,
            Keys.None,
            UiGlyph.Favorite,
            UiIconTone.Warning),
        new(
            UiCommandIds.ConnectionsNewConnection,
            UiMenuId.Connections,
            static strings => strings.ConnectionsNewConnection,
            static strings => strings.ConnectionsNewConnectionDescription,
            Keys.Control | Keys.Shift | Keys.M,
            UiGlyph.Add,
            UiIconTone.Text),
        new(
            UiCommandIds.ConnectionsKeyStore,
            UiMenuId.Connections,
            static strings => strings.ConnectionsKeyStore,
            static strings => strings.ConnectionsKeyStoreDescription,
            Keys.Control | Keys.Shift | Keys.K,
            UiGlyph.Key,
            UiIconTone.Text),
        new(
            UiCommandIds.ConnectionsQuickConnect,
            UiMenuId.Connections,
            static strings => strings.ConnectionsQuickConnect,
            static strings => strings.ConnectionsQuickConnectDescription,
            Keys.Control | Keys.K,
            UiGlyph.Connect,
            UiIconTone.Text),
        new(
            UiCommandIds.ConnectionsReconnect,
            UiMenuId.Connections,
            static strings => strings.ConnectionsReconnect,
            static strings => strings.ConnectionsReconnectDescription,
            Keys.None,
            UiGlyph.Refresh,
            UiIconTone.Text),
        new(
            UiCommandIds.ConnectionsDisconnect,
            UiMenuId.Connections,
            static strings => strings.ConnectionsDisconnect,
            static strings => strings.ConnectionsDisconnectDescription,
            Keys.None,
            UiGlyph.Disconnect,
            UiIconTone.Text),
        new(
            UiCommandIds.ConnectionsTestConnection,
            UiMenuId.Connections,
            static strings => strings.ConnectionsTestConnection,
            static strings => strings.ConnectionsTestConnectionDescription,
            Keys.None,
            UiGlyph.Test,
            UiIconTone.Success),
        new(
            UiCommandIds.TransferStartQueue,
            UiMenuId.Transfer,
            static strings => strings.TransferStartQueue,
            static strings => strings.TransferStartQueueDescription,
            Keys.F7,
            UiGlyph.Run,
            UiIconTone.Success),
        new(
            UiCommandIds.TransferPauseAll,
            UiMenuId.Transfer,
            static strings => strings.TransferPauseAll,
            static strings => strings.TransferPauseAllDescription,
            Keys.F8,
            UiGlyph.Pause,
            UiIconTone.Text),
        new(
            UiCommandIds.TransferResumeAll,
            UiMenuId.Transfer,
            static strings => strings.TransferResumeAll,
            static strings => strings.TransferResumeAllDescription,
            Keys.None,
            UiGlyph.Run,
            UiIconTone.Text),
        new(
            UiCommandIds.TransferCancelSelected,
            UiMenuId.Transfer,
            static strings => strings.TransferCancelSelected,
            static strings => strings.TransferCancelSelectedDescription,
            Keys.None,
            UiGlyph.Stop,
            UiIconTone.Danger),
        new(
            UiCommandIds.TransferSpeedLimits,
            UiMenuId.Transfer,
            static strings => strings.TransferSpeedLimits,
            static strings => strings.TransferSpeedLimitsDescription,
            Keys.None,
            UiGlyph.Speed,
            UiIconTone.Text),
        new(
            UiCommandIds.SyncComparePanes,
            UiMenuId.Sync,
            static strings => strings.SyncComparePanes,
            static strings => strings.SyncComparePanesDescription,
            Keys.Control | Keys.D,
            UiGlyph.Compare,
            UiIconTone.Text),
        new(
            UiCommandIds.SyncReviewRun,
            UiMenuId.Sync,
            static strings => strings.SyncReviewRun,
            static strings => strings.SyncReviewRunDescription,
            Keys.Control | Keys.Shift | Keys.P,
            UiGlyph.Test,
            UiIconTone.Text),
        new(
            UiCommandIds.SyncSyncProfiles,
            UiMenuId.Sync,
            static strings => strings.SyncSyncProfiles,
            static strings => strings.SyncSyncProfilesDescription,
            Keys.None,
            UiGlyph.Profiles,
            UiIconTone.Text),
        new(
            UiCommandIds.SyncSchedules,
            UiMenuId.Sync,
            static strings => strings.SyncSchedules,
            static strings => strings.SyncSchedulesDescription,
            Keys.Control | Keys.Alt | Keys.S,
            UiGlyph.Schedule,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsSearch,
            UiMenuId.Tools,
            static strings => strings.ToolsSearch,
            static strings => strings.ToolsSearchDescription,
            Keys.Control | Keys.F,
            UiGlyph.Search,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsBackgroundAgent,
            UiMenuId.Tools,
            static strings => strings.ToolsBackgroundAgent,
            static strings => strings.ToolsBackgroundAgentDescription,
            Keys.None,
            UiGlyph.Server,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsChecksums,
            UiMenuId.Tools,
            static strings => strings.ToolsChecksums,
            static strings => strings.ToolsChecksumsDescription,
            Keys.None,
            UiGlyph.Checksum,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsSettings,
            UiMenuId.Tools,
            static strings => strings.ToolsSettings,
            static strings => strings.ToolsSettingsDescription,
            Keys.Control | Keys.Oemcomma,
            UiGlyph.Settings,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsExportSettings,
            UiMenuId.Tools,
            static strings => strings.ToolsExportSettings,
            static strings => strings.ToolsExportSettingsDescription,
            Keys.None,
            UiGlyph.Upload,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsImportSettings,
            UiMenuId.Tools,
            static strings => strings.ToolsImportSettings,
            static strings => strings.ToolsImportSettingsDescription,
            Keys.None,
            UiGlyph.Download,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsLogs,
            UiMenuId.Tools,
            static strings => strings.ToolsLogs,
            static strings => strings.ToolsLogsDescription,
            Keys.None,
            UiGlyph.Log,
            UiIconTone.Text),
        new(
            UiCommandIds.ToolsDiagnostics,
            UiMenuId.Tools,
            static strings => strings.ToolsDiagnostics,
            static strings => strings.ToolsDiagnosticsDescription,
            Keys.None,
            UiGlyph.Diagnostics,
            UiIconTone.Text),
        new(
            UiCommandIds.HelpCheckForUpdates,
            UiMenuId.Help,
            static strings => strings.HelpCheckForUpdates,
            static strings => strings.HelpCheckForUpdatesDescription,
            Keys.None,
            UiGlyph.Refresh,
            UiIconTone.Text),
        new(
            UiCommandIds.HelpKeyboardShortcuts,
            UiMenuId.Help,
            static strings => strings.HelpKeyboardShortcuts,
            static strings => strings.HelpKeyboardShortcutsDescription,
            Keys.None,
            UiGlyph.Keyboard,
            UiIconTone.Text),
        new(
            UiCommandIds.HelpDocumentation,
            UiMenuId.Help,
            static strings => strings.HelpDocumentation,
            static strings => strings.HelpDocumentationDescription,
            Keys.None,
            UiGlyph.Documentation,
            UiIconTone.Text),
        new(
            UiCommandIds.HelpReportIssue,
            UiMenuId.Help,
            static strings => strings.HelpReportIssue,
            static strings => strings.HelpReportIssueDescription,
            Keys.None,
            UiGlyph.Bug,
            UiIconTone.Text),
        new(
            UiCommandIds.HelpAboutStorageHub,
            UiMenuId.Help,
            static strings => strings.HelpAboutStorageHub,
            static strings => strings.HelpAboutStorageHubDescription,
            Keys.None,
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
