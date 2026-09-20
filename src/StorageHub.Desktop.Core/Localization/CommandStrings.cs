using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The menu commands, as the user reads them.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>: the localization
/// merge is top-level, so a nested value would replace a whole subtree instead of falling back
/// key by key, and <c>Culture</c> is already taken by the base class.
///
/// The English text here is the source; the en-US file on disk is generated from it.
/// </remarks>
[LocalizationSection("commands")]
internal sealed class CommandStrings : LocalizationModelBase
{
    public string WorkspaceNewWorkspace { get; set; } = "New Workspace...";

    public string WorkspaceNewWorkspaceDescription { get; set; } = "Choose a one- to four-pane workspace.";

    public string WorkspaceOpenWorkspace { get; set; } = "Open Workspace...";

    public string WorkspaceOpenWorkspaceDescription { get; set; } = "Open a saved StorageHub workspace file.";

    public string WorkspaceSaveWorkspace { get; set; } = "Save Workspace";

    public string WorkspaceSaveWorkspaceDescription { get; set; } = "Save the active workspace.";

    public string WorkspaceSaveWorkspaceAs { get; set; } = "Save Workspace As...";

    public string WorkspaceSaveWorkspaceAsDescription { get; set; } = "Save the active workspace to a new file.";

    public string WorkspaceRenameWorkspace { get; set; } = "Rename Workspace...";

    public string WorkspaceRenameWorkspaceDescription { get; set; } = "Rename the active workspace tab.";

    public string WorkspaceCloseWorkspace { get; set; } = "Close Workspace";

    public string WorkspaceCloseWorkspaceDescription { get; set; } = "Close the active workspace tab.";

    public string WorkspaceExit { get; set; } = "Exit";

    public string WorkspaceExitDescription { get; set; } = "Close StorageHub.";

    public string EditNewFolder { get; set; } = "New Folder";

    public string EditNewFolderDescription { get; set; } = "Create a folder in the active pane.";

    public string EditNewEmptyFile { get; set; } = "New Empty File...";

    public string EditNewEmptyFileDescription { get; set; } = "Create an empty file in the active pane.";

    public string EditCut { get; set; } = "Cut";

    public string EditCutDescription { get; set; } = "Stage selected files for moving to another pane.";

    public string EditCopy { get; set; } = "Copy";

    public string EditCopyDescription { get; set; } = "Stage selected files for copying to another pane.";

    public string EditPaste { get; set; } = "Paste";

    public string EditPasteDescription { get; set; } = "Enqueue the staged operation in this pane.";

    public string EditRename { get; set; } = "Rename";

    public string EditRenameDescription { get; set; } = "Rename the focused item.";

    public string EditBatchRename { get; set; } = "Batch Rename...";

    public string EditBatchRenameDescription { get; set; } = "Preview and rename several selected items.";

    public string EditDelete { get; set; } = "Delete";

    public string EditDeleteDescription { get; set; } = "Review and delete the selected items.";

    public string EditSelectAll { get; set; } = "Select All";

    public string EditSelectAllDescription { get; set; } = "Select every visible item.";

    public string EditInvertSelection { get; set; } = "Invert Selection";

    public string EditInvertSelectionDescription { get; set; } = "Invert the visible selection in the active pane.";

    public string EditProperties { get; set; } = "Properties";

    public string EditPropertiesDescription { get; set; } = "Inspect read-only versions, metadata, and tags for one saved-connection file.";

    public string ViewRefresh { get; set; } = "Refresh";

    public string ViewRefreshDescription { get; set; } = "Refresh the focused pane.";

    public string ViewConnectionsPanel { get; set; } = "Connections Panel";

    public string ViewConnectionsPanelDescription { get; set; } = "Show or hide the saved-connections panel.";

    public string ViewMoveConnectionsPanel { get; set; } = "Move Connections Panel";

    public string ViewMoveConnectionsPanelDescription { get; set; } = "Dock the connections panel to the other side of the window.";

    public string ViewDirectoryTree { get; set; } = "Directory Tree";

    public string ViewDirectoryTreeDescription { get; set; } = "Show or hide the directory tree beside the file list.";

    public string ViewTransferQueue { get; set; } = "Transfer Queue";

    public string ViewTransferQueueDescription { get; set; } = "Show or hide the transfer queue.";

    public string ViewSessionLog { get; set; } = "Session Log";

    public string ViewSessionLogDescription { get; set; } = "Show or hide the session activity log.";

    public string ViewHiddenFiles { get; set; } = "Hidden Files";

    public string ViewHiddenFilesDescription { get; set; } = "Show or hide items the provider marks as hidden.";

    public string ViewTheme { get; set; } = "Theme";

    public string ViewThemeDescription { get; set; } = "Switch between the light, dark, and system appearances.";

    public string GoBack { get; set; } = "Back";

    public string GoBackDescription { get; set; } = "Return to the previous location.";

    public string GoForward { get; set; } = "Forward";

    public string GoForwardDescription { get; set; } = "Move to the next location in history.";

    public string GoUp { get; set; } = "Up";

    public string GoUpDescription { get; set; } = "Open the parent location.";

    public string GoFocusAddress { get; set; } = "Focus Address";

    public string GoFocusAddressDescription { get; set; } = "Select the active pane's address.";

    public string GoNextPane { get; set; } = "Next Pane";

