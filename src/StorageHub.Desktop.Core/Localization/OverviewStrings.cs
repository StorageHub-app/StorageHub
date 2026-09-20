using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The Welcome dashboard: the headline, the metric tiles, and the three lists on it.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
/// </remarks>
[LocalizationSection("overview")]
internal sealed class OverviewStrings : LocalizationModelBase
{
    public string OverviewAccessibleName { get; set; } = "StorageHub overview";

    public string Headline { get; set; } = "Welcome to StorageHub";

    public string Subheading { get; set; } =
        "Your connections, transfers, and items that need attention in one place.";

    // ----------------------------------------------------------------- actions
    public string ActionNewWorkspace { get; set; } = "New workspace";

    public string ActionConnections { get; set; } = "Connections";

    public string ActionSyncTasks { get; set; } = "Sync tasks";

    public string ActionRefresh { get; set; } = "Refresh";

    // ----------------------------------------------------------------- metrics
    public string MetricAgent { get; set; } = "Agent";

    public string MetricActiveTransfers { get; set; } = "Active transfers";

    public string MetricQueued { get; set; } = "Queued";

    public string MetricNeedsAttention { get; set; } = "Needs attention";

    public string AgentConnected { get; set; } = "Connected";

    public string AgentRecoveryMode { get; set; } = "Recovery mode";

    public string AgentOffline { get; set; } = "Offline";

    public string AgentStarting { get; set; } = "Starting";

    // -------------------------------------------------------------- workspaces
    public string WorkspacesTitle { get; set; } = "Workspaces";

    public string WorkspacesSubtitle { get; set; } =
        "Pinned layouts first, then the ones you opened most recently";

    public string WorkspacesEmpty { get; set; } = "No workspaces yet";

    public string WorkspacesEmptyHint { get; set; } = "Save a workspace to pin it here";

    public string WorkspaceStatePinned { get; set; } = "Pinned";

    public string WorkspaceStateRecent { get; set; } = "Recent";

    public string WorkspaceStateMissing { get; set; } = "Missing";

    // ------------------------------------------------------------- connections
    public string ConnectionsTitle { get; set; } = "Recent connections";

    public string ConnectionsSubtitle { get; set; } =
        "Opened this session, followed by saved favorites";

    public string ConnectionsEmpty { get; set; } = "No saved connections yet";

    public string ConnectionFavorite { get; set; } = "Favorite";

    // --------------------------------------------------------------- attention
    public string AttentionTitle { get; set; } = "Needs attention";

    public string AttentionSubtitle { get; set; } = "Failed, blocked, or conflicting transfers";

    public string AttentionEmpty { get; set; } = "Nothing needs attention";

    // ----------------------------------------------------------------- columns
    public string ColumnName { get; set; } = "Name";

    public string ColumnLocation { get; set; } = "Location";

    public string ColumnState { get; set; } = "State";

    public string ColumnProvider { get; set; } = "Provider";

    public string ColumnDetails { get; set; } = "Details";

    public string ColumnUpdated { get; set; } = "Updated";

    // ------------------------------------------------------------ context menu
    public string ContextOpen { get; set; } = "Open";

    public string ContextPin { get; set; } = "Pin";

    public string ContextUnpin { get; set; } = "Unpin";

    public string ContextRemoveFromList { get; set; } = "Remove from list";

    public string ContextCopyPath { get; set; } = "Copy path";

    // ------------------------------------------------------------------ status
    public string StatusDeferred { get; set; } = "Overview will load when StorageHub is shown.";

    public string StatusRefreshing { get; set; } = "Refreshing overview...";

    /// <summary>{0} = the time of the refresh.</summary>
    public string StatusUpdatedFormat { get; set; } = "Updated {0:t}";
}
