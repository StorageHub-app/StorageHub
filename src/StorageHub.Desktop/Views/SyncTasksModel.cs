using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Lucide.Avalonia;
using StorageHub.Contracts.Ipc;
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
internal sealed class TabbedPageModel(IReadOnlyList<PageTab> tabs) : INotifyPropertyChanged
{
    private int _selectedIndex;

    public IReadOnlyList<PageTab> Tabs { get; } = tabs;

    /// <summary>
    /// Which sub-tab is showing.
    /// </summary>
    /// <remarks>
    /// Settable because one screen sends you to another: previewing a profile produces a run, and
    /// the run belongs on the review tab. Leaving the person to find it themselves is how 1.x's
    /// editor came to embed a second copy of the review control.
    /// </remarks>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value) return;
            _selectedIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndex)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>A row of the saved sync tasks table.</summary>
internal sealed record SyncTaskRow(string Name, string Behavior, string State, string Updated);

/// <summary>A row of the last-syncs table.</summary>
internal sealed record LastSyncRow(string Name, string State, string Updated);

/// <summary>
/// The Tasks sub-tab, against docs/ui-reference/03-sync-tasks.png.
/// </summary>
/// <remarks>
/// <para>
/// This was a record built once from constants: three metrics reading zero, a table holding the
/// single row "No tasks configured", and a footer timestamped whenever the shell happened to start.
/// It described the screen rather than being it, and no amount of saved profiles would have changed
/// a character of it.
/// </para>
/// <para>
/// It now asks the agent. <see cref="SyncTasksController"/> does the asking and has its own suite;
/// what is here is what the answer looks like -- which counts go in the cards, how a profile's
/// behaviour is named, and what a failure leaves on screen.
/// </para>
/// </remarks>
internal sealed class SyncTasksModel : INotifyPropertyChanged
{
    private readonly SyncTasksController? _controller;
    private string _status = Ui.Sync.NoTasksConfigured;
    private bool _isBusy;
    private ICommand? _newProfileCommand;
    private ICommand? _schedulesCommand;
    private ICommand? _runHistoryCommand;

