using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The shell itself: menu titles, and the surrounding chrome as it is converted.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
/// </remarks>
[LocalizationSection("shell")]
internal sealed class ShellStrings : LocalizationModelBase
{
    public string MenuWorkspace { get; set; } = "Workspace";

    public string MenuEdit { get; set; } = "Edit";

    public string MenuView { get; set; } = "View";

    public string MenuGo { get; set; } = "Go";

    public string MenuConnections { get; set; } = "Connections";

    public string MenuTransfer { get; set; } = "Transfer";

    public string MenuSync { get; set; } = "Sync";

    public string MenuTools { get; set; } = "Tools";

    public string MenuHelp { get; set; } = "Help";

    // ------------------------------------------------------------- workspace tabs
    public string TabWelcome { get; set; } = "Welcome";

    public string TabSyncTasks { get; set; } = "Sync tasks";

    public string NewWorkspaceTab { get; set; } = "New workspace";

    /// <summary>{0} = how many workspaces have been made this session, counting from one.</summary>
    public string WorkspaceTabFormat { get; set; } = "Workspace {0}";

    // ----------------------------------------------------------------- toolbar
    public string CheckForUpdatesTooltip { get; set; } = "Check for StorageHub updates";

    public string AgentControlsTooltip { get; set; } = "Open background agent controls";

    public string AgentReconnectingTooltip { get; set; } = "StorageHub is reconnecting to the background agent.";

    public string FavoritesEmptyHint { get; set; } =
        "Mark a connection as a favorite in the connections panel to list it here.";

    public string ArrangePanesTooltip { get; set; } = "Arrange the two workspace panes";

    public string ClipboardTooltip { get; set; } = "StorageHub's staged file selection";

    public string ClipboardStageTooltip { get; set; } = "Copy or move files from either storage pane";

    public string ClipboardPasteTooltip { get; set; } =
        "Review and paste the staged selection into the active storage pane";

    public string ClipboardClearTooltip { get; set; } = "Clear the StorageHub clipboard";

    // -------------------------------------------------------------- status bar
    public string StatusNoConnection { get; set; } = "No connection";

    public string StatusNoSelection { get; set; } = "0 selected";

    /// <summary>{0} = the number of items, {1} = their total size.</summary>
    public string StatusSelectionFormat { get; set; } = "{0:N0} selected · {1}";

    /// <summary>{0} = the transfer rate, already formatted as a size.</summary>
    public string StatusTransferRateFormat { get; set; } = "{0}/s";

    /// <summary>{0} = the number of queued jobs.</summary>
    public string StatusQueueFormat { get; set; } = "Queue: {0:N0}";

    /// <summary>{0} = the number of queued jobs, {1} = the number running.</summary>
    public string StatusQueueActiveFormat { get; set; } = "Queue: {0:N0} · Active: {1:N0}";

    public string AgentStarting { get; set; } = "Agent: starting";

    public string AgentConnected { get; set; } = "Agent: connected";

    public string AgentRecoveryMode { get; set; } = "Agent: recovery mode";

    public string AgentNotConnected { get; set; } = "Agent: not connected";

    /// <summary>The status bar while a reconnect is in flight.</summary>
    public string AgentReconnectingStatus { get; set; } = "Agent: reconnecting";

    // --------------------------------------------------------- status messages
    public string StatusSettingsImported { get; set; } = "Settings imported.";

    public string StatusIndexingDestination { get; set; } =
        "Indexing the destination folder for conflict review…";

    public string StatusClipboardCleared { get; set; } = "StorageHub clipboard cleared.";

    public string StatusEditedFileUploaded { get; set; } = "Edited file uploaded successfully.";

    public string StatusConcurrencyRestartRequired { get; set; } =
        "Concurrency settings saved; restart StorageHub to apply them.";

    public string StatusConcurrencyPendingIdle { get; set; } =
        "Concurrency settings saved; the Agent will restart when active work is safely idle.";

    public string StatusApplyingConcurrency { get; set; } =
        "Applying concurrency settings to the background Agent…";

