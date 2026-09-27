using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Threading;
using Lucide.Avalonia;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;

namespace StorageHub.Desktop.Views;

/// <summary>One transfer, as the queue's table shows it.</summary>
/// <remarks>
/// One object per transfer for as long as the transfer is on the tab, updated in place by each
/// poll. It was a record rebuilt on every poll, and a table selects objects: every 0.5 to 2 seconds
/// the selection went with the old ones, so a row could not stay picked long enough to act on it.
/// </remarks>
internal sealed class TransferRow : INotifyPropertyChanged
{
    private long _revision;
    private TransferQueueState _state;
    private DateTimeOffset _updated;
    private string _operation = string.Empty;
    private string _source = string.Empty;
    private string _destination = string.Empty;
    private string _progress = string.Empty;
    private string _attempt = string.Empty;
    private string _status = string.Empty;
    private bool _canCancel;
    private bool _canRetry;
    private bool _needsReconciliation;
    private double? _progressFraction;

    /// <param name="connectionName">
    /// A saved connection's name by its id, for Source and Destination; see
    /// <see cref="TransferQueueModel.ConnectionName"/>.
    /// </param>
    internal TransferRow(TransferQueueSummary transfer, Func<Guid, string?>? connectionName = null)
    {
        Id = transfer.TransferId;
        Update(transfer, connectionName);
    }

    public Guid Id { get; }

    /// <summary>
    /// What the agent last reported. Cancel and retry send it back, so a request built from a row
    /// the queue has since moved on from is refused rather than applied to a different state.
    /// </summary>
    public long Revision => _revision;

    /// <summary>Which of the queue's states it is in, which decides what can be cleared.</summary>
    public TransferQueueState State => _state;

    /// <summary>When it last changed, which is the order several rows are acted on in.</summary>
    public DateTimeOffset Updated => _updated;

    public string Operation => _operation;

    public string Source => _source;

    public string Destination => _destination;

    public string Progress => _progress;

    public string Attempt => _attempt;

    public string Status => _status;

    public bool CanCancel => _canCancel;

    public bool CanRetry => _canRetry;

    public bool NeedsReconciliation => _needsReconciliation;

    /// <summary>
    /// How much of the transfer is done, from 0 to 1, or null when its size is not known and it has
    /// not finished -- which is when the column shows bytes moved and no bar, rather than a bar
    /// stuck at nothing.
    /// </summary>
    public double? ProgressFraction => _progressFraction;

    /// <summary>Whether there is a bar to draw behind the text.</summary>
    public bool HasProgressBar => ProgressFraction is not null;

    /// <summary>The bar's value, in percent.</summary>
    public double ProgressPercent => (ProgressFraction ?? 0) * 100;

    /// <summary>A finished bar is drawn in the success colour, as 1.x drew it.</summary>
    public bool IsComplete => ProgressFraction >= 1;

    /// <summary>A finished transfer, which is what history is and all that can be cleared.</summary>
    public bool IsHistory => _state is
        TransferQueueState.Completed or TransferQueueState.Cancelled or TransferQueueState.Failed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Takes what the agent now says about this transfer, telling the table only what changed.
    /// </summary>
    /// <remarks>
    /// The paths are shown whole rather than shortened. The old shell elided the middle to fit a
    /// fixed column; the columns here are resizable, so eliding would throw away what the person
    /// widened the column to read. The connection's name is asked for on every poll, so a
    /// connection renamed, or one the sidebar had not read yet, is named on the next one.
    /// </remarks>
    internal void Update(TransferQueueSummary transfer, Func<Guid, string?>? connectionName = null)
    {
        Set(ref _revision, transfer.Revision, nameof(Revision));
        Set(ref _state, transfer.State, nameof(State));
        Set(ref _updated, transfer.UpdatedUtc, nameof(Updated));
        Set(ref _operation, UiEnumNames.Describe(transfer.Operation), nameof(Operation));
        Set(
            ref _source,
            TransferQueueModel.DescribeEndpoint(transfer.SourceConnectionId, transfer.SourcePath, connectionName),
            nameof(Source));
        Set(
            ref _destination,
            TransferQueueModel.DescribeEndpoint(transfer.DestinationConnectionId, transfer.DestinationPath, connectionName),
            nameof(Destination));
        Set(ref _progress, TransferQueueModel.DescribeProgress(transfer), nameof(Progress));
        Set(ref _attempt, transfer.Attempt.ToString(CultureInfo.CurrentCulture), nameof(Attempt));
        Set(ref _status, TransferQueueModel.DescribeStatus(transfer), nameof(Status));
        Set(ref _canCancel, transfer.CanCancel, nameof(CanCancel));
        Set(ref _canRetry, transfer.CanRetry, nameof(CanRetry));
        Set(ref _needsReconciliation, transfer.NeedsReconciliation, nameof(NeedsReconciliation));
        if (Set(ref _progressFraction, TransferQueueModel.ProgressFractionOf(transfer), nameof(ProgressFraction)))
        {
            Raise(nameof(HasProgressBar));
            Raise(nameof(ProgressPercent));
            Raise(nameof(IsComplete));
        }
    }

    private bool Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One tab of the queue, with the count the agent reported for it.</summary>
internal sealed class TransferQueueTab(TransferQueueTabDefinition definition) : INotifyPropertyChanged
{
    private int _count;

    public string Key => definition.Key;

    public LucideIconKind Icon =>
        Themes.IconCatalog.Resolve(definition.Glyph) ?? LucideIconKind.ListOrdered;