    /// <param name="controller">
    /// How it reaches the agent. Null leaves a screen that shows its empty state and never loads,
    /// which is what a layout test wants.
    /// </param>
    internal SyncTasksModel(SyncTasksController? controller = null)
    {
        _controller = controller;
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(), _ => !IsBusy && _controller is not null);
        ResetMetrics();
    }

    /// <summary>A screen with nothing behind it, for a preview or a layout test.</summary>
    internal static SyncTasksModel Create() => new();

    /// <summary>And one that will ask the agent when it is refreshed.</summary>
    internal static SyncTasksModel Create(Func<ISyncManagementAgentClient> clients) =>
        new(new SyncTasksController(clients));

    public ObservableCollection<MetricCard> Metrics { get; } = [];

    public ObservableCollection<SyncTaskRow> Tasks { get; } = [];

    public ObservableCollection<LastSyncRow> LastSyncs { get; } = [];

    /// <summary>What the foot of the screen says: when it last loaded, or why it did not.</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public ICommand RefreshCommand { get; }

    /// <summary>
    /// What the three buttons across the top do, supplied by the shell.
    /// </summary>
    /// <remarks>
    /// Settable rather than built here, as the connections panel's Manage button is: opening a
    /// window needs an owner, and a page inside a tab has no business knowing what a window is.
    /// Left unset they are simply unavailable, which is what a layout test wants and what the
    /// menu already does for a command with no handler.
    /// </remarks>
    public ICommand? NewProfileCommand
    {
        get => _newProfileCommand;
        set { if (Set(ref _newProfileCommand, value)) { } }
    }

    public ICommand? SchedulesCommand
    {
        get => _schedulesCommand;
        set { if (Set(ref _schedulesCommand, value)) { } }
    }

    public ICommand? RunHistoryCommand
    {
        get => _runHistoryCommand;
        set { if (Set(ref _runHistoryCommand, value)) { } }
    }

    /// <summary>
    /// Loads the profiles and the recent runs.
    /// </summary>
    /// <remarks>
    /// A failure leaves whatever was already listed alone and says why in the status. Clearing the
    /// tables would replace a list somebody was reading with an empty screen that looks exactly
    /// like having no sync profiles at all.
    /// </remarks>
    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_controller is null || IsBusy) return;
        IsBusy = true;
        try
        {
            Status = Ui.Sync.TasksRefreshing;
            var snapshot = await _controller.LoadAsync(cancellationToken).ConfigureAwait(true);
            if (snapshot.Failed && snapshot.Profiles.Count == 0)
            {
                Status = snapshot.ErrorMessage!;
                return;
            }

            Show(snapshot);
            Status = snapshot.Failed
                ? snapshot.ErrorMessage!
                : Ui.Format(Ui.Sync.TasksUpdatedFormat, DateTime.Now, snapshot.Runs.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(SyncTasksSnapshot snapshot)
    {
        Metrics.Clear();
        Metrics.Add(new MetricCard(
            Count(snapshot.EnabledCount), Ui.Sync.EnabledTasks, LucideIconKind.Play, MetricTone.Success));
        Metrics.Add(new MetricCard(
            Count(snapshot.DisabledCount), Ui.Sync.DisabledTasks, LucideIconKind.Pause, MetricTone.Neutral));
        Metrics.Add(new MetricCard(
            Count(snapshot.Runs.Count), Ui.Sync.RunsThisSession, LucideIconKind.ArrowLeftRight,
            MetricTone.Primary));

        Tasks.Clear();
        foreach (var profile in snapshot.Profiles.OrderBy(
            static profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Tasks.Add(new SyncTaskRow(
                profile.DisplayName,
                SyncBehaviorCatalog.DisplayName(profile.Behavior),
                profile.Enabled ? Ui.Sync.Enabled : Ui.Sync.TaskDisabled,
                Moment(profile.UpdatedUtc)));
        }

        if (Tasks.Count == 0)
        {
            Tasks.Add(new SyncTaskRow(
                Ui.Sync.NoTasksConfigured, string.Empty, string.Empty, string.Empty));
        }

        // A run names a profile by id. Resolving it here rather than showing the id is the whole
        // difference between a table somebody can read and one they have to cross-reference.
        var names = snapshot.Profiles.ToDictionary(
            static profile => profile.ProfileId, static profile => profile.DisplayName);

        LastSyncs.Clear();
        foreach (var run in snapshot.Runs.OrderByDescending(static run => run.UpdatedUtc).Take(20))
        {
            LastSyncs.Add(new LastSyncRow(
                names.TryGetValue(run.ProfileId, out var name) ? name : Ui.Sync.UnknownProfile,
                UiEnumNames.Describe(run.Phase),
                Moment(run.UpdatedUtc)));
        }

        if (LastSyncs.Count == 0)
        {
            LastSyncs.Add(new LastSyncRow(Ui.Sync.NoRunOpened, Ui.Sync.UseReviewAndRun, string.Empty));
        }
    }

    /// <summary>The empty screen, which is also what it shows before the first load.</summary>
    private void ResetMetrics()
    {
        Metrics.Clear();
        Metrics.Add(new MetricCard("0", Ui.Sync.EnabledTasks, LucideIconKind.Play, MetricTone.Success));
        Metrics.Add(new MetricCard("0", Ui.Sync.DisabledTasks, LucideIconKind.Pause, MetricTone.Neutral));
        Metrics.Add(new MetricCard(
            "0", Ui.Sync.RunsThisSession, LucideIconKind.ArrowLeftRight, MetricTone.Primary));

        Tasks.Add(new SyncTaskRow(Ui.Sync.NoTasksConfigured, string.Empty, string.Empty, string.Empty));
        LastSyncs.Add(new LastSyncRow(Ui.Sync.NoRunOpened, Ui.Sync.UseReviewAndRun, string.Empty));
    }

    private static string Count(int value) => value.ToString(CultureInfo.CurrentCulture);

    /// <summary>
    /// A timestamp in the reader's own zone.
    /// </summary>
    /// <remarks>
    /// The agent works in UTC and says so on the wire. Showing that unconverted is how a sync that
    /// ran ten minutes ago reads as having run two hours from now.
    /// </remarks>
    private static string Moment(DateTimeOffset value) =>
        value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public static string Headline => Ui.Sync.TasksTitle;

    public static string Subheading => Ui.Sync.TasksAccessibleDescription;

    public static string NewProfileLabel => Ui.Sync.NewSyncProfile;

    public static string SchedulesLabel => Ui.Commands.SyncSchedules;

    public static string RunHistoryLabel => Ui.Sync.RunHistoryAndReview;

    public static string RefreshLabel => Ui.Sync.TasksRefresh;

    public static string SavedTasksTitle => Ui.Sync.SavedTasks;

    public static string LastSyncsTitle => Ui.Sync.LastSyncs;

    public static string ColumnName => Ui.Sync.ColumnName;

    public static string ColumnBehavior => Ui.Sync.ColumnBehavior;

    public static string ColumnState => Ui.Overview.ColumnState;

    public static string ColumnUpdated => Ui.Sync.ColumnUpdated;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