    // ------------------------------------------------------ rename workspace
    public string RenameWorkspaceTitle { get; set; } = "Rename Workspace";

    public string RenameWorkspaceAccept { get; set; } = "Rename";

    // ------------------------------------------------------------- shell chrome
    public string ShellAccessibleDescription { get; set; } =
        "A secure multi-pane file manager for local and remote storage.";

    public string WorkspaceCommands { get; set; } = "Workspace and connection commands.";

    public string ClipboardLabel { get; set; } = "CLIPBOARD";

    public string ClipboardEmpty { get; set; } = "Empty";

    public string ClipboardClear { get; set; } = "Clear";

    /// <summary>{0} = the operation, {1} = how many items, {2} = where they came from.</summary>
    public string ClipboardSummaryFormat { get; set; } = "{0} · {1:N0} item(s) · {2}";

    public string DragPaneHeadersHint { get; set; } = "Drag pane headers to swap or dock panes";

    public string WorkspaceAccessibleDescription { get; set; } =
        "A resizable workspace containing one to four equal-capability panes.";

    /// <summary>{0} = the workspace's name.</summary>
    public string WorkspaceLayoutAccessibleNameFormat { get; set; } = "{0} workspace layout";

    public string PaneSplitAccessibleName { get; set; } = "Resizable workspace pane split";

    public string ConnectionsPanel { get; set; } = "Connections panel";

    public string ConnectionsPanelHint { get; set; } = "Show or hide the saved-connections panel.";

    public string Favorites { get; set; } = "Favorites";

    public string NewConnection { get; set; } = "New connection";

    public string PinWorkspace { get; set; } = "Pin Workspace";

    public string PinWorkspaceHint { get; set; } =
        "Keep this workspace one click away. An unsaved workspace is saved first.";

    public string UnpinWorkspace { get; set; } = "Unpin Workspace";

    public string UnpinWorkspaceAccessibleName { get; set; } = "Unpin workspace";

    public string LayoutSideBySide { get; set; } = "Layout: Side by side";

    public string LayoutTopAndBottom { get; set; } = "Layout: Top and bottom";

    public string NamedWorkspacesHint { get; set; } =
        "Named workspaces contain one to four equal-capability browser panes.";

    /// <summary>{0} = the next free workspace number.</summary>
    public string WorkspaceNumberFormat { get; set; } = "Workspace {0}";

    // ------------------------------------------------------------ item dialogs
    public string FileName { get; set; } = "File name";

    public string FolderName { get; set; } = "Folder name";

    public string Create { get; set; } = "Create";

    public string NameAlreadyExists { get; set; } = "An item with that name already exists.";

    public string NameMustBeDirectChild { get; set; } =
        "The name must identify a direct child of the current folder.";

    public string ParentFolderUnavailable { get; set; } = "The parent folder is unavailable.";

    public string SelectOneToRename { get; set; } = "Select exactly one item to rename.";

    public string SelectTwoToBatchRename { get; set; } = "Select at least two items to batch rename.";

    public string SelectOneToInspect { get; set; } = "Select exactly one file to inspect.";

    public string OperationCancelled { get; set; } = "The operation was cancelled.";

    public string EmptyDestinationCreated { get; set; } = "The empty destination folder was created.";

    // ------------------------------------------------------------ agent status
    public string AgentReconnected { get; set; } = "The background agent reconnected and is ready.";

    public string AgentRestarted { get; set; } = "The background agent was restarted and is ready.";

    public string AdaptiveConcurrencyActive { get; set; } = "Adaptive concurrency settings are active.";

    public string ConcurrencyAgentRestartFailed { get; set; } =
        "Concurrency settings were saved, but the background Agent could not restart.";

    public string AgentCannotDelete { get; set; } =
        "The background agent could not delete the selected items.";

    public string AgentCannotEnqueue { get; set; } =
        "The background agent could not enqueue the transfer. Please retry.";

    public string AgentCannotReviewDrop { get; set; } =
        "The background agent could not review the dropped files. Please retry.";

    public string CouldNotReviewDrop { get; set; } = "StorageHub could not review the dropped items.";

