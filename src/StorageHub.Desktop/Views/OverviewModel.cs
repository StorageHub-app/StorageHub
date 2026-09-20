using Lucide.Avalonia;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Which palette role a metric card takes.
/// </summary>
/// <remarks>
/// A class rather than a brush, so the card's colour resolves through the design tokens and follows
/// an appearance change. The WinForms cards each held their own resolved <c>Color</c> and had to be
/// re-tinted by the theme walker.
/// </remarks>
internal enum MetricTone
{
    /// <summary>No colour of its own. A count that is neither good nor bad, such as disabled tasks.</summary>
    Neutral,

    Primary,
    Success,
    Warning,
    Danger,
}

/// <summary>One of the four cards along the top of the overview.</summary>
internal sealed record MetricCard(
    string Value,
    string Caption,
    LucideIconKind Icon,
    MetricTone Tone)
{
    internal bool IsNeutral => Tone == MetricTone.Neutral;

    internal bool IsPrimary => Tone == MetricTone.Primary;

    internal bool IsSuccess => Tone == MetricTone.Success;

    internal bool IsWarning => Tone == MetricTone.Warning;

    internal bool IsDanger => Tone == MetricTone.Danger;
}

/// <summary>A row of the workspaces table.</summary>
internal sealed record WorkspaceRow(string Name, string Location, string State);

/// <summary>A row of the recent-connections table.</summary>
internal sealed record RecentConnectionRow(string Name, string Provider, string Details);

/// <summary>A row of the needs-attention table.</summary>
internal sealed record AttentionRow(string Name, string State, string Updated);

/// <summary>
/// The overview, which is what the Welcome tab shows.
/// </summary>
/// <remarks>
/// The first screen anyone sees, so it is the first one ported. Every string comes from
/// OverviewStrings, which already had all of them - including the empty states, which this screen
/// spends most of its life showing and which are written out rather than left blank.
/// </remarks>
internal sealed record OverviewModel(
    string Headline,
    string Subheading,
    string NewWorkspaceLabel,
    string ConnectionsLabel,
    string SyncTasksLabel,
    string RefreshLabel,
    IReadOnlyList<MetricCard> Metrics,
    string WorkspacesTitle,
    string WorkspacesSubtitle,
    IReadOnlyList<WorkspaceRow> Workspaces,
    string ConnectionsTitle,
    string ConnectionsSubtitle,
    IReadOnlyList<RecentConnectionRow> RecentConnections,
    string AttentionTitle,
    string AttentionSubtitle,
    IReadOnlyList<AttentionRow> Attention,
    string ColumnName,
    string ColumnLocation,
    string ColumnState,
    string ColumnProvider,
    string ColumnDetails,
    string ColumnUpdated)
{
    /// <summary>
    /// The overview as it stands with nothing saved and nothing running.
    /// </summary>
    /// <remarks>
    /// The empty rows are rows, not an absence of them: the WinForms screen fills each table with a
    /// single line saying what would be there ("No workspaces yet" / "Save a workspace to pin it
    /// here"), which reads better than an empty grid and keeps the column widths honest.
    /// </remarks>
    internal static OverviewModel Create(ShellStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var strings = Ui.Overview;

        return new(
            strings.Headline,
            strings.Subheading,
            strings.ActionNewWorkspace,
            strings.ActionConnections,
            strings.ActionSyncTasks,
            strings.ActionRefresh,
            [
                new(
                    AgentValue(status, strings),
                    strings.MetricAgent,
                    LucideIconKind.Server,
                    status.AgentIsHealthy ? MetricTone.Success : MetricTone.Primary),
                new(
                    status.ActiveJobs.ToString(System.Globalization.CultureInfo.CurrentCulture),
                    strings.MetricActiveTransfers,
                    LucideIconKind.Play,
                    MetricTone.Success),
                new(
                    status.QueuedJobs.ToString(System.Globalization.CultureInfo.CurrentCulture),
                    strings.MetricQueued,
                    LucideIconKind.ListOrdered,
                    MetricTone.Primary),
                new("0", strings.MetricNeedsAttention, LucideIconKind.TriangleAlert, MetricTone.Warning),
            ],
            strings.WorkspacesTitle,
            strings.WorkspacesSubtitle,
            [new(strings.WorkspacesEmpty, strings.WorkspacesEmptyHint, string.Empty)],
            strings.ConnectionsTitle,
            strings.ConnectionsSubtitle,
            [new(strings.ConnectionsEmpty, string.Empty, string.Empty)],
            strings.AttentionTitle,
            strings.AttentionSubtitle,
            [new(strings.AttentionEmpty, string.Empty, string.Empty)],
            strings.ColumnName,
            strings.ColumnLocation,
            strings.ColumnState,
            strings.ColumnProvider,
            strings.ColumnDetails,
            strings.ColumnUpdated);
    }

    /// <summary>
    /// The agent's state in the card's own shorter wording.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="ShellStatusSnapshot.AgentText"/>: the status bar says
    /// "Agent: connected" because it has no label beside it, while the card says "Connected" under a
    /// caption that already reads "Agent". OverviewStrings has always carried both sets.
    /// </remarks>
    private static string AgentValue(ShellStatusSnapshot status, OverviewStrings strings) =>
        status.AgentState switch
        {
            AgentConnectionState.Connected => strings.AgentConnected,
            AgentConnectionState.RecoveryOnly => strings.AgentRecoveryMode,
            AgentConnectionState.Disconnected => strings.AgentOffline,
            _ => strings.AgentStarting,
        };
}