    /// <summary>"Active (3)", as the old shell wrote it.</summary>
    public string Title => string.Create(
        CultureInfo.CurrentCulture,
        $"{definition.Label()} ({_count})");

    internal int Count
    {
        get => _count;
        set
        {
            if (_count == value) return;
            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    internal IReadOnlyList<TransferQueueState> States => definition.States;

    /// <summary>Whether this tab filters the queue. Every tab but Logs does.</summary>
    public bool IsStateFilter => definition.States.Count > 0;

    /// <summary>Whether this tab shows the activity log instead.</summary>
    public bool IsLog => !IsStateFilter;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// The transfer queue, on whatever the agent actually holds.
/// </summary>
/// <remarks>
/// <para>
/// Polls rather than subscribes, because that is what the queue IPC offers: a list request with a
/// state filter. The interval follows the work -- twice a second while something is transferring,
/// every two seconds otherwise -- which is the old shell's rule and the difference between a
/// progress column that moves and a desktop that wakes the agent all day for nothing.
/// </para>
/// <para>
/// Every failure is swallowed into a message rather than thrown. The agent can be starting, or
/// stopped, or mid-restart after an update, and a queue that throws in that window takes the shell
/// with it. The row area says what happened instead.
/// </para>
/// <para>
/// The rows are kept rather than rebuilt. Each poll updates the transfers already shown, by id,
/// adds the new ones and drops the ones that have left the tab, so what somebody selected and
/// where they had scrolled to survive it. Several rows can be selected, as in 1.x's grid, and
/// Cancel, Retry, Apply and the right-click menu act on all of them.
/// </para>
/// </remarks>
internal sealed class TransferQueueModel : INotifyPropertyChanged, IAsyncDisposable
{
    private const int ActivePollMilliseconds = 500;
    private const int IdlePollMilliseconds = 2_000;

    /// <summary>How many rows a tab shows at once: four pages.</summary>
    private const int MaximumRows = TransferQueueIpcLimits.MaximumPageSize * 4;

    /// <summary>
    /// How long what an action did stays on the toolbar: long enough to be read, and then it gives
    /// way to the count 1.x wrote there after every read.
    /// </summary>
    private static readonly TimeSpan NoticeLasts = TimeSpan.FromSeconds(5);

    /// <summary>The contract's reconciliation actions, in its order, which is the drop-down's.</summary>
    private static readonly TransferReconciliationAction[] ReconcileChoices =
        Enum.GetValues<TransferReconciliationAction>();

    private readonly Func<ITransferQueueAgentClient> _connect;
    private readonly IDialogService? _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private ITransferQueueAgentClient? _client;
    private DispatcherTimer? _timer;
    private string _message = string.Empty;
    private int _selectedTab;
    private int _reconcileAction;
    private bool _busy;

    /// <summary>Whether a row waiting on a decision was among the chosen ones when last looked.</summary>
    private bool _couldReconcile;

    /// <summary>
    /// Whether a poll is putting the rows in line, which lets go of chosen rows and chooses them
    /// again on its own; the commands are told once it is done rather than at every step.
    /// </summary>
    private bool _updating;

    /// <summary>
    /// Whether a failed read of the queue has been written to the error log since the last one
    /// that worked. An agent that keeps refusing is refused every poll, and a log entry per poll
    /// would push out the ones the log is there to keep.
    /// </summary>
    private bool _readFailureLogged;

    /// <summary>A refresh asked for while another was in flight, to be run when it is done.</summary>
    private bool _again;

    /// <summary>
    /// Moves on whenever the tab or the page changes, so a read that was already under way for
    /// the old one is not shown on the new one.
    /// </summary>
    private int _view;

    /// <summary>Where the rows shown begin: null for the newest, or where Next moved them on to.</summary>
    private string? _pageStart;

    /// <summary>Where Next would begin, or null when there is nothing after these rows.</summary>
    private string? _nextPage;

    /// <summary>
    /// What the last action did, such as "Cleared 3 history record(s).", kept on the toolbar
    /// through the polls for <see cref="NoticeLasts"/>, after which the count comes back.
    /// </summary>
    private string? _notice;

    /// <summary>When the notice was said, as a <see cref="TimeProvider"/> timestamp.</summary>
    private long _noticeSaid;

    /// <summary>
    /// What was chosen on each tab when somebody left it, by transfer id, so coming back finds it
    /// still chosen, as each of 1.x's grids kept its own.
    /// </summary>
    private readonly Dictionary<string, Guid[]> _chosenOnTab = new(StringComparer.Ordinal);

    /// <summary>What the first read after a tab change chooses again, once its rows are there.</summary>
    private Guid[]? _restore;

    /// <param name="activity">
    /// What the Logs tab shows. Optional so a test of the queue itself need not stand up a sync
    /// client; without it the tab says the log is not loaded, as it did before the log was ported.
    /// </param>
    /// <param name="dialogs">
    /// How Clear all history asks first. Without it that entry clears nothing, since there is
    /// nobody it can ask.
    /// </param>
    internal TransferQueueModel(
        Func<ITransferQueueAgentClient> connect,
        ActivityLogModel? activity = null,
        IDialogService? dialogs = null)
    {
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
        _dialogs = dialogs;
        Activity = activity;
        Tabs = [.. TransferQueueTabs.All.Select(definition => new TransferQueueTab(definition))];

        // The buttons follow the selection the moment it changes. They were only told on the next
        // poll, so a Cancel lit up a second or two after its row was picked, when it did at all.
        // Not while a poll moves the rows, though: the table lets go of a chosen row that moves
        // and it is chosen again straight after, which looked like a conflict newly chosen and put
        // the drop-down back on Restart. The poll tells them once, when it is done.
        SelectedRows.CollectionChanged += (_, _) =>
        {
            if (!_updating) RaiseCommands();
        };

        RefreshCommand = new RelayCommand(_ => _ = RefreshFromStartAsync());
        CancelCommand = new RelayCommand(
            _ => _ = MutateAsync(Mutation.Cancel),
            _ => SelectedRows.Any(static row => row.CanCancel));
        RetryCommand = new RelayCommand(
            _ => _ = MutateAsync(Mutation.Retry),
            _ => SelectedRows.Any(static row => row.CanRetry));
        ApplyReconcileCommand = new RelayCommand(
            _ => _ = MutateAsync(Mutation.Reconcile),
            _ => CanReconcile);
        NextPageCommand = new RelayCommand(_ => ShowNextPage(), _ => _nextPage is not null);
        ClearSelectedCommand = new RelayCommand(
            _ => _ = ClearAsync([.. SelectedRows.Where(static row => row.IsHistory).Select(static row => row.Id)]),
            _ => SelectedRows.Any(static row => row.IsHistory));
        CancelAndClearCommand = new RelayCommand(
            _ => _ = CancelAndClearAsync(),
            _ => SelectedRows.Any(CanCancelAndClear));
        ClearAllHistoryCommand = new RelayCommand(_ => _ = ClearAllHistoryAsync());
    }

    public ObservableCollection<TransferQueueTab> Tabs { get; }

    public ObservableCollection<TransferRow> Rows { get; } = [];

    /// <summary>The rows chosen in the table, which every command acts on.</summary>
    public ObservableCollection<TransferRow> SelectedRows { get; } = [];

    /// <summary>The first chosen row; setting it makes that row the whole selection.</summary>
    public TransferRow? Selected
    {
        get => SelectedRows.Count > 0 ? SelectedRows[0] : null;
        set
        {
            SelectedRows.Clear();
            if (value is not null) SelectedRows.Add(value);
        }
    }

    /// <summary>The Logs tab's content, or null when this queue was made without one.</summary>
    public ActivityLogModel? Activity { get; }

    /// <summary>
    /// Whether Clear all history asks first: the "Warn before clearing all transfer history"
    /// setting. Null asks every time.
    /// </summary>
    internal Func<bool>? ClearAllConfirmation { get; set; }

    /// <summary>Turns that setting off, for the warning's "Don't show this warning again".</summary>
    internal Action? StopClearAllConfirmation { get; set; }

    /// <summary>
    /// A saved connection's name by its id, which Source and Destination are written with. Null,
    /// or an id it does not know, leaves the id's first eight characters there, as 1.x wrote it.
    /// </summary>
    internal Func<Guid, string?>? ConnectionName { get; set; }

    public static string ColumnOperation => Ui.Transfer.ColumnOperation;

    public static string ColumnSource => Ui.Transfer.ColumnSource;

    public static string ColumnDestination => Ui.Transfer.ColumnDestination;

    public static string ColumnProgress => Ui.Transfer.ColumnProgress;

    public static string ColumnAttempt => Ui.Transfer.ColumnAttempt;

    public static string ColumnStatus => Ui.Transfer.ColumnStatus;

    public static string RefreshLabel => Ui.Transfer.Refresh;

    public static string RefreshHint => Ui.Transfer.RefreshQueue;

    public static string CancelLabel => Ui.Transfer.Cancel;

    public static string CancelHint => Ui.Transfer.CancelSelected;

    public static string RetryLabel => Ui.Transfer.Retry;

    public static string RetryHint => Ui.Transfer.RetrySelected;

    public static string ReconcileLabel => Ui.Transfer.ReconcileLabel;

    public static string ApplyLabel => Ui.Transfer.Apply;

    public static string ApplyHint => Ui.Transfer.ApplyReconciliation;

    public static string NextLabel => Ui.Transfer.Next;

    public static string NextHint => Ui.Transfer.NextPage;

    public static string ClearSelectedLabel => Ui.Transfer.ClearSelectedHistory;

    public static string CancelAndClearLabel => Ui.Transfer.CancelAndClearSelected;

    public static string ClearAllHistoryLabel => Ui.Transfer.ClearAllHistory;

    public static string HistoryCommandsLabel => Ui.Transfer.HistoryCommands;

    public static string ReconcileActionName => Ui.Transfer.ReconciliationAction;

    /// <summary>
    /// What to do with a transfer the agent cannot decide about on its own: all five of the
    /// contract's, in its order, as 1.x offered them.
    /// </summary>
    /// <remarks>
    /// This offered three, leaving out Mark failed and Cancel as too destructive to sit in a
    /// drop-down. Nothing else marks a transfer failed, though, and 1.x offered both.
    /// </remarks>
    public IReadOnlyList<string> ReconcileActions { get; } =
        [.. ReconcileChoices.Select(static action => UiEnumNames.Describe(action))];

    /// <summary>Which of <see cref="ReconcileActions"/> Apply sends.</summary>
    public int SelectedReconcileAction
    {
        get => _reconcileAction;
        set
        {
            if (_reconcileAction == value) return;
            _reconcileAction = value;
            Raise(nameof(SelectedReconcileAction));
        }
    }

    /// <summary>
    /// Whether a chosen row waits on a decision, which is when the drop-down and Apply mean
    /// anything; both are dimmed otherwise, as 1.x's were.
    /// </summary>
    public bool CanReconcile => SelectedRows.Any(static row => row.NeedsReconciliation);

    /// <summary>The action the drop-down is on.</summary>
    private TransferReconciliationAction ReconcileAction
    {
        get => ReconcileChoices[Math.Clamp(SelectedReconcileAction, 0, ReconcileChoices.Length - 1)];
        set => SelectedReconcileAction = Array.IndexOf(ReconcileChoices, value);
    }

    public ICommand ApplyReconcileCommand { get; }

    /// <summary>
    /// The rows after these, as 1.x's Next showed them: the page moves on, and Refresh comes back
    /// to the newest.
    /// </summary>
    public ICommand NextPageCommand { get; }

    /// <summary>Clears the finished transfers among the selected rows.</summary>
    public ICommand ClearSelectedCommand { get; }

    /// <summary>Cancels the unfinished transfers among the selected rows, then clears them.</summary>
    public ICommand CancelAndClearCommand { get; }

    /// <summary>Clears every finished transfer, on every tab.</summary>
    public ICommand ClearAllHistoryCommand { get; }

    /// <summary>
    /// What the toolbar says: how many transfers are shown, what was just done, or, when there are
    /// no rows, that the tab is empty or why nothing could be read.
    /// </summary>
    public string Message
    {
        get => _message;
        private set
        {
            if (string.Equals(_message, value, StringComparison.Ordinal)) return;
            _message = value;
            Raise(nameof(Message));
            Raise(nameof(HasMessage));
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(_message) && Rows.Count == 0;

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab == value) return;

            // Each tab keeps what was chosen on it, as each of 1.x's grids did, by transfer id
            // rather than by row, since the rows will have moved by the time somebody is back.
            // One left before its first read still holds what it was about to choose again.
            _chosenOnTab[TabAt(_selectedTab).Key] = _restore ?? [.. SelectedRows.Select(static row => row.Id)];
            _selectedTab = value;
            _restore = _chosenOnTab.GetValueOrDefault(TabAt(value).Key);
            Raise(nameof(SelectedTab));

            // A tab starts at its newest transfers, without the last tab's news. The rows shown
            // until its read lands are the last tab's, so nothing is chosen among them.
            _view++;
            _pageStart = null;
            _nextPage = null;
            _notice = null;
            SelectedRows.Clear();
            RaiseCommands();
            _ = RefreshAsync();
        }
    }

    public ICommand RefreshCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand RetryCommand { get; }

    /// <summary>Transfers the agent reports as running, for the status bar.</summary>
    internal int ActiveCount => Tabs[0].Count;

    /// <summary>And those waiting behind them, which is the other half of "how busy is it".</summary>
    internal int QueuedCount => Tabs[1].Count;

    /// <summary>How fast everything running moves together, for the status bar; 0 when nothing does.</summary>
    internal long BytesPerSecond { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Starts polling.
    /// </summary>
    /// <remarks>
    /// Opt-in, like the agent status monitor, so a headless test measures a queue that is not
    /// talking to a socket unless it asked to.
    /// </remarks>
    internal void Start()
    {
        if (_timer is not null) return;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IdlePollMilliseconds) };
        _timer.Tick += (_, _) => _ = RefreshAsync(background: true);
        _timer.Start();
        _ = RefreshAsync();
    }

    /// <summary>Reads the selected tab's transfers and the counts for all of them.</summary>
    /// <param name="background">
    /// A timer tick rather than a press or a tab change. The Logs tab polls less often than the
    /// queue and does so quietly; and a tick that finds a read already under way is simply
    /// skipped, where a press waits for it and then runs.
    /// </param>
    internal async Task RefreshAsync(bool background = false)
    {
        if (_lifetime.IsCancellationRequested) return;

        // One request in flight. The client is strictly correlated, and a second poll landing on
        // top of a slow one is how a response gets attributed to the wrong tab. A press or a tab
        // change meanwhile is not dropped, though, or the new tab would show the old one's rows
        // until the next tick: it runs as soon as the read in flight is done.
        if (_busy)
        {
            if (!background) _again = true;
            return;
        }

        _busy = true;
        try
        {
            do
            {
                _again = false;
                await ReadAsync(background).ConfigureAwait(true);
                background = false;
            }
            while (_again && !_lifetime.IsCancellationRequested);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Reads the newest transfers again, leaving wherever Next had gone to: Refresh, and a
    /// transfer just queued, which is at the top of the first page and nowhere on the others.
    /// </summary>
    /// <remarks>As 1.x's Refresh and its refresh after an enqueue both went back to page one.</remarks>
    internal Task RefreshFromStartAsync()
    {
        _notice = null;
        ShowFromStart();
        return RefreshAsync();
    }

    private TransferQueueTab TabAt(int index) => Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];

    private async Task ReadAsync(bool background)
    {
        var tab = TabAt(_selectedTab);
        if (tab.IsLog)
        {
            // Logs. It asks for every state and for sync runs besides, which is a different
            // request from the queue's, and a list request with no states would be refused by
            // the contract anyway. The queue's own rows and message are left alone.
            if (Activity is null)
            {
                Message = Ui.Transfer.ActivityNotLoaded;
                return;
            }

            // The toolbar's message is about the queue, and nothing reads the queue on this
            // tab, so whatever it last said can only be stale here: an agent that came back
            // while somebody sat on Logs would go on being reported as unavailable beside a log
            // that reads fine. The log's own status line says how the log is.
            Message = string.Empty;
            await Activity.RefreshAsync(background, _lifetime.Token).ConfigureAwait(true);
            tab.Count = Activity.Rows.Count;
            return;
        }

        var view = _view;
        var response = await ListAsync(tab.States).ConfigureAwait(true);

        // Read for a tab or a page that is no longer the one showing. The change that moved it on
        // has asked for a read of its own, which runs next.
        if (response is null || view != _view) return;

        Apply(response, tab);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _timer?.Stop();
        _timer = null;
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }

        _lifetime.Dispose();
    }

    private async Task<TransferListResponse?> ListAsync(IReadOnlyList<TransferQueueState> states)
    {
        try
        {
            _client ??= _connect();

            // A page at a time, as the contract allows, up to MaximumRows from where the rows
            // shown begin. This asked for 100 in one page against a limit of 50, which the client
            // refuses before sending, so the queue never listed anything, and the refusal was
            // thrown into a fire-and-forget.
            var response = await _client.ListAsync(
                new TransferListRequest(
                    TransferQueueIpcContract.CurrentVersion,
                    [.. states],
                    PageSize: TransferQueueIpcLimits.MaximumPageSize,
                    ContinuationToken: _pageStart),
                _lifetime.Token).ConfigureAwait(true);
            var rows = new List<TransferQueueSummary>(response.Transfers);
            while (response.Failure is null &&
                   response.ContinuationToken is { } next &&
                   rows.Count < MaximumRows)
            {
                response = await _client.ListAsync(
                    new TransferListRequest(
                        TransferQueueIpcContract.CurrentVersion,
                        [.. states],
                        PageSize: TransferQueueIpcLimits.MaximumPageSize,
                        ContinuationToken: next),
                    _lifetime.Token).ConfigureAwait(true);
                rows.AddRange(response.Transfers);
            }

            _readFailureLogged = false;
            return response with { Transfers = [.. rows] };
        }
        catch (Exception error) when (IsRefusal(error))
        {
            // A request the client would not send is a defect here rather than a state of the
            // agent, and so is one the agent refused or answered with something unreadable.
            // Said, logged once, and survived -- not thrown out of a timer callback.
            if (!_readFailureLogged) Framework.DesktopErrorLog.Write("queue", error);
            _readFailureLogged = true;
            Unavailable();
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception error) when (IsUnavailable(error))
        {
            // The agent is starting, stopped, or mid-restart. Drop the client so the next poll
            // reconnects rather than reusing a broken one.
            await DropClientAsync().ConfigureAwait(true);
            Unavailable();
            Rows.Clear();
            SelectedRows.Clear();
            Raise(nameof(HasMessage));
            return null;
        }
    }

    private static bool IsUnavailable(Exception error) =>
        error is IOException or TimeoutException or InvalidOperationException or
            UnauthorizedAccessException or ObjectDisposedException;

    /// <summary>
    /// The agent answered, but with a refusal rather than an outcome: a request it would not take,
    /// or a reply that could not be read. Or the client would not send the request at all.
    /// </summary>
    private static bool IsRefusal(Exception error) =>
        error is InvalidDataException or System.Text.Json.JsonException or ArgumentException;

    /// <summary>Says why an action came to nothing, rather than letting it go unheard.</summary>
    /// <remarks>
    /// Only an agent that could not be reached used to be caught. A refusal went out of the
    /// fire-and-forget that ran the action, and the toolbar said "Applying queue action..." for a
    /// few seconds and then the count, as if the action had worked. 1.x said something for every
    /// failure. The agent's own words are not shown: they are English, and written for the log.
    /// </remarks>
    private async Task FailedAsync(Exception error)
    {
        if (IsUnavailable(error))
        {
            // Drop the client so the next request reconnects rather than reusing a broken one.
            await DropClientAsync().ConfigureAwait(true);
            Unavailable();
            return;
        }

        Framework.DesktopErrorLog.Write("queue", error);
        Say(Ui.Shell.AgentRequestFailed);
    }

    private async Task DropClientAsync()
    {
        if (_client is null) return;
        try
        {
            await _client.DisposeAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // Disposing a connection that is already broken is not news.
        }

        _client = null;
    }

    private void Apply(TransferListResponse response, TransferQueueTab tab)
    {
        _updating = true;
        try
        {
            Update(response.Transfers);
        }
        finally
        {
            _updating = false;
        }

        if (response.StateCounts is { } counts)
        {
            foreach (var each in Tabs)
            {
                each.Count = each.States.Sum(state => counts.TryGetValue(state, out var value) ? value : 0);
            }
        }

        // A failure part-way through still shows the pages that were read; it is only said when
        // there is nothing to show, and then it outranks what was last done. Otherwise what was
        // last done is said for a few seconds, and then how many transfers are shown, as 1.x
        // said after every read; which is also what tells somebody Next has moved them on.
        _nextPage = response.Failure is null ? response.ContinuationToken : null;
        if (response.Failure is not null ||
            (_notice is not null && TimeProvider.System.GetElapsedTime(_noticeSaid) > NoticeLasts))
        {
            _notice = null;
        }

        Message = Rows.Count > 0
            ? _notice ?? Ui.Format(Ui.Transfer.TransferCountFormat, Rows.Count)
            : response.Failure?.Message ?? _notice ?? EmptyMessage(tab);

        BytesPerSecond = response.TotalBytesPerSecond ?? 0;

        Raise(nameof(HasMessage));
        Raise(nameof(ActiveCount));
        Raise(nameof(QueuedCount));
        Raise(nameof(BytesPerSecond));
        Interval = Tabs[0].Count > 0 ? ActivePollMilliseconds : IdlePollMilliseconds;
        RaiseCommands();
    }

    /// <summary>What an empty tab says: 1.x's Conflicts tab had words of its own.</summary>
    private static string EmptyMessage(TransferQueueTab tab) =>
        string.Equals(tab.Key, TransferQueueTabs.ConflictsKey, StringComparison.Ordinal)
            ? Ui.Transfer.NoReconciliationNeeded
            : Ui.Transfer.NoTransfers;

    /// <summary>
    /// Brings the rows in line with what the agent listed, in its order, keeping every row that
    /// is still there.
    /// </summary>
    /// <remarks>
    /// Matched by transfer id: updated in place, added where they appear, removed when they have
    /// left the tab. The agent lists the most recently changed first, so a running transfer moves
    /// up on most polls, and a table lets go of a selected row that moves; the ones that were
    /// selected are selected again afterwards. So are the ones chosen on this tab before somebody
    /// last left it, on the first read after they come back.
    /// </remarks>
    private void Update(IReadOnlyList<TransferQueueSummary> transfers)
    {
        var listed = transfers.DistinctBy(static transfer => transfer.TransferId).ToArray();
        var ids = listed.Select(static transfer => transfer.TransferId).ToHashSet();
        var chosen = SelectedRows.Select(static row => row.Id).ToHashSet();
        if (_restore is { } restore) chosen.UnionWith(restore);
        _restore = null;

        for (var index = Rows.Count - 1; index >= 0; index--)
        {
            if (!ids.Contains(Rows[index].Id)) Rows.RemoveAt(index);
        }

        var shown = Rows.ToDictionary(static row => row.Id);
        for (var index = 0; index < listed.Length; index++)
        {
            var transfer = listed[index];
            if (shown.TryGetValue(transfer.TransferId, out var row))
            {
                var at = Rows.IndexOf(row);
                if (at != index) Rows.Move(at, index);
                row.Update(transfer, ConnectionName);
            }
            else
            {
                Rows.Insert(index, new TransferRow(transfer, ConnectionName));
            }
        }

        // Without a table there is nobody to drop the rows that went from the selection.
        for (var index = SelectedRows.Count - 1; index >= 0; index--)
        {
            if (!ids.Contains(SelectedRows[index].Id)) SelectedRows.RemoveAt(index);
        }

        foreach (var row in Rows)
        {
            if (chosen.Contains(row.Id) && !SelectedRows.Contains(row)) SelectedRows.Add(row);
        }
    }

    private void RaiseCommands()
    {
        // Restart, when a transfer waiting on a decision comes to be chosen and the drop-down is
        // still on Review, as 1.x moved it. Review on an interrupted transfer only moves it on to
        // needing reconciliation, which leaves it on the same tab, still a conflict: applying the
        // default reported success and changed nothing anybody could see. 1.x moved it back on
        // every poll as well, so a Review chosen on purpose lasted two seconds; here it stays.
        var canReconcile = CanReconcile;
        if (canReconcile && !_couldReconcile && ReconcileAction == TransferReconciliationAction.Review)
        {
            ReconcileAction = TransferReconciliationAction.Restart;
        }

        _couldReconcile = canReconcile;
        Raise(nameof(CanReconcile));

        (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RetryCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ApplyReconcileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelAndClearCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private int Interval
    {
        set
        {
            if (_timer is null || (int)_timer.Interval.TotalMilliseconds == value) return;
            _timer.Interval = TimeSpan.FromMilliseconds(value);
        }
    }

    /// <summary>Says what an action did, and keeps saying it through the next few seconds of polls.</summary>
    private void Say(string notice)
    {
        _notice = notice;
        _noticeSaid = TimeProvider.System.GetTimestamp();
        Message = notice;
    }

    /// <summary>The agent could not be reached; there is no next page to go to, either.</summary>
    private void Unavailable()
    {
        _notice = null;
        _nextPage = null;
        Message = Ui.Transfer.QueueUnavailable;
        RaiseCommands();
    }

    /// <summary>Back to the newest rows, after an action, as 1.x went back to its first page.</summary>
    private void ShowFromStart()
    {
        if (_pageStart is null) return;
        _pageStart = null;
        _view++;
    }

    private void ShowNextPage()
    {
        if (_nextPage is not { } next) return;
        _pageStart = next;
        _nextPage = null;
        _notice = null;
        _view++;
        RaiseCommands();
        _ = RefreshAsync();
    }

    private enum Mutation
    {
        Cancel,
        Retry,
        Reconcile
    }

    /// <summary>
    /// Cancels, retries or reconciles every selected row the action applies to, and says how many
    /// the agent took.
    /// </summary>
    /// <remarks>
    /// Oldest first, as 1.x sent them, so several retries go back into the queue in the order they
    /// first failed. Each is revision-checked: the agent refuses a decision made about a state the
    /// transfer has already left, which matters most for reconciling, the one action that can
    /// mark an incomplete transfer complete. Rows the action does not apply to are not sent;
    /// 1.x sent them and counted the refusals as conflicts, which asked somebody to review a
    /// finished transfer that needed nothing.
    /// </remarks>
    private async Task MutateAsync(Mutation mutation)
    {
        // The revisions as they were when the button was pressed. The rows are updated in place
        // and the polls go on while the requests go out one by one, so a revision read after the
        // first request could be one the person never saw, and a decision about a state the
        // transfer has since left would be applied rather than refused.
        var targets = SelectedRows
            .Where(row => mutation switch
            {
                Mutation.Cancel => row.CanCancel,
                Mutation.Retry => row.CanRetry,
                _ => row.NeedsReconciliation
            })
            .OrderBy(static row => row.Updated)
            .Select(static row => (row.Id, row.Revision))
            .ToArray();
        if (targets.Length == 0) return;

        var action = ReconcileAction;

        Say(Ui.Transfer.ApplyingAction);
        var applied = 0;
        var refused = 0;
        try
        {
            var client = _client ??= _connect();
            foreach (var (id, revision) in targets)
            {
                var response = mutation switch
                {
                    Mutation.Cancel => await client.CancelAsync(
                        new TransferCancelRequest(TransferQueueIpcContract.CurrentVersion, id, revision),
                        _lifetime.Token).ConfigureAwait(true),
                    Mutation.Retry => await client.RetryAsync(
                        new TransferRetryRequest(TransferQueueIpcContract.CurrentVersion, id, revision),
                        _lifetime.Token).ConfigureAwait(true),
                    _ => await client.ReconcileAsync(
                        new TransferReconcileRequest(
                            TransferQueueIpcContract.CurrentVersion, id, revision, action),
                        _lifetime.Token).ConfigureAwait(true)
                };
                if (IsApplied(response)) applied++;
                else refused++;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception error) when (IsUnavailable(error) || IsRefusal(error))
        {
            await FailedAsync(error).ConfigureAwait(true);
            return;
        }

        Say(refused == 0
            ? Ui.Format(Ui.Transfer.UpdatedTransfersFormat, applied)
            : Ui.Format(Ui.Transfer.UpdatedWithConflictsFormat, applied, refused));
        ShowFromStart();
        await RefreshAsync().ConfigureAwait(true);
    }

    private static bool IsApplied(TransferMutationResponse response) =>
        response.Outcome is TransferQueueMutationOutcome.Applied or TransferQueueMutationOutcome.Accepted;

    /// <summary>An unfinished transfer that can be settled so that it can be cleared.</summary>
    private static bool CanCancelAndClear(TransferRow row) => !row.IsHistory && row.CanCancel;

    /// <summary>
    /// Cancels the unfinished transfers in the selection, then clears them.
    /// </summary>
    /// <remarks>
    /// Only finished transfers can be cleared, so a conflict has to be settled first, and doing
    /// both from one entry is what somebody trying to get rid of the row wants. Only the ones the
    /// agent actually cancelled are cleared: one it refused stays where it is rather than
    /// disappearing unresolved.
    /// </remarks>
    private async Task CancelAndClearAsync()
    {
        // At the revisions shown when it was chosen, for the reason MutateAsync gives.
        var targets = SelectedRows
            .Where(CanCancelAndClear)
            .Select(static row => (row.Id, row.Revision))
            .ToArray();
        if (targets.Length == 0) return;

        Say(Ui.Transfer.ApplyingAction);
        var cancelled = new List<Guid>(targets.Length);
        try
        {
            var client = _client ??= _connect();
            foreach (var (id, revision) in targets)
            {
                var response = await client.CancelAsync(
                    new TransferCancelRequest(TransferQueueIpcContract.CurrentVersion, id, revision),
                    _lifetime.Token).ConfigureAwait(true);
                if (IsApplied(response)) cancelled.Add(id);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception error) when (IsUnavailable(error) || IsRefusal(error))
        {
            await FailedAsync(error).ConfigureAwait(true);
            return;
        }

        if (cancelled.Count == 0)
        {
            Say(Ui.Transfer.NothingCouldBeCancelled);
            return;
        }

        await ClearAsync(cancelled).ConfigureAwait(true);
    }

    /// <summary>
    /// Clears every finished transfer, having asked first while the "Warn before clearing all
    /// transfer history" setting is on.
    /// </summary>
    /// <remarks>
    /// The setting was saved and never read. It cannot be undone, so a queue with no way to ask
    /// clears nothing rather than clearing without asking. The warning's button says what it
    /// does, "Clear history", beside Cancel, as 1.x's own form had it; that form also set a
    /// danger colour on it, which its button never painted, so it was drawn primary.
    /// </remarks>
    private async Task ClearAllHistoryAsync()
    {
        if (ClearAllConfirmation?.Invoke() != false)
        {
            if (_dialogs is null) return;

            DialogChoice choice;
            try
            {
                choice = await _dialogs.ConfirmAsync(
                    new DialogRequest
                    {
                        Title = Ui.Transfer.ClearAllTitle,
                        Message = Ui.Transfer.ClearAllBody,
                        Severity = DialogSeverity.Warning,
                        Buttons = DialogButtons.OkCancel,
                        Accept = Ui.Transfer.ClearHistory,
                        CheckBoxLabel = StopClearAllConfirmation is null ? null : Ui.Transfer.ClearAllSuppress,
                        CheckBoxAnswered = ticked =>
                        {
                            if (ticked) StopClearAllConfirmation?.Invoke();
                        }
                    },
                    _lifetime.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (choice != DialogChoice.Ok) return;
        }

        await ClearAsync(null).ConfigureAwait(true);
    }

    /// <summary>
    /// Clears the given finished transfers, or all of them for null, and says how many went.
    /// </summary>
    /// <remarks>
    /// The contract takes a page of ids per request, so a larger selection goes in several; 1.x
    /// sent the first page and left the rest where they were.
    /// </remarks>
    private async Task ClearAsync(IReadOnlyList<Guid>? ids)
    {
        if (ids is { Count: 0 }) return;

        Say(Ui.Transfer.ClearingHistory);
        var cleared = 0;
        StorageIpcFailure? failure = null;
        try
        {
            var client = _client ??= _connect();
            Guid[][] batches = ids is null
                ? [[]]
                : ids.Distinct().Chunk(TransferQueueIpcLimits.MaximumPageSize).ToArray();
            foreach (var batch in batches)
            {
                var response = await client.ClearHistoryAsync(
                    new TransferHistoryClearRequest(
                        TransferQueueIpcContract.CurrentVersion, batch, ClearAll: batch.Length == 0),
                    _lifetime.Token).ConfigureAwait(true);
                cleared += response.ClearedCount;
                failure = response.Failure;
                if (failure is not null) break;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception error) when (IsUnavailable(error) || IsRefusal(error))
        {
            await FailedAsync(error).ConfigureAwait(true);
            return;
        }

        Say(failure?.Message ?? (cleared == 0
            ? Ui.Transfer.NoHistoryToClear
            : Ui.Format(Ui.Transfer.ClearedHistoryFormat, cleared)));
        if (failure is not null) return;

        ShowFromStart();
        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>A summary as the queue's table shows it.</summary>
    internal static TransferRow ToRow(TransferQueueSummary transfer, Func<Guid, string?>? connectionName = null) =>
        new(transfer, connectionName);

    /// <summary>
    /// One side of a transfer, as Source and Destination show it: "Studio SFTP · /photos/a.jpg".
    /// </summary>
    /// <remarks>
    /// <para>
    /// 1.x wrote the connection and then the path, with a dot between them. The column had lost
    /// the connection, so two transfers of the same file to two servers read the same.
    /// </para>
    /// <para>
    /// 1.x wrote the first eight characters of the connection's id rather than its name. That
    /// is still what is written when the name is not known: for a connection deleted since, before
    /// the connections have been read, and for a This PC folder, which is no saved connection and
    /// whose id is made from the folder.
    /// </para>
    /// </remarks>
    internal static string DescribeEndpoint(Guid connectionId, string path, Func<Guid, string?>? connectionName)
    {
        var connection = connectionName?.Invoke(connectionId) is { Length: > 0 } name
            ? name
            : connectionId.ToString("N", CultureInfo.InvariantCulture)[..8];
        return $"{connection} · {(path.Length == 0 ? "/" : path)}";
    }

    /// <summary>
    /// The state, and what went wrong when something did: "Failed: The server refused the login.",
    /// as 1.x wrote it.
    /// </summary>
    /// <remarks>
    /// This showed the error in place of the state, so a transfer that had failed and one waiting
    /// to retry after the same error read the same.
    /// </remarks>
    internal static string DescribeStatus(TransferQueueSummary transfer) =>
        transfer.ErrorSummary is { Length: > 0 } summary
            ? Ui.Format(Ui.Transfer.StateWithErrorFormat, UiEnumNames.Describe(transfer.State), summary)
            : UiEnumNames.Describe(transfer.State);

    /// <summary>
    /// A percentage when the size is known, bytes moved when it is not; while it runs, also the
    /// speed, and the time left when the size is known. A transfer of nothing is 100%, as 1.x wrote it.
    /// </summary>
    /// <remarks>
    /// ExpectedBytes is null for a provider that does not report a length before the transfer runs,
    /// and showing "0%" for those was the old shell's one persistent complaint about this column.
    /// A stalled transfer shows 0 B/s and no time left, rather than a guess from its last speed.
    /// </remarks>
    internal static string DescribeProgress(TransferQueueSummary transfer)
    {
        var done = transfer.ExpectedBytes switch
        {
            // Nothing to move is all of it, as 1.x said.
            0 => "100%",
            > 0 => string.Create(CultureInfo.CurrentCulture, $"{(ProgressFractionOf(transfer) ?? 0) * 100:0}%"),

            // A finished transfer of unknown size has a full bar but still says what it moved, as
            // 1.x's did, even when that was nothing: "100%" of a size nobody knew would say less,
            // and a bar with no text reads as a bar that lost its label.
            _ when transfer.ProgressBytes > 0 || transfer.State is TransferQueueState.Completed =>
                UiFormatting.FormatBytes(transfer.ProgressBytes),
            _ => string.Empty,
        };

        if (transfer.BytesPerSecond is not { } rate)
        {
            return done;
        }

        var speed = UiFormatting.FormatBytes(rate);
        if (done.Length == 0)
        {
            return Ui.Format(Ui.Shell.StatusTransferRateFormat, speed);
        }

        if (rate > 0 && transfer.ExpectedBytes is { } expected && expected > transfer.ProgressBytes)
        {
            var left = TimeSpan.FromSeconds((double)(expected - transfer.ProgressBytes) / rate);
            return Ui.Format(Ui.Transfer.ProgressRemainingFormat, done, speed, UiFormatting.FormatDuration(left));
        }

        return Ui.Format(Ui.Transfer.ProgressRateFormat, done, speed);
    }

    /// <summary>
    /// The done fraction, or null when the size is not known and the transfer has not finished.
    /// </summary>
    /// <remarks>
    /// One function for both the text and the bar, so the two cannot disagree about a transfer
    /// that has, say, reported more bytes than it expected: both say 100%. A completed transfer is
    /// a full bar whatever its size said, as 1.x drew it; one whose provider never said how big it
    /// was had no bar at all, and read as though it had not run.
    /// </remarks>
    internal static double? ProgressFractionOf(TransferQueueSummary transfer) =>
        transfer.State is TransferQueueState.Completed ? 1
        : transfer.ExpectedBytes is { } expected && expected > 0
            ? Math.Clamp((double)transfer.ProgressBytes / expected, 0, 1)
            : null;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
