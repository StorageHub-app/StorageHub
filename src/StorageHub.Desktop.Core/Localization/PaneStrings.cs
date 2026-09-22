using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The file pane: its address bar, its list, its commands and what it reports.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
///
/// Clipboard format names and drag-and-drop identifiers are absent on purpose: they are exchanged
/// with Explorer and other processes, so they are protocol, not prose.
/// </remarks>
[LocalizationSection("pane")]
internal sealed class PaneStrings : LocalizationModelBase
{
    // ------------------------------------------------------------------- chrome
    public string ListAccessibleDescription { get; set; } =
        "A virtualized list of files, folders, and storage objects.";

    public string ConnectionsHome { get; set; } = "Connections Home";

    public string Connections { get; set; } = "Connections";

    public string SavedConnections { get; set; } = "Saved connections";

    public string Favorites { get; set; } = "Favorites";

    public string Favorite { get; set; } = "Favorite";

    public string LocalDrives { get; set; } = "Local drives";

    public string OnThisDevice { get; set; } = "On this device";

    public string ThisPc { get; set; } = "This PC";

    public string UseManageToAddOne { get; set; } =
        "Add one from the Connections panel";

    public string NoEnabledConnections { get; set; } = "No enabled saved connections";

    public string ChooseConnection { get; set; } =
        "Choose a saved connection to browse its storage root.";

    public string ConnectionPickerHint { get; set; } =
        "Choose the local or remote connection displayed in this pane.";

    public string ConnectionPickerAccessibleName { get; set; } =
        "Select the local or remote connection displayed in this pane.";

    public string SelectProfileToConnect { get; set; } = "Select the profile to connect";

    public string FoldersAndRoots { get; set; } = "Folders and connection roots.";

    // ------------------------------------------------------------- address bar
    public string LocalAddressHint { get; set; } =
        "Enter an absolute local or UNC folder path, then press Enter.";

    public string RemoteAddressHint { get; set; } =
        "Enter a path relative to the selected saved connection root, then press Enter.";

    public string RemoteAddressAccessibleName { get; set; } =
        "The address on the selected remote connection.";

    public string InvalidFolderPath { get; set; } = "The address is not a valid folder path.";

    public string InvalidRemotePath { get; set; } = "The address is not a valid remote path.";

    public string GoUpOneLevel { get; set; } = "Go up one level";

    public string ParentFolder { get; set; } = "Parent folder";

    public string Forward { get; set; } = "Forward";

    public string Refresh { get; set; } = "Refresh";

    public string CancelLoadAndRefresh { get; set; } = "Cancel current load and refresh";

    // ----------------------------------------------------------------- filter
    public string FilterLabel { get; set; } = "Filter:";

    public string FilterPlaceholder { get; set; } = "Filter by name (* and ? wildcards supported)";

    public string FilterAccessibleName { get; set; } =
        "Filter the visible items by name. Wildcards star and question mark are supported.";

    public string NoItemsMatchFilter { get; set; } = "No items match the current filter.";

    public string FolderIsEmpty { get; set; } = "This folder is empty.";

    // --------------------------------------------------------------- commands
    public string MoreFileCommands { get; set; } = "More file commands";

    public string MoreCommandsHint { get; set; } = "Open additional commands for this file pane.";

    public string Open { get; set; } = "Open";

    public string NewFolder { get; set; } = "New folder";

    public string NewEmptyFile { get; set; } = "New empty file...";

    public string EditInExternalEditor { get; set; } = "Edit in external editor...";

    public string OpenInExternalEditor { get; set; } = "Open in external editor...";

    public string Rename { get; set; } = "Rename";

    public string BatchRename { get; set; } = "Batch rename...";

    public string Copy { get; set; } = "Copy";

    public string Cut { get; set; } = "Cut";

    public string Move { get; set; } = "Move";

    public string Paste { get; set; } = "Paste";

    public string Delete { get; set; } = "Delete";

    public string SelectAll { get; set; } = "Select all";

    public string InvertSelection { get; set; } = "Invert selection";

    public string Properties { get; set; } = "Properties...";

    public string CreateFolderHere { get; set; } = "Create a folder in this location";