    public string CouldNotFinishIndexing { get; set; } =
        "StorageHub could not finish indexing the destination folder.";

    public string ExplorerExportRequiresConnection { get; set; } =
        "Explorer export requires an open saved connection.";

    public string ExplorerIntegrationUnavailable { get; set; } =
        "Drag to File Explorer is unavailable because the StorageHub Explorer integration could not be registered. Repair the installation or restart StorageHub.";

    public string ExternalEditRequiresOneFile { get; set; } =
        "External editing requires exactly one file opened through a saved connection.";

    public string PaneHasNoVerifiedIdentity { get; set; } =
        "The remote pane does not have a verified saved-connection identity.";

    public string FileHasNoObjectIdentity { get; set; } =
        "The selected file does not have a valid bounded object identity.";

    /// <summary>{0} = the transfer ids the agent did not confirm.</summary>
    public string AmbiguousTransfersFormat { get; set; } =
        "The agent did not confirm whether transfer ID(s) {0} were durably enqueued.";

    /// <summary>{0} = the underlying error message.</summary>
    public string DeleteWarningPreferenceFailedFormat { get; set; } =
        "The delete warning preference could not be saved. {0}";

    /// <summary>{0} = the underlying error message.</summary>
    public string ItemCreateFailedFormat { get; set; } = "The item could not be created. {0}";

    /// <summary>{0} = the underlying failure.</summary>
    public string ItemRenameFailedFormat { get; set; } = "The item could not be renamed. {0}";

    /// <summary>{0} = the underlying error message.</summary>
    public string ItemsDeleteFailedFormat { get; set; } = "The selected items could not be deleted. {0}";

    /// <summary>{0} = how many items were sent to the Recycle Bin.</summary>
    public string SentToRecycleBinFormat { get; set; } = "Sent {0:N0} item(s) to the Recycle Bin.";

    /// <summary>{0} = how many remote items were deleted.</summary>
    public string DeletedRemoteItemsFormat { get; set; } = "Deleted {0:N0} remote item(s).";

    /// <summary>{0} = how many were deleted, {1} = why it stopped.</summary>
    public string DeletedThenStoppedFormat { get; set; } = "Deleted {0:N0} item(s), then stopped. {1}";

    /// <summary>{0} = how many were deleted before the agent went away.</summary>
    public string DeletedThenAgentLostFormat { get; set; } =
        "Deleted {0:N0} item(s), then the background agent became unavailable.";

    /// <summary>{0} = the exported file's name.</summary>
    public string SettingsExportedFormat { get; set; } = "Settings exported to {0}.";

    // -------------------------------------------------------- accessible names
    public string ShellAccessibleName { get; set; } = "StorageHub file manager";

    public string MainToolbar { get; set; } = "Main toolbar";

    public string ApplicationStatus { get; set; } = "Application status";

    public string AgentStatus { get; set; } = "Agent status";

    public string UpdateStatus { get; set; } = "Update status";

    public string CurrentLocation { get; set; } = "Current location";

    public string SelectionSummary { get; set; } = "Selection summary";

    public string QueueSummary { get; set; } = "Queue summary";

    public string TransferSpeed { get; set; } = "Transfer speed";

    public string WorkspaceTabs { get; set; } = "Workspace tabs";

    public string WorkspacePaneLayout { get; set; } = "Workspace pane layout";

    public string WorkspaceAndJobQueue { get; set; } = "Workspace and job queue";

    public string ConnectionsAndWorkspaces { get; set; } = "Connections and workspaces";

    public string SourceAndDestinationPanes { get; set; } = "Source and destination panes";

    public string PasteToActivePane { get; set; } = "Paste to active pane";

    public string NoFavoriteConnections { get; set; } = "No favorite connections";

    public string RemoveFromPinned { get; set; } = "Remove this workspace from the pinned list.";

    public string RenameItem { get; set; } = "Rename item";

    public string NewNameLabel { get; set; } = "New name";

    public string DeleteItemsCaption { get; set; } = "Delete items";

