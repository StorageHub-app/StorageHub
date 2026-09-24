using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The saved-connections panel, its sidebar groups, and the connection picker.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
/// </remarks>
[LocalizationSection("connections")]
internal sealed class ConnectionStrings : LocalizationModelBase
{
    // ------------------------------------------------------------------- panel
    public string PanelTitle { get; set; } = "Connections";

    public string PanelOptions { get; set; } = "Connections panel options";

    public string PanelOptionsTooltip { get; set; } =
        "Move the panel to the other side, refresh it, or hide it.";

    public string PanelStatus { get; set; } = "Connections status";

    public string PanelAccessibleDescription { get; set; } =
        "Saved connections, grouped by favourites, type, and folder.";

    public string SearchPlaceholder { get; set; } = "Search connections…";

    public string SearchAccessibleName { get; set; } = "Search connections";

    public string NewConnection { get; set; } = "New";

    public string NewConnectionAccessibleName { get; set; } = "New connection";

    public string NewConnectionTooltip { get; set; } = "Create a saved connection.";

    public string MoveToOtherSide { get; set; } = "Move to the other side";

    public string HidePanel { get; set; } = "Hide panel";

    public string Refresh { get; set; } = "Refresh";

    // ------------------------------------------------------------ panel errors
    public string AgentUnavailable { get; set; } =
        "The background agent is unavailable; saved connections could not be loaded.";

    public string LoadFailed { get; set; } = "The connection could not be loaded.";

    public string DeleteFailed { get; set; } = "The connection could not be deleted.";

    public string DeleteFailedThroughAgent { get; set; } =
        "The connection could not be deleted through the background agent.";

    public string UpdateFailed { get; set; } = "The connection could not be updated.";

    public string UpdateFailedThroughAgent { get; set; } =
        "The connection could not be updated through the background agent.";

    public string TestFailedThroughAgent { get; set; } =
        "The connection could not be tested through the background agent.";

    // ------------------------------------------------------------ context menu
    public string ContextOpen { get; set; } = "Open";

    public string ContextOpenInNewPane { get; set; } = "Open in new pane";

    public string ContextEdit { get; set; } = "Edit…";

    public string ContextDelete { get; set; } = "Delete…";

    public string ContextToggleFavorite { get; set; } = "Toggle favorite";

    // ---------------------------------------------------------------- sidebar
    public string GroupFavorites { get; set; } = "Favorites";

    public string GroupStorage { get; set; } = "Storage";

    public string GroupRemoteClients { get; set; } = "Remote clients";

    public string GroupUnsorted { get; set; } = "Unsorted";

    public string SidebarAccessibleName { get; set; } = "Saved connection groups";

    // ------------------------------------------------------------------ the editor
    public string SaveConnection { get; set; } = "Save";

    public string TestConnection { get; set; } = "Test";

    public string Provider { get; set; } = "Provider";

    public string SectionGeneral { get; set; } = "General";

    public string SectionIdentity { get; set; } = "Connection";

    public string FieldConnectionName { get; set; } = "Name";

    public string ConnectionNameHint { get; set; } = "What this connection is called in StorageHub.";

    public string FolderHint { get; set; } = "The group it is filed under in the connections panel.";

    public string TagsHint { get; set; } = "Comma separated, for searching.";

    public string ConnectionSaved { get; set; } = "Connection saved.";

    public string ConnectionNotFound { get; set; } = "That connection no longer exists.";

    public string ConnectionReachable { get; set; } = "The agent reached this connection.";

    public string ConnectionUnreachable { get; set; } =
        "The agent could not reach this connection with these settings.";

    public string ManagerTitle { get; set; } = "Connections";

    public string EditorEmpty { get; set; } = "Choose a connection to edit, or add one.";

    // ------------------------------------------------------------------- groups
    /// <summary>
    /// The group a connection lands in before anybody has filed it.
    /// </summary>
    /// <remarks>
    /// A name rather than "Ungrouped", because it is an ordinary group that can be renamed,
    /// reordered and dragged out of. What makes it the default is only where new connections go.
    /// </remarks>
    public string DefaultGroup { get; set; } = "Connections";