    public string StageForCopying { get; set; } = "Stage selected items for copying";

    public string StageForMoving { get; set; } = "Stage selected items for moving";

    /// <summary>{0} = what was staged, {1} = the pane it was staged from.</summary>
    public string StagedCopyFormat { get; set; } = "Ready to copy {0} from {1}";

    /// <summary>{0} = what was staged, {1} = the pane it was staged from.</summary>
    public string StagedMoveFormat { get; set; } = "Ready to move {0} from {1}";

    public string ClearStaged { get; set; } = "Clear the staged items";

    public string TerminalPending { get; set; } = "The terminal for this connection is not open yet.";

    public string TerminalConnectHint { get; set; } =
        "SSH clients open a terminal here instead of a file listing.";

    public string LoadMore { get; set; } = "Load more";

    public string LoadNextPage { get; set; } = "Load the next page";

    public string LoadNextPageHint { get; set; } =
        "Load the next bounded page from the selected remote folder.";

    // ---------------------------------------------------------------- columns
    public string ColumnName { get; set; } = "Name";

    public string ColumnSize { get; set; } = "Size";

    public string ColumnType { get; set; } = "Type";

    public string ColumnModified { get; set; } = "Modified";

    public string ColumnStatus { get; set; } = "Status";

    public string ColumnPrefix { get; set; } = "Prefix";

    public string Folder { get; set; } = "Folder";

    public string SymbolicLink { get; set; } = "Symbolic link";

    public string StorageItem { get; set; } = "Storage item";

    // ----------------------------------------------------------------- status
    public string Loading { get; set; } = "Loading…";

    public string LoadingConnections { get; set; } = "Loading saved connections…";

    public string LoadingLocalDrives { get; set; } = "Loading local drives…";

    public string FetchingFolder { get; set; } = "Fetching folder…";

    public string Ready { get; set; } = "Ready";

    public string Saved { get; set; } = "Saved";

    public string Disabled { get; set; } = "Disabled";

    public string Disconnected { get; set; } = "Disconnected";

    public string Unavailable { get; set; } = "Unavailable";

    public string NotTested { get; set; } = "Not tested";

    public string NeedsAttention { get; set; } = "Needs attention";

    public string CredentialsNeedAttention { get; set; } = "Credentials need attention";

    public string TrustDecisionRequired { get; set; } = "Trust decision required";

    public string ConnectionCouldNotOpen { get; set; } = "Connection could not be opened";

    public string AutoReconnectDisabled { get; set; } =
        "Automatic reconnect is disabled. Click here to Connect.";

    public string LocationCouldNotOpen { get; set; } = "StorageHub could not open this location.";

    public string RemoteLocationCouldNotOpen { get; set; } = "The remote location could not be opened.";

    public string IndexingFailed { get; set; } = "StorageHub could not continue indexing this folder.";

    public string ChooseFolderBeforeTransfer { get; set; } =
        "Choose a storage folder in this pane before transferring files.";

    public string FinishIndexingFirst { get; set; } =
        "Finish indexing the destination folder before reviewing transfer conflicts.";

    public string NotTransferable { get; set; } =
        "The pane contains an item that is not a transferable storage object.";

    public string SelectionChangedWhileCapturing { get; set; } =
        "The pane selection changed while it was being captured.";

    public string InvalidNavigationResult { get; set; } =
        "The local browser returned an invalid navigation result.";

    public string InvalidRemoteNavigationResult { get; set; } =
        "The remote browser returned an invalid navigation result.";

    public string InvalidConnectionLoadResult { get; set; } =
        "The remote browser returned an invalid connection-load result.";

    public string UnknownProvider { get; set; } = "Unknown storage provider.";

    // ------------------------------------------------------------ explorer drop
    public string DropRequiresConnection { get; set; } =
        "Explorer drops require an open saved connection folder.";

    public string DropCouldNotInitialize { get; set; } =
        "StorageHub could not initialize the Explorer drop.";

    public string DropNotInitialized { get; set; } = "The drop could not be initialized.";

    public string DroppedItemsUnusable { get; set; } =
        "Only files and folders on this computer can be dropped here.";


    public string NoDestinationReported { get; set; } = "No destination was reported.";