    /// <summary>{0} = what is selected, named or counted.</summary>
    public string DeleteItemsPromptFormat { get; set; } = "Delete {0}?";

    /// <summary>
    /// Said plainly, because it is true on both platforms and was not true in 1.x.
    /// </summary>
    /// <remarks>
    /// The WinForms shell sent local deletions to the Recycle Bin through a Windows-only API.
    /// There is no cross-platform equivalent, so 2.0 deletes rather than pretending to a
    /// recoverable one it cannot offer on Linux -- and says so before it does.
    /// </remarks>
    public string DeleteItemsDetail { get; set; } =
        "The items are removed from the storage they are on. This cannot be undone.";

    /// <summary>{0} = how many were removed.</summary>
    public string DeletedItemsFormat { get; set; } = "Deleted {0:N0} item(s).";

    public string NewEmptyFile { get; set; } = "New empty file";

    public string NewFolderTitle { get; set; } = "New folder";

    public string DefaultFileName { get; set; } = "New file.txt";

    public string OpenFolderBeforeCreating { get; set; } =
        "Open a local or remote folder before creating an item.";

    public string OpenFolderBeforeRenaming { get; set; } =
        "Open a local or saved remote folder before renaming items.";

    public string InspectionRequiresConnection { get; set; } =
        "Object inspection currently requires a file opened through a saved connection.";

    public string RemoteDeleteRequiresRoot { get; set; } =
        "Remote deletion requires a saved connection with a verified storage root.";

    /// <summary>{0} = the connection's display name.</summary>
    public string OpenConnectionInPaneFormat { get; set; } = "Open {0} in the active pane.";

    /// <summary>{0} = how many files were queued.</summary>
    public string QueuedExplorerImportFormat { get; set; } =
        "Queued {0:N0} Explorer import file(s).";

    /// <summary>{0} = how many files were queued.</summary>
    public string QueuedFromManifestFormat { get; set; } =
        "Queued {0:N0} file(s) from the recursive folder manifest.";

    /// <summary>{0} = how many items were renamed.</summary>
    public string RenamedItemsFormat { get; set; } = "Renamed {0:N0} item(s).";

    /// <summary>{0} = how many were renamed, {1} = the item it stopped on, {2} = why.</summary>
    public string RenamedThenStoppedFormat { get; set; } =
        "Renamed {0:N0} item(s), then stopped at ‘{1}’. {2}";

    /// <summary>{0} = the old name, {1} = the new name.</summary>
    public string RenamedOneFormat { get; set; } = "Renamed ‘{0}’ to ‘{1}’.";

    /// <summary>
    /// {0} = the created item's name.
    /// </summary>
    /// <remarks>
    /// Two separate sentences rather than one with the noun interpolated: the article and ending
    /// of "folder" and "empty file" differ by language, and composing them produced grammar that
    /// only worked in English.
    /// </remarks>
    public string CreatedFolderFormat { get; set; } = "Created folder ‘{0}’.";

    /// <summary>{0} = the created file's name.</summary>
    public string CreatedFileFormat { get; set; } = "Created empty file ‘{0}’.";

    // ------------------------------------------------------------ batch rename
    public string BatchRenameTitle { get; set; } = "Batch rename";

    public string BatchRenameFind { get; set; } = "Find";

    public string BatchRenameReplaceWith { get; set; } = "Replace with";

    public string BatchRenameFindAccessibleName { get; set; } = "Text to find";

    public string BatchRenameReplaceAccessibleName { get; set; } = "Replacement text";

    public string BatchRenamePreviewAccessibleName { get; set; } = "Rename preview";

    /// <summary>{0} = a name the replacement leaves alone.</summary>
    public string BatchRenameUnchangedFormat { get; set; } = "{0}  (unchanged)";

    /// <summary>{0} = the current name, {1} = the name it would become.</summary>
    public string BatchRenameMappingFormat { get; set; } = "{0}  \u2192  {1}";

    /// <summary>{0} = how many items the replacement would rename.</summary>
    public string BatchRenameSummaryFormat { get; set; } =
        "{0:N0} item(s) will be renamed. Processing stops if a provider rejects a change.";