    public string BadgeStorage { get; set; } = "STORAGE";

    public string BadgeClient { get; set; } = "CLIENT";

    public string ClearSearch { get; set; } = "Clear the search";

    public string OpenInActivePane { get; set; } = "Double-click to open in the active pane";

    public string NoMatches { get; set; } = "No connections match that search.";

    public string NewGroup { get; set; } = "New group";

    public string GroupName { get; set; } = "Group name";

    public string RenameGroup { get; set; } = "Rename group";

    public string RemoveGroup { get; set; } = "Remove group";

    /// <summary>{0} = the group's name, {1} = how many connections are in it.</summary>
    public string GroupAccessibleNameFormat { get; set; } = "{0}, {1:N0} connection(s)";

    public string DragConnectionHint { get; set; } = "Drag a connection to file it in another group";

    public string SidebarEmpty { get; set; } = "No saved connections yet";

    public string SidebarNoMatches { get; set; } = "No connections match this search";

    public string SidebarKeyboardHint { get; set; } =
        "Press Enter to open, F2 to edit, Delete to remove, Shift+F10 for more.";

    public string RowEdit { get; set; } = "Edit";

    public string RowDelete { get; set; } = "Delete";

    public string StateDisabled { get; set; } = "Disabled";

    /// <summary>{0} = the connection name.</summary>
    public string EditConnectionAccessibleFormat { get; set; } = "Edit connection {0}";

    /// <summary>{0} = the connection name.</summary>
    public string DeleteConnectionAccessibleFormat { get; set; } = "Delete connection {0}";

    /// <summary>{0} = the group's label.</summary>
    public string GroupAccessibleFormat { get; set; } = "{0} connection group";

    /// <summary>{0} = the provider name, {1} = the connection state.</summary>
    public string ConnectionAccessibleFormat { get; set; } = "{0} saved connection. {1}. ";

    /// <summary>{0} = the endpoint, {1} = the connection state.</summary>
    public string EndpointStateFormat { get; set; } = "{0} · {1}";

    // ----------------------------------------------------------------- picker
    public string PickerTitle { get; set; } = "Connections";

    public string PickerNoMatches { get; set; } = "No connection matches that search.";

    public string PickerActive { get; set; } = "ACTIVE";

    public string PickerSystemLocal { get; set; } = "SYSTEM · LOCAL";

    /// <summary>{0} = the provider name in capitals.</summary>
    public string PickerStorageFormat { get; set; } = "STORAGE · {0}";

    /// <summary>{0} = the provider name in capitals.</summary>
    public string PickerClientFormat { get; set; } = "CLIENT · {0}";

    // ------------------------------------------------------- detail view
    public string SectionServer { get; set; } = "Server";

    public string SectionAuthentication { get; set; } = "Authentication";

    public string SectionSecurity { get; set; } = "Security";

    public string SectionSpeedLimits { get; set; } = "Speed limits";

    public string SpeedLimitPlaceholder { get; set; } = "Unlimited";

    public string SpeedLimitHint { get; set; } =
        "In KiB/s, shared by every transfer on this connection. Leave empty for no limit.";

    public string SectionTransfer { get; set; } = "Transfer";

    public string SectionOrganisation { get; set; } = "Organisation";

    public string SectionStatus { get; set; } = "Status";

    public string FieldProvider { get; set; } = "Provider";

    public string FieldAddress { get; set; } = "Address";

    public string FieldHost { get; set; } = "Host";

    public string FieldPort { get; set; } = "Port";

    public string FieldBucket { get; set; } = "Bucket";

    public string FieldRegion { get; set; } = "Region";

    public string FieldService { get; set; } = "Service";

    public string FieldPathStyle { get; set; } = "Path style";

    public string FieldPath { get; set; } = "Path";

    public string FieldMethod { get; set; } = "Method";

    public string FieldUsername { get; set; } = "Username";