    public string GoNextPaneDescription { get; set; } = "Focus the next pane in the workspace.";

    public string GoHome { get; set; } = "Home";

    public string GoHomeDescription { get; set; } = "Open the pane's home location.";

    public string GoHistory { get; set; } = "History";

    public string GoHistoryDescription { get; set; } = "Reopen a location visited in this session.";

    public string GoFavorites { get; set; } = "Favorites";

    public string GoFavoritesDescription { get; set; } = "Open a connection you marked as a favorite.";

    public string ConnectionsNewConnection { get; set; } = "New Connection...";

    public string ConnectionsNewConnectionDescription { get; set; } = "Create a saved connection.";

    public string ConnectionsKeyStore { get; set; } = "Key Store...";

    public string ConnectionsKeyStoreDescription { get; set; } = "Import and manage the certificates and private keys shared by saved connections.";

    public string ConnectionsQuickConnect { get; set; } = "Quick Connect...";

    public string ConnectionsQuickConnectDescription { get; set; } = "Open a temporary connection without saving credentials in the profile.";

    public string ConnectionsReconnect { get; set; } = "Reconnect";

    public string ConnectionsReconnectDescription { get; set; } = "Reopen the active pane's connection.";

    public string ConnectionsDisconnect { get; set; } = "Disconnect";

    public string ConnectionsDisconnectDescription { get; set; } = "Close the active pane's connection.";

    public string ConnectionsTestConnection { get; set; } = "Test Connection";

    public string ConnectionsTestConnectionDescription { get; set; } = "Check that the selected profile can reach its endpoint.";

    public string TransferStartQueue { get; set; } = "Start Queue";

    public string TransferStartQueueDescription { get; set; } = "Start queued transfers.";

    public string TransferPauseAll { get; set; } = "Pause All";

    public string TransferPauseAllDescription { get; set; } = "Pause active transfers at safe checkpoints.";

    public string TransferResumeAll { get; set; } = "Resume All";

    public string TransferResumeAllDescription { get; set; } = "Resume every paused transfer.";

    public string TransferCancelSelected { get; set; } = "Cancel Selected";

    public string TransferCancelSelectedDescription { get; set; } = "Cancel the selected transfers at their next safe checkpoint.";

    public string TransferSpeedLimits { get; set; } = "Speed Limits...";

    public string TransferSpeedLimitsDescription { get; set; } = "Cap the bandwidth StorageHub transfers use.";

    public string SyncComparePanes { get; set; } = "Compare Panes";

    public string SyncComparePanesDescription { get; set; } = "Compare the visible source and destination.";

    public string SyncReviewRun { get; set; } = "Review & Run...";

    public string SyncReviewRunDescription { get; set; } = "Review an exact sync plan, then run it when its safety checks pass.";

    public string SyncSyncProfiles { get; set; } = "Sync Profiles...";

    public string SyncSyncProfilesDescription { get; set; } = "Create and edit saved synchronization tasks.";

    public string SyncSchedules { get; set; } = "Schedules...";

    public string SyncSchedulesDescription { get; set; } = "Manage durable review-only or safety-gated automatic synchronization schedules.";

    public string ToolsSearch { get; set; } = "Search...";

    public string ToolsSearchDescription { get; set; } = "Search within the focused endpoint.";

    public string ToolsBackgroundAgent { get; set; } = "Background Agent...";

    public string ToolsBackgroundAgentDescription { get; set; } = "Check whether the background agent is running and start, stop, or restart it.";

    public string ToolsChecksums { get; set; } = "Checksums...";

    public string ToolsChecksumsDescription { get; set; } = "Compute and compare content hashes for the selected items.";

    public string ToolsSettings { get; set; } = "Settings...";

    public string ToolsSettingsDescription { get; set; } = "Configure automatic StorageHub updates.";

    public string ToolsExportSettings { get; set; } = "Export Settings...";

    public string ToolsExportSettingsDescription { get; set; } = "Write your settings, connections, and sync tasks to a file, optionally password protected.";

    public string ToolsImportSettings { get; set; } = "Import Settings...";

    public string ToolsImportSettingsDescription { get; set; } = "Review a settings file and choose what to bring in.";

    public string ToolsLogs { get; set; } = "Logs...";

    public string ToolsLogsDescription { get; set; } = "Open the StorageHub log directory.";

    public string ToolsDiagnostics { get; set; } = "Diagnostics...";

    public string ToolsDiagnosticsDescription { get; set; } = "Collect a support bundle describing this installation.";

    public string HelpCheckForUpdates { get; set; } = "Check for Updates...";

    public string HelpCheckForUpdatesDescription { get; set; } = "Check the official StorageHub GitHub releases for an update.";

    public string HelpKeyboardShortcuts { get; set; } = "Keyboard Shortcuts";

    public string HelpKeyboardShortcutsDescription { get; set; } = "Show and rebind the StorageHub keyboard shortcuts.";

    public string HelpDocumentation { get; set; } = "Documentation";

    public string HelpDocumentationDescription { get; set; } = "Open the StorageHub documentation.";

    public string HelpReportIssue { get; set; } = "Report Issue";

    public string HelpReportIssueDescription { get; set; } = "Report a StorageHub defect.";

    public string HelpAboutStorageHub { get; set; } = "About StorageHub";

    public string HelpAboutStorageHubDescription { get; set; } = "Show StorageHub version and application information.";
}
