namespace StorageHub.Desktop;

/// <summary>
/// The stable identity of every menu command.
/// </summary>
/// <remarks>
/// These strings are persisted: a user's rebound shortcuts are stored against them in the desktop
/// settings file. They were previously derived at runtime from the English menu and label, which
/// is what made the labels untranslatable — renaming a command silently orphaned its shortcut.
///
/// So the values are frozen exactly as that derivation produced them, quirks and all: only a
/// literal three-dot ellipsis is stripped, it is stripped before spaces become hyphens, and an
/// ampersand survives. They were generated from the old derivation rather than retyped, and
/// <c>UiCommandCatalogTests</c> re-derives them from a frozen table of the original English labels
/// to keep it that way. Do not tidy a value here to match a label; add a new id instead.
/// </remarks>
internal static class UiCommandIds
{
    internal const string WorkspaceNewWorkspace = "workspace.new-workspace";
    internal const string WorkspaceOpenWorkspace = "workspace.open-workspace";
    internal const string WorkspaceSaveWorkspace = "workspace.save-workspace";
    internal const string WorkspaceSaveWorkspaceAs = "workspace.save-workspace-as";
    internal const string WorkspaceRenameWorkspace = "workspace.rename-workspace";
    internal const string WorkspaceCloseWorkspace = "workspace.close-workspace";
    internal const string WorkspaceExit = "workspace.exit";
    internal const string EditNewFolder = "edit.new-folder";
    internal const string EditNewEmptyFile = "edit.new-empty-file";
    internal const string EditCut = "edit.cut";
    internal const string EditCopy = "edit.copy";
    internal const string EditPaste = "edit.paste";
    internal const string EditRename = "edit.rename";
    internal const string EditBatchRename = "edit.batch-rename";
    internal const string EditDelete = "edit.delete";
    internal const string EditSelectAll = "edit.select-all";
    internal const string EditInvertSelection = "edit.invert-selection";
    internal const string EditProperties = "edit.properties";
    internal const string ViewRefresh = "view.refresh";
    internal const string ViewConnectionsPanel = "view.connections-panel";
    internal const string ViewMoveConnectionsPanel = "view.move-connections-panel";
    internal const string ViewDirectoryTree = "view.directory-tree";
    internal const string ViewTransferQueue = "view.transfer-queue";
    internal const string ViewSessionLog = "view.session-log";
    internal const string ViewHiddenFiles = "view.hidden-files";
    internal const string ViewTheme = "view.theme";
    internal const string GoBack = "go.back";
    internal const string GoForward = "go.forward";
    internal const string GoUp = "go.up";
    internal const string GoFocusAddress = "go.focus-address";
    internal const string GoNextPane = "go.next-pane";
    internal const string GoHome = "go.home";
    internal const string GoHistory = "go.history";
    internal const string GoFavorites = "go.favorites";
    internal const string ConnectionsNewConnection = "connections.new-connection";
    internal const string ConnectionsKeyStore = "connections.key-store";
    internal const string ConnectionsQuickConnect = "connections.quick-connect";
    internal const string ConnectionsReconnect = "connections.reconnect";
    internal const string ConnectionsDisconnect = "connections.disconnect";
    internal const string ConnectionsTestConnection = "connections.test-connection";
    internal const string TransferStartQueue = "transfer.start-queue";
    internal const string TransferPauseAll = "transfer.pause-all";
    internal const string TransferResumeAll = "transfer.resume-all";
    internal const string TransferCancelSelected = "transfer.cancel-selected";
    internal const string TransferSpeedLimits = "transfer.speed-limits";
    internal const string SyncComparePanes = "sync.compare-panes";
    internal const string SyncReviewRun = "sync.review-&-run";
    internal const string SyncSyncProfiles = "sync.sync-profiles";
    internal const string SyncSchedules = "sync.schedules";
    internal const string ToolsSearch = "tools.search";
    internal const string ToolsBackgroundAgent = "tools.background-agent";
    internal const string ToolsChecksums = "tools.checksums";
    internal const string ToolsSettings = "tools.settings";
    internal const string ToolsExportSettings = "tools.export-settings";
    internal const string ToolsImportSettings = "tools.import-settings";
    internal const string ToolsLogs = "tools.logs";
    internal const string ToolsDiagnostics = "tools.diagnostics";
    internal const string HelpCheckForUpdates = "help.check-for-updates";
    internal const string HelpKeyboardShortcuts = "help.keyboard-shortcuts";
    internal const string HelpDocumentation = "help.documentation";
    internal const string HelpReportIssue = "help.report-issue";
    internal const string HelpAboutStorageHub = "help.about-storagehub";
}
