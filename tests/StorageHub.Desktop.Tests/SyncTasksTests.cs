using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Sync tasks, against docs/ui-reference/03-sync-tasks.png and 04-sync-run-history.png.
/// </summary>
/// <remarks>
/// The first screen with sub-tabs of its own, which is the shape these tests are really about: a
/// workspace tab can hold a strip, and the shell renders it from the model rather than growing a
/// second tab control of its own.
/// </remarks>
public class SyncTasksTests
{
    [Fact]
    public void TheTasksPageSaysWhatTheOldOneSaid()
    {
        var model = SyncTasksModel.Create();

        Assert.Equal(Ui.Sync.TasksTitle, model.Headline);
        Assert.Equal(Ui.Sync.NewSyncProfile, model.NewProfileLabel);
        Assert.Equal(
            [Ui.Sync.EnabledTasks, Ui.Sync.DisabledTasks, Ui.Sync.RunsThisSession],
            model.Metrics.Select(metric => metric.Caption));
        Assert.Equal(Ui.Sync.NoTasksConfigured, model.Tasks.Single().Name);
        Assert.Equal(Ui.Sync.NoRunOpened, model.LastSyncs.Single().Name);
    }

    /// <summary>
    /// What is unavailable until a run is loaded is model state, not a look.
    /// </summary>
    /// <remarks>
    /// Next page, Approve &amp; dispatch and Load next operations are all disabled on the reference
    /// screenshot. Dispatching a plan nobody has loaded is the kind of thing that must not become
    /// possible because a view forgot to bind IsEnabled.
    /// </remarks>
    [Fact]
    public void NothingCanBeDispatchedUntilARunIsLoaded()
    {
        var model = SyncRunHistoryModel.Create();

        Assert.False(model.CanApprove);
        Assert.False(model.CanPageForward);
        Assert.False(model.CanLoadMore);
        Assert.Equal(Ui.Sync.NoRunsYet, model.HistoryStatus);
        Assert.Equal(Ui.Sync.NoPlanLoaded, model.PlanEmptyTitle);
    }

    [AvaloniaFact]
    public void TheSyncTabCarriesItsTwoSubTabs()
    {
        var model = ShellPreview.SampleOnSyncTasks;
        var tab = model.Workspaces[1];

        var page = Assert.IsType<TabbedPageModel>(tab.Page);
        Assert.Equal(
            [Ui.Sync.TasksTitle, Ui.Sync.RunHistoryAndReview],
            page.Tabs.Select(sub => sub.Title));
        Assert.IsType<SyncTasksModel>(page.Tabs[0].Content);
        Assert.IsType<SyncRunHistoryModel>(page.Tabs[1].Content);
    }

    [AvaloniaFact]
    public void TheShellRendersTheTasksSubTab()
    {
        var window = new MainWindow { DataContext = ShellPreview.SampleOnSyncTasks };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));

        Assert.Single(window.GetVisualDescendants().OfType<SyncTasksView>());

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(Ui.Sync.SavedTasks, texts);
        Assert.Contains(Ui.Sync.LastSyncs, texts);
    }

    /// <summary>The second sub-tab renders too, which only selecting it proves.</summary>
    [AvaloniaFact]
    public void TheRunHistorySubTabRendersWhenSelected()
    {
        var window = new MainWindow { DataContext = ShellPreview.SampleOnSyncTasks };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));

        var subTabs = window.GetVisualDescendants().OfType<TabControl>()
            .First(control => control.Classes.Contains("subtabs"));
        subTabs.SelectedIndex = 1;

        // A manual Measure/Arrange is not enough: selecting a tab queues the new content's template
        // to be built, so nothing inside it exists until layout actually runs.
        window.UpdateLayout();

        Assert.Single(window.GetVisualDescendants().OfType<SyncRunHistoryView>());

        // The button that must stay unavailable really is.
        var approve = window.GetVisualDescendants().OfType<Button>()
            .Single(button => (button.Content as string) == Ui.Sync.ApproveAndDispatch);
        Assert.False(approve.IsEnabled);
    }
}