    // ------------------------------------------------------------ pane header
    public string PaneActions { get; set; } = "Pane actions";

    /// <summary>{0} = the pane's position in the arrangement, counting from one.</summary>
    public string PaneNumberFormat { get; set; } = "Pane {0}";

    /// <summary>{0} = the pane's number. The one pane every command acts on.</summary>
    public string PaneActiveFormat { get; set; } = "Pane {0} (Active)";

    public string SplitRight { get; set; } = "Split right";

    public string SplitBelow { get; set; } = "Split below";

    public string ClosePane { get; set; } = "Close pane";

    public string ShowConnectionBar { get; set; } = "Show connection bar";

    public string MoveOrSwapPane { get; set; } = "Move or swap pane";

    public string SwapWithPane { get; set; } = "Swap";

    public string MoveLeftOfPane { get; set; } = "Move left of it";

    public string MoveAbovePane { get; set; } = "Move above it";

    public string MoveRightOfPane { get; set; } = "Move right of it";

    public string MoveBelowPane { get; set; } = "Move below it";

    // ------------------------------------------------------- new workspace chooser
    public string NewWorkspaceCaption { get; set; } = "New Workspace";

    public string NewWorkspaceChooserAccessibleName { get; set; } = "New workspace layout chooser";

    /// <summary>{0} = the preset's title, {1} = its description, on the line below.</summary>
    public string WorkspacePresetButtonFormat { get; set; } = "{0}\n{1}";

    /// <summary>{0} = the preset's full label.</summary>
    public string CreateWorkspaceWithFormat { get; set; } = "Create workspace with {0}";

    public string RememberLayout { get; set; } = "Use this for new workspaces and stop asking";

    public string RememberLayoutAccessibleName { get; set; } = "Remember this layout";

    public string RememberLayoutAccessibleDescription { get; set; } =
        "New workspaces use the arrangement you pick here. Change it later in Settings, under Workspace.";

    // ------------------------------------------------------------ layout presets
    public string PresetSingle { get; set; } = "Single";

    public string PresetSideBySide { get; set; } = "Side by side";

    public string PresetTopAndBottom { get; set; } = "Top and bottom";

    public string PresetLargeLeftTwoStacked { get; set; } = "Large left, two stacked";

    public string PresetLargeTopTwoBeside { get; set; } = "Large top, two beside";

    public string PresetGrid { get; set; } = "2 x 2 grid";

    /// <summary>The one-pane title. {0} = the pane count, always 1.</summary>
    public string PresetPaneOneFormat { get; set; } = "{0} pane";

    /// <summary>The title for two or more panes. {0} = the pane count.</summary>
    public string PresetPaneManyFormat { get; set; } = "{0} panes";

    /// <summary>{0} = the preset's title, {1} = its description.</summary>
    public string PresetLabelFormat { get; set; } = "{0} - {1}";

    /// <summary>{0} = the workspace's name.</summary>
    public string WorkspaceAccessibleNameFormat { get; set; } = "{0} workspace";

    // ------------------------------------------------------------ staged selection
    public string StagedCut { get; set; } = "Cut";

    public string StagedCopied { get; set; } = "Copied";

    /// <summary>{0} = the staging verb above, {1} = how many items were staged.</summary>
    public string StagedSelectionFormat { get; set; } =
        "{0} {1:N0} item(s). Choose a destination and paste.";

    // ------------------------------------------------------------ agent availability
    /// <summary>Shown while a reconnect is in flight, which is the usual case after a restart.</summary>
    public string AgentReconnecting { get; set; } =
        "The background agent is not answering. StorageHub is reconnecting...";

    /// <summary>Shown once a probe has confirmed the agent is not running.</summary>
    public string AgentOffline { get; set; } =
        "The background agent is not running. Transfers, synchronization, and saved connections are unavailable until it starts.";

    /// <summary>Shown when the agent answered and refused, which is not a reconnect problem.</summary>
    public string AgentRequestFailed { get; set; } =
        "StorageHub could not complete that request.";
}