    public string DragCouldNotStart { get; set; } = "Windows could not start the drag.";

    /// <summary>{0} = the underlying error message.</summary>
    public string DragCouldNotStartFormat { get; set; } =
        "Windows could not start the drag operation: {0}";

    /// <summary>{0} = the underlying error message.</summary>
    public string DropCouldNotQueueFormat { get; set; } =
        "StorageHub could not queue the Explorer drop: {0}";

    /// <summary>{0} = the destination path.</summary>
    public string QueuedToFormat { get; set; } = "Queued in StorageHub → {0}";

    // ---------------------------------------------------------------- formats
    /// <summary>{0} = the connection's display name.</summary>
    public string ActiveConnectionFormat { get; set; } = "Active connection: {0}";

    /// <summary>{0} = the pane's title in lower case.</summary>
    public string BrowseEndpointFormat { get; set; } =
        "Browse and select items on the {0} endpoint.";

    /// <summary>{0} = the connection name, {1} = the displayed path.</summary>
    public string ShowingConnectionFormat { get; set; } = "Showing {0} {1}";

    /// <summary>{0} = the displayed location.</summary>
    public string ShowingLocationFormat { get; set; } = "Showing {0}";

    /// <summary>{0} = the round-trip time in milliseconds.</summary>
    public string HealthyFormat { get; set; } = "Healthy · {0:N0} ms";

    /// <summary>{0} = the provider's display name.</summary>
    public string StorageGroupFormat { get; set; } = "Storage · {0}";

    /// <summary>{0} = the provider's display name.</summary>
    public string ClientGroupFormat { get; set; } = "Clients · {0}";

    public string EmptyFolderNotice { get; set; } = "Empty folder notice";

    public string FetchingFolderContents { get; set; } = "Fetching folder contents";

    public string FolderLoadingIndicator { get; set; } = "Folder loading indicator";

    public string LoadNextPageLabel { get; set; } = "Load next page";

    public string ReviewAndDelete { get; set; } = "Review and delete selected items";

    public string ReviewAndPaste { get; set; } = "Review and paste the StorageHub clipboard here";

    public string SavedConnection { get; set; } = "Saved connection";

    public string ConnectionsUnavailable { get; set; } = "Saved connections are temporarily unavailable.";

    /// <summary>{0} = the profile's display name or id.</summary>
    /// <summary>{0} = the provider, e.g. "Amazon S3".</summary>
    public string SavedProfileSummaryFormat { get; set; } = "{0} saved profile";

    public string SavedProfileUnavailableFormat { get; set; } =
        "Saved profile '{0}' is unavailable. Select another profile.";

    // ------------------------------------------------------ connection state line
    /// <remarks>
    /// The leading glyph is part of the text rather than drawn separately, so a translation keeps
    /// the state readable when the words around it change length. Keep it when translating.
    /// </remarks>
    public string StateTestingConnection { get; set; } = "● Testing connection…";

    public string StateReady { get; set; } = "● Ready";

    public string StateLoading { get; set; } = "● Loading…";

    public string StateSshTerminal { get; set; } = "● SSH terminal";

    public string StateChooseConnection { get; set; } = "○ Choose a saved connection";

    public string StateLocationUnavailable { get; set; } = "⚠ Location unavailable";

    public string ItemCountEmpty { get; set; } = "0 items";

    /// <summary>{0} = the pane's title.</summary>
    public string ItemSummaryAccessibleNameFormat { get; set; } = "{0} item summary";

    // ------------------------------------------------------ pane accessible names
    /// <summary>{0} = the pane's title.</summary>
    public string PaneHeaderAccessibleNameFormat { get; set; } = "{0} header";

    /// <summary>{0} = the pane's title.</summary>
    public string PaneBrowserAccessibleNameFormat { get; set; } = "{0} browser pane";

    /// <summary>{0} = the pane's title.</summary>
    public string ConnectionIdentityAccessibleNameFormat { get; set; } = "{0} connection identity";

    /// <summary>{0} = the pane's title.</summary>
    public string ConnectionSelectionAccessibleNameFormat { get; set; } =
        "{0} connection selection and status";

}