    /// <summary>
    /// Named ...Label rather than ...KeyFormat: a name ending in Format means a composite format
    /// string everywhere else in these models, and this is an ordinary field caption.
    /// </summary>
    public string FieldKeyFormatLabel { get; set; } = "Key format";

    public string FieldPassword { get; set; } = "Password";

    public string FieldAccessKey { get; set; } = "Access key";

    public string FieldSecretKey { get; set; } = "Secret key";

    public string FieldSessionToken { get; set; } = "Session token";

    public string FieldPrivateKey { get; set; } = "Private key";

    public string FieldKeyPassphrase { get; set; } = "Key passphrase";

    public string FieldConnectTimeout { get; set; } = "Connect timeout";

    public string FieldOperationTimeout { get; set; } = "Operation timeout";

    public string FieldRetries { get; set; } = "Retries";

    public string FieldProxy { get; set; } = "Proxy";

    public string FieldUploadLimit { get; set; } = "Upload limit";

    public string FieldDownloadLimit { get; set; } = "Download limit";

    public string FieldEncoding { get; set; } = "Encoding";

    public string FieldFolder { get; set; } = "Folder";

    public string FieldTags { get; set; } = "Tags";

    public string FieldFavorite { get; set; } = "Favorite";

    public string FieldState { get; set; } = "State";

    public string FieldChecked { get; set; } = "Checked";

    public string FieldRoundTrip { get; set; } = "Round trip";

    public string FieldDetail { get; set; } = "Detail";

    public string FieldTls { get; set; } = "TLS";

    public string FieldFtpsMode { get; set; } = "FTPS mode";

    public string FieldClientCert { get; set; } = "Client cert";

    public string FieldHostKey { get; set; } = "Host key";

    public string DetailLoading { get; set; } = "Loading…";

    public string DetailForced { get; set; } = "Forced";

    public string DetailNone { get; set; } = "None";

    public string DetailYes { get; set; } = "Yes";

    public string DetailNo { get; set; } = "No";

    public string DetailStoredInVault { get; set; } = "Stored in vault";

    public string DetailPinned { get; set; } = "Pinned";

    public string DetailTrustOnFirstUse { get; set; } = "Trust on first use";

    public string DetailViewTitle { get; set; } = "Connection details";

    public string DetailEmpty { get; set; } = "Select a connection to see its details.";


    public string DetailFixCredentials { get; set; } = "Fix credentials…";

    public string DetailReviewTrust { get; set; } = "Review trust…";

    /// <summary>{0} = the field name, {1} = its value. Read together by a screen reader.</summary>
    public string DetailFactAccessibleFormat { get; set; } = "{0}: {1}";

    /// <summary>{0} = the connection name, {1} = the provider name.</summary>
    public string DetailHeaderAccessibleFormat { get; set; } = "{0}, {1}";

    // ------------------------------------------------- authentication and trust
    public string AuthAnonymous { get; set; } = "Anonymous";

    public string AuthDefaultCredentialChain { get; set; } = "AWS default credential chain";

    public string AuthStoredCredential { get; set; } = "Stored credential";

    public string AuthUsernamePassword { get; set; } = "Username and password";

    public string AuthAccessKeyAndSecret { get; set; } = "Access key and secret";

    public string AuthPrivateKey { get; set; } = "Private key";

    public string AuthPrivateKeyAndPassword { get; set; } = "Private key and password (MFA)";

    public string TlsSystemTrust { get; set; } = "System trust";

    public string TlsPinnedCertificate { get; set; } = "Pinned certificate";

    public string TlsUnspecified { get; set; } = "Unspecified";

    public string FieldTransport { get; set; } = "Transport";

    public string TransportUnencrypted { get; set; } = "Unencrypted";

    // ----------------------------------------------------------- detail actions
    public string DetailOpen { get; set; } = "Open";

    public string DetailTest { get; set; } = "Test";

    public string DetailTesting { get; set; } = "Testing…";

    public string DetailEdit { get; set; } = "Edit";

    public string DetailDelete { get; set; } = "Delete";

