using System.Globalization;
using Lucide.Avalonia;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>One sub-tab of a workspace page.</summary>
internal sealed record PageTab(string Title, object Content);

/// <summary>
/// A workspace page that is itself tabbed.
/// </summary>
/// <remarks>
/// Sync tasks splits into Tasks and Run history and review, so a tab in the workspace strip can hold
/// a strip of its own. Modelled rather than nested by hand, so any later screen with sub-tabs -
/// settings sections, a connection editor - reuses the same shape instead of growing another
/// TabControl in the shell.
/// </remarks>
internal sealed record TabbedPageModel(IReadOnlyList<PageTab> Tabs);

/// <summary>A row of the saved sync tasks table.</summary>
internal sealed record SyncTaskRow(string Name, string Behavior, string State, string Updated);

/// <summary>A row of the last-syncs table.</summary>
internal sealed record LastSyncRow(string Name, string State, string Updated);

/// <summary>
/// The Tasks sub-tab, against docs/ui-reference/03-sync-tasks.png.
/// </summary>
internal sealed record SyncTasksModel(
    string Headline,
    string Subheading,
    string NewProfileLabel,
    string SchedulesLabel,
    string RunHistoryLabel,
    string RefreshLabel,
    IReadOnlyList<MetricCard> Metrics,
    string SavedTasksTitle,
    IReadOnlyList<SyncTaskRow> Tasks,
    string LastSyncsTitle,
    IReadOnlyList<LastSyncRow> LastSyncs,
    string Footer,
    string ColumnName,
    string ColumnBehavior,
    string ColumnState,
    string ColumnUpdated)
{
    internal static SyncTasksModel Create()
    {
        var strings = Ui.Sync;

        return new(
            strings.TasksTitle,
            strings.TasksAccessibleDescription,
            strings.NewSyncProfile,
            Ui.Commands.SyncSchedules,
            strings.RunHistoryAndReview,
            strings.TasksRefresh,
            [
                new("0", strings.EnabledTasks, LucideIconKind.Play, MetricTone.Success),
                new("0", strings.DisabledTasks, LucideIconKind.Pause, MetricTone.Neutral),
                new("0", strings.RunsThisSession, LucideIconKind.ArrowLeftRight, MetricTone.Primary),
            ],
            strings.SavedTasks,
            [new(strings.NoTasksConfigured, string.Empty, string.Empty, string.Empty)],
            strings.LastSyncs,
            [new(strings.NoRunOpened, strings.UseReviewAndRun, string.Empty)],
            Ui.Format(strings.TasksUpdatedFormat, DateTime.Now, 0),
            strings.ColumnName,
            strings.ColumnBehavior,
            Ui.Overview.ColumnState,
            strings.ColumnUpdated);
    }
}

/// <summary>A row of the run history table.</summary>
internal sealed record SyncRunRow(string Updated, string Run, string Phase, string Dispatch, string Conflicts);

/// <summary>A row of the plan table.</summary>
internal sealed record PlanRow(
    string Index,
    string Action,
    string From,
    string To,
    string ExpectedBytes,
    string Approval);

/// <summary>
/// The Run history and review sub-tab, against docs/ui-reference/04-sync-run-history.png.
/// </summary>
/// <remarks>
/// The densest screen in the app. Note what is disabled: Next page, Approve &amp; dispatch and Load
/// next operations are unavailable until a run is loaded, and that is state this model carries
/// rather than decoration the view invents.
/// </remarks>
internal sealed record SyncRunHistoryModel(
    string RunIdLabel,
    string RunIdPlaceholder,
    string LoadRunLabel,
    string RefreshHistoryLabel,
    string NextPageLabel,
    bool CanPageForward,
    string HistoryStatus,
    IReadOnlyList<SyncRunRow> Runs,
    string PlanEmptyTitle,
    string PlanEmptyHint,
    string RefreshStatusLabel,
    string ApproveLabel,
    bool CanApprove,
    string PlanTabLabel,
    string ConflictsTabLabel,
    IReadOnlyList<PlanRow> Plan,
    string LoadMoreLabel,
    bool CanLoadMore,
    string ColumnUpdated,
    string ColumnRun,
    string ColumnPhase,
    string ColumnDispatch,
    string ColumnConflicts)
{
    internal static SyncRunHistoryModel Create()
    {
        var strings = Ui.Sync;

        return new(
            strings.RunIdLabel,
            Guid.Empty.ToString("D", CultureInfo.InvariantCulture),
            strings.LoadRun,
            strings.RefreshHistory,
            strings.NextPage,
            CanPageForward: false,
            strings.NoRunsYet,
            [],
            strings.NoPlanLoaded,
            strings.ChooseReviewAndRun,
            strings.RefreshStatus,
            strings.ApproveAndDispatch,
            CanApprove: false,
            strings.PlanTab,
            strings.ConflictsTab,
            [],
            strings.LoadNextOperations,
            CanLoadMore: false,
            strings.ColumnUpdated,
            strings.ColumnRun,
            strings.ColumnPhase,
            strings.ColumnDispatch,
            Ui.Transfer.TabConflicts);
    }
}
