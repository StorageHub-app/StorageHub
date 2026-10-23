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

    /// <summary>
    /// What a screen reader calls a number box's two arrows. Their content is an icon, which was
    /// read out as "PathIcon".
    /// </summary>
    public string SpinIncrease { get; set; } = "Increase";

    public string SpinDecrease { get; set; } = "Decrease";

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

    /// <summary>The Open and Save dialogs' name for a <c>.shw</c> file.</summary>
    public string WorkspaceFileType { get; set; } = "StorageHub workspace";

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

    /// <summary>{0} = the workspace's name, in the Workspace menu, when its file has gone.</summary>
    public string WorkspaceMissingEntryFormat { get; set; } = "{0} (missing)";

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

    public string SelectOneToInspect { get; set; } = "Select exactly one file or folder to see its properties.";

    public string OperationCancelled { get; set; } = "The operation was cancelled.";

    public string EmptyDestinationCreated { get; set; } = "The empty destination folder was created.";

    // ------------------------------------------------------------ agent status
    public string AgentReconnected { get; set; } = "The background agent reconnected and is ready.";

    public string AgentRestarted { get; set; } = "The background agent was restarted and is ready.";

    public string AdaptiveConcurrencyActive { get; set; } = "Adaptive concurrency settings are active.";

    public string ConcurrencyAgentRestartFailed { get; set; } =
        "Transfer settings were saved, but the background agent could not restart to use them.";

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

    /// <summary>{0} = transfers the agent took before the one it did not confirm.</summary>
    public string AcknowledgedTransfersFormat { get; set; } =
        "{0:N0} transfer(s) were durably acknowledged.";

    /// <summary>{0} = files after the unconfirmed one that were never sent.</summary>
    public string UnsubmittedTransfersFormat { get; set; } =
        "{0:N0} later selected file(s) were not submitted.";

    public string CheckQueueForAmbiguousTransfers { get; set; } =
        "Check the queue for those exact IDs before submitting replacement jobs.";

    /// <summary>{0} = transfers queued before the failure; {1} = the failure.</summary>
    public string PartiallyQueuedFormat { get; set; } =
        "{0:N0} transfer(s) were durably accepted before the next request failed. {1}";

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

    public string MoreToolbarCommands { get; set; } = "More toolbar commands";

    public string ApplicationStatus { get; set; } = "Application status";

    public string StartupStatus { get; set; } = "Startup status";

    public string StartupDetails { get; set; } = "Startup details";

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
    /// <summary>{0} = the command id. Shown when a shortcut reaches something not built yet.</summary>
    public string CommandNotBuiltFormat { get; set; } = "{0} is not built yet.";

    public string PaneActions { get; set; } = "Pane actions";

    /// <summary>{0} = the pane's position in the arrangement, counting from one.</summary>
    public string PaneNumberFormat { get; set; } = "Pane {0}";

    /// <summary>{0} = the pane's number. The one pane every command acts on.</summary>
    public string PaneActiveFormat { get; set; } = "Pane {0} (Active)";

    public string SplitRight { get; set; } = "Split right";

    public string SplitBelow { get; set; } = "Split below";

    public string ClosePane { get; set; } = "Close pane";

    public string ShowConnectionBar { get; set; } = "Show connection bar";

    /// <summary>The pane's FILES row of file commands, which the list's right-click menu repeats.</summary>
    public string ShowFilesBar { get; set; } = "Show files bar";

    public string ShowDirectoryTree { get; set; } = "Show directory tree";

    public string NoOtherLocationsVisited { get; set; } = "No other locations visited yet";

    public string HistoryHint { get; set; } = "Folders this pane has shown in this session, newest first.";

    public string ThemeNowFormat { get; set; } = "Theme: {0}";

    public string HiddenFilesShown { get; set; } = "Hidden files are shown.";

    public string HiddenFilesNotShown { get; set; } = "Hidden files are not shown.";

    public string DisconnectedFromFormat { get; set; } = "Disconnected from {0}.";

    public string TestingConnectionFormat { get; set; } = "Testing {0}…";

    public string TestedConnectionFormat { get; set; } = "{0}: {1}";

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

    public string SearchTitle { get; set; } = "Search";

    public string SearchIntroFormat { get; set; } = "Searches {0} and every folder under it for names that match.";

    public string SearchQueryLabel { get; set; } = "Name:";

    public string SearchQueryHint { get; set; } = "Part of a name, or a pattern such as *.jpg";

    public string SearchButton { get; set; } = "Search";

    public string SearchStop { get; set; } = "Stop";

    public string SearchGoTo { get; set; } = "Go to";

    public string SearchColumnName { get; set; } = "Name";

    public string SearchColumnFolder { get; set; } = "Folder";

    public string SearchColumnSize { get; set; } = "Size";

    public string SearchColumnModified { get; set; } = "Modified";

    public string SearchFolderKind { get; set; } = "Folder";

    public string SearchReady { get; set; } = "Type a name and press Search.";

    public string Searching { get; set; } = "Searching…";

    public string SearchingFormat { get; set; } = "Searching… {0:N0} items looked at.";

    public string SearchFoundFormat { get; set; } = "Found {0:N0} in {1:N0} items.";

    public string SearchNothingFormat { get; set; } = "Nothing matches among {0:N0} items.";

    public string SearchLimitFormat { get; set; } =
        "Found {0:N0} in the first {1:N0} items; the search stopped at its limit. Search a folder further down for the rest.";

    public string SearchStopped { get; set; } = "Search stopped.";

    public string SearchNeedsAName { get; set; } = "Type part of a name to search for.";

    public string SearchNeedsAFolder { get; set; } = "Open a folder in the pane to search it.";

    public string SearchTimedOut { get; set; } = "The agent took too long to list a folder; the search stopped.";

    public string ChecksumsTitle { get; set; } = "Checksums";

    public string ChecksumsIntro { get; set; } =
        "The SHA-256 of each selected file: the server's own where it keeps one, otherwise read and computed. Paste a checksum below to compare every file with it.";

    public string ChecksumExpected { get; set; } = "Expected:";

    public string ChecksumExpectedHint { get; set; } = "A SHA-256 to compare with, as shown on a download page";

    public string ChecksumCopy { get; set; } = "Copy checksums";

    public string ChecksumColumnSha256 { get; set; } = "SHA-256";

    public string ChecksumColumnSource { get; set; } = "From";

    public string ChecksumColumnMatch { get; set; } = "Comparison";

    public string ChecksumPending { get; set; } = "Waiting…";

    public string ChecksumFromServer { get; set; } = "The server";

    public string ChecksumComputed { get; set; } = "Computed from the file";

    public string ChecksumFilesOnly { get; set; } = "Only a file has a checksum.";

    public string ChecksumTimedOut { get; set; } = "Reading the file took too long.";

    public string ChecksumMatchesExpected { get; set; } = "Matches";

    public string ChecksumDiffersFromExpected { get; set; } = "Differs";

    public string ChecksumSameAsFormat { get; set; } = "Same as {0}";

    public string ChecksumUnique { get; set; } = "Unique";

    public string ChecksumWorking { get; set; } = "Reading checksums…";

    public string ChecksumProgressFormat { get; set; } = "Read {0} of {1}…";

    public string ChecksumReady { get; set; } = "Checksum ready.";

    public string ChecksumAllIdenticalFormat { get; set; } = "All {0} files are identical.";

    public string ChecksumDistinctFormat { get; set; } = "{0} different checksums among {1} files.";

    public string ChecksumExpectedSummaryFormat { get; set; } = "{0} of {1} match the expected checksum.";

    public string ChecksumFailedFormat { get; set; } = "{0} could not be read.";

    public string ChecksumSkippedFormat { get; set; } = "{0} folder(s) or extra item(s) left out.";

    public string ChecksumNoneRead { get; set; } = "No checksum could be read.";

    public string FtpConsoleTitleFormat { get; set; } = "FTP Command Console — {0}";

    public string FtpConsoleIntro { get; set; } =
        "Commands go to the server one at a time, each on a short connection of its own, and the reply is shown below. Listings and transfers stay in the panes.";

    public string FtpConsoleCommandLabel { get; set; } = "Command:";

    public string FtpConsoleCommandHint { get; set; } =
        "SYST, FEAT, STAT, SITE CHMOD 644 file.txt… Up and Down go through earlier commands";

    public string FtpConsoleTranscript { get; set; } = "Transcript";

    public string FtpConsoleSend { get; set; } = "Send";

    public string FtpConsoleClear { get; set; } = "Clear";

    public string FtpConsoleCopy { get; set; } = "Copy transcript";

    public string FtpConsoleCopied { get; set; } = "Transcript copied.";

    public string FtpConsoleReady { get; set; } = "Type a command and press Enter.";

    public string FtpConsoleSending { get; set; } = "Waiting for the server…";

    public string FtpConsoleRepliedFormat { get; set; } = "The server answered {0}.";

    public string FtpConsoleNotSent { get; set; } = "Not sent.";

    public string FtpConsoleRefusedDataConnection { get; set; } =
        "Not sent: this command opens a data connection. Use the panes for listings and transfers.";

    public string FtpConsoleRefusedLogin { get; set; } =
        "Not sent: the connection logs in with its saved account, and the console does not log in or out.";

    public string FtpConsoleRefusedSecurity { get; set; } = "Not sent: TLS is set by the connection's settings.";

    public string FtpConsoleRefusedSessionSetting { get; set; } =
        "Not sent: each command runs on a session of its own, so a setting such as CWD or TYPE would not last. Use the address bar to change folder.";

    public string FtpConsoleRefusedMalformed { get; set; } =
        "Not sent: a command is one line of at most 512 characters, starting with its verb.";

    public string FtpConsoleTruncated { get; set; } = "(The reply was cut short at 16,384 characters.)";

    public string FtpConsoleTimedOut { get; set; } = "The server did not answer in time.";

    public string FtpConsoleUnavailable { get; set; } = "The server could not be reached, or it closed the connection.";

    public string FtpConsoleUnauthorized { get; set; } =
        "The server refused the saved account, or the connection is disabled.";

    public string FtpConsoleSecurity { get; set; } =
        "The connection needs a trust decision first; test it from the connections panel.";

    public string FtpConsoleNotFound { get; set; } = "The saved connection was not found.";

    public string FtpConsoleUnsupported { get; set; } = "Only FTP and FTPS connections take raw commands.";

    public string FtpConsoleFailed { get; set; } = "The command could not be sent.";

    public string FtpConsoleAgentUnavailable { get; set; } = "The background agent could not be reached.";

    public string DiagnosticsTitle { get; set; } = "Diagnostics";

    public string DiagnosticsIntro { get; set; } =
        "This installation in a few lines, to copy into a report, and a support bundle to attach to one.";

    public string DiagnosticsPrivacy { get; set; } =
        "The bundle holds the version, the system, the agent's state and the error log with your user name, computer name and home folder taken out. No connection, password or key goes in it.";

    public string DiagnosticsVersion { get; set; } = "StorageHub";

    public string DiagnosticsOperatingSystem { get; set; } = "System";

    public string DiagnosticsRuntime { get; set; } = "Runtime";

    public string DiagnosticsLanguage { get; set; } = "Language";

    public string DiagnosticsAgent { get; set; } = "Agent";

    public string DiagnosticsAgentNotHeard { get; set; } = "not heard from yet";

    public string DiagnosticsRunning { get; set; } = "Running";

    public string DiagnosticsRunningFormat { get; set; } = "{0} transfer(s), {1} sync run(s)";

    public string DiagnosticsQueue { get; set; } = "Queue";

    public string DiagnosticsQueueNotRead { get; set; } = "not read yet";

    public string DiagnosticsQueueEmpty { get; set; } = "empty";

    public string DiagnosticsLogFolder { get; set; } = "Logs";

    public string DiagnosticsCollected { get; set; } = "Collected";

    public string DiagnosticsCopy { get; set; } = "Copy report";

    public string DiagnosticsCopied { get; set; } = "The report is on the clipboard.";

    public string DiagnosticsSaveBundle { get; set; } = "Save bundle…";

    public string DiagnosticsBundleFilter { get; set; } = "Support bundle";

    public string DiagnosticsSavedFormat { get; set; } = "Saved {0} with {1} file(s).";

    public string DiagnosticsSaveFailedFormat { get; set; } = "The bundle could not be saved: {0}";

    public string DiagnosticsOpenLogs { get; set; } = "Open logs";

    public string LogsTitle { get; set; } = "Logs";

    public string LogsFolderIsFormat { get; set; } = "The logs are in {0}";

    public string LinkNotOpenedTitle { get; set; } = "Open this address in a browser";
}