    /// <summary>{0} = the connection name.</summary>
    public string DetailIsFavoriteFormat { get; set; } = "{0} is a favorite. Activate to remove it.";


    // -------------------------------------------------------- connection picker
    public string PickerSearchPlaceholder { get; set; } = "Search connections";

    public string PickerListAccessibleName { get; set; } = "Connections";

    public string PickerNoMatch { get; set; } = "No connection matches that search.";

    /// <summary>Marks the connection already open in the pane. Capitals, to read as a badge.</summary>
    public string PickerActiveBadge { get; set; } = "ACTIVE";

    /// <summary>{0} = the provider name, in capitals.</summary>
    public string PickerClientBadgeFormat { get; set; } = "CLIENT \u00b7 {0}";

    public string PickerSystemLocalBadge { get; set; } = "SYSTEM \u00b7 LOCAL";

    /// <summary>{0} = the provider name, in capitals.</summary>
    public string PickerStorageBadgeFormat { get; set; } = "STORAGE \u00b7 {0}";

    // -------------------------------------------------------------- ssh terminal
    /// <summary>{0} = the connection's display name.</summary>
    public string TerminalCaptionFormat { get; set; } = "{0} \u2014 SSH Terminal";

    /// <summary>{0} = the connection's display name.</summary>
    public string TerminalAccessibleNameFormat { get; set; } = "SSH terminal for {0}";

    public string TerminalOutputAccessibleName { get; set; } = "Interactive SSH terminal output";

    public string TerminalOutputAccessibleDescription { get; set; } =
        "Type to send input. Control+Shift+C copies the selection and Control+Shift+V pastes; "
        + "Control+C always interrupts the remote program.";

    public string TerminalConnecting { get; set; } = "Connecting\u2026";

    public string TerminalConnectionFailed { get; set; } = "Connection failed";

    public string TerminalAgentUnavailable { get; set; } = "Agent unavailable";

    // ---------------------------------------------------------------- icon picker
    public string IconPickerConnectionTitleFormat { get; set; } = "Choose an icon for {0}";

    public string IconPickerFolderTitleFormat { get; set; } = "Choose an icon for {0}";

    public string IconPickerUse { get; set; } = "Use icon";

    public string IconPickerUseDefault { get; set; } = "Use default";

    public string IconPickerCancel { get; set; } = "Cancel";

    public string ChooseIcon { get; set; } = "Choose icon…";

    public string FieldIcon { get; set; } = "Icon";

    public string TerminalDisconnected { get; set; } = "Disconnected";

    public string TerminalRestartingAgent { get; set; } = "Restarting the agent…";

    public string TerminalAgentOutOfDate { get; set; } =
        "The background agent is out of date and could not be restarted. Restart StorageHub.";

    /// <summary>Shown in the session when the agent could not retain output the terminal missed.</summary>
    public string TerminalOutputDropped { get; set; } =
        "Some output was dropped because it arrived faster than it could be read.";

    /// <summary>{0} = the display name, {1} = columns, {2} = rows.</summary>
    public string TerminalConnectedFormat { get; set; } = "Connected \u00b7 {0} \u00b7 {1}\u00d7{2}";

    /// <summary>{0} = columns, {1} = rows.</summary>
    public string TerminalResizedFormat { get; set; } = "Connected \u00b7 {0}\u00d7{1}";

    public string TerminalResizeFailed { get; set; } =
        "The terminal could not be resized; it will retry after the next window resize.";

    /// <summary>A line StorageHub itself writes into the terminal. {0} = the message.</summary>
    public string TerminalSystemLineFormat { get; set; } = "[StorageHub] {0}";

    public string TerminalCouldNotOpen { get; set; } =
        "The background agent could not open the SSH terminal.";

    public string TerminalSessionClosed { get; set; } = "SSH session closed.";

    public string TerminalLostAgent { get; set; } = "Lost contact with the background agent.";

    public string TerminalCouldNotSendInput { get; set; } =
        "Could not send input to the background agent.";
}
