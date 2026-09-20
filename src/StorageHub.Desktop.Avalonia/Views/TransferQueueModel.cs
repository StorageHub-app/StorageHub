using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Threading;
using Lucide.Avalonia;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>One transfer, as the queue's table shows it.</summary>
internal sealed record TransferRow(
    Guid Id,
    /// <summary>
    /// What the agent last reported. Cancel and retry send it back, so a request built from a row
    /// the queue has since moved on from is refused rather than applied to a different state.
    /// </summary>
    long Revision,
    string Operation,
    string Source,
    string Destination,
    string Progress,
    string Attempt,
    string Status,
    bool CanCancel,
    bool CanRetry,
    bool NeedsReconciliation);

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
/// </remarks>
internal sealed class TransferQueueModel : INotifyPropertyChanged, IAsyncDisposable
{
    private const int ActivePollMilliseconds = 500;
    private const int IdlePollMilliseconds = 2_000;

    private readonly Func<ITransferQueueAgentClient> _connect;
    private readonly CancellationTokenSource _lifetime = new();
    private ITransferQueueAgentClient? _client;
    private DispatcherTimer? _timer;
    private string _message = string.Empty;
    private int _selectedTab;
    private bool _busy;

    internal TransferQueueModel(Func<ITransferQueueAgentClient> connect)
    {
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
        Tabs = [.. TransferQueueTabs.All.Select(definition => new TransferQueueTab(definition))];

        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
        CancelCommand = new RelayCommand(
            _ => _ = MutateAsync(Mutation.Cancel),
            _ => Selected?.CanCancel == true);
        RetryCommand = new RelayCommand(
            _ => _ = MutateAsync(Mutation.Retry),
            _ => Selected?.CanRetry == true);
        ApplyReconcileCommand = new RelayCommand(
            _ => _ = ReconcileAsync(),
            _ => Selected?.NeedsReconciliation == true);
        NextConflictCommand = new RelayCommand(_ => SelectNextConflict(), _ => Rows.Count > 1);
    }

    public ObservableCollection<TransferQueueTab> Tabs { get; }

    public ObservableCollection<TransferRow> Rows { get; } = [];

    public static string RefreshLabel => Ui.Transfer.Refresh;

    public static string CancelLabel => Ui.Transfer.Cancel;

    public static string RetryLabel => Ui.Transfer.Retry;

    public static string ReconcileLabel => Ui.Transfer.ReconcileLabel;

    public static string ApplyLabel => Ui.Transfer.Apply;

    public static string NextLabel => Ui.Transfer.Next;

    /// <summary>
    /// What to do with a transfer the agent cannot decide about on its own.
    /// </summary>
    /// <remarks>
    /// Three of the contract's five. MarkFailed and Cancel are reachable from the row's own
    /// commands, and a drop-down whose every entry is destructive invites the wrong one.
    /// </remarks>
    public IReadOnlyList<string> ReconcileActions { get; } =
    [
        Ui.Transfer.ReconcileReview,
        Ui.Transfer.ReconcileRestart,
        Ui.Transfer.ReconcileMarkCompleted
    ];

    public int SelectedReconcileAction { get; set; }

    public ICommand ApplyReconcileCommand { get; }

    public ICommand NextConflictCommand { get; }

    /// <summary>What the row area says when it has no rows: empty, or why not.</summary>
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
            _selectedTab = value;
            Raise(nameof(SelectedTab));
            _ = RefreshAsync();
        }
    }

    public TransferRow? Selected { get; set; }

    public ICommand RefreshCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand RetryCommand { get; }

    /// <summary>Transfers the agent reports as running, for the status bar.</summary>
    internal int ActiveCount => Tabs[0].Count;

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
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    /// <summary>Reads the selected tab's transfers and the counts for all of them.</summary>
    internal async Task RefreshAsync()
    {
        // One request in flight. The client is strictly correlated, and a second poll landing on
        // top of a slow one is how a response gets attributed to the wrong tab.
        if (_busy || _lifetime.IsCancellationRequested) return;
        _busy = true;
        try
        {
            var tab = Tabs[Math.Clamp(_selectedTab, 0, Tabs.Count - 1)];
            if (tab.States.Count == 0)
            {
                // Logs. It shows the activity log, which is its own screen and not ported yet; a
                // list request with no states is refused by the contract, and rightly.
                Rows.Clear();
                Message = Ui.Transfer.ActivityNotLoaded;
                Raise(nameof(HasMessage));
                return;
            }

            var response = await ListAsync(tab.States).ConfigureAwait(true);
            if (response is null) return;

            Apply(response);
        }
        finally
        {
            _busy = false;
        }
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
            var response = await _client.ListAsync(
                new TransferListRequest(
                    TransferQueueIpcContract.CurrentVersion,
                    [.. states],
                    PageSize: 100),
                _lifetime.Token).ConfigureAwait(true);

            Message = response.Failure is { } failure
                ? failure.Message
                : string.Empty;
            return response;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception error) when (error is IOException or TimeoutException or
            InvalidOperationException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // The agent is starting, stopped, or mid-restart. Drop the client so the next poll
            // reconnects rather than reusing a broken one.
            await DropClientAsync().ConfigureAwait(true);
            Message = Ui.Transfer.QueueUnavailable;
            Rows.Clear();
            Raise(nameof(HasMessage));
            return null;
        }
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

    private void Apply(TransferListResponse response)
    {
        var selectedId = Selected?.Id;
        Rows.Clear();
        foreach (var transfer in response.Transfers)
        {
            Rows.Add(ToRow(transfer));
        }

        Selected = Rows.FirstOrDefault(row => row.Id == selectedId);

        if (response.StateCounts is { } counts)
        {
            foreach (var tab in Tabs)
            {
                tab.Count = tab.States.Sum(state => counts.TryGetValue(state, out var value) ? value : 0);
            }
        }

        if (Rows.Count > 0)
        {
            Message = string.Empty;
        }
        else if (string.IsNullOrEmpty(Message))
        {
            Message = Ui.Transfer.NoTransfers;
        }

        Raise(nameof(HasMessage));
        Raise(nameof(ActiveCount));
        Interval = Tabs[0].Count > 0 ? ActivePollMilliseconds : IdlePollMilliseconds;
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RetryCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ApplyReconcileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NextConflictCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private int Interval
    {
        set
        {
            if (_timer is null || (int)_timer.Interval.TotalMilliseconds == value) return;
            _timer.Interval = TimeSpan.FromMilliseconds(value);
        }
    }

    /// <summary>
    /// Applies the chosen reconciliation to the selected transfer.
    /// </summary>
    /// <remarks>
    /// Revision-checked like cancel and retry: the agent refuses a decision made about a state the
    /// transfer has already left, which matters most here because reconciling is the one action
    /// that can mark an incomplete transfer complete.
    /// </remarks>
    private async Task ReconcileAsync()
    {
        if (Selected is not { } row || _client is null) return;

        var action = SelectedReconcileAction switch
        {
            1 => TransferReconciliationAction.Restart,
            2 => TransferReconciliationAction.MarkCompleted,
            _ => TransferReconciliationAction.Review
        };

        try
        {
            _ = await _client.ReconcileAsync(
                new TransferReconcileRequest(
                    TransferQueueIpcContract.CurrentVersion, row.Id, row.Revision, action),
                _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception error) when (error is IOException or TimeoutException or
            InvalidOperationException or ObjectDisposedException)
        {
            await DropClientAsync().ConfigureAwait(true);
            Message = Ui.Transfer.QueueUnavailable;
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Moves to the next row, wrapping, so a list of conflicts can be worked through.</summary>
    private void SelectNextConflict()
    {
        if (Rows.Count == 0) return;

        var current = Selected is null ? -1 : Rows.IndexOf(Selected);
        Selected = Rows[(current + 1) % Rows.Count];
        Raise(nameof(Selected));
        RaiseCommands();
    }

    private enum Mutation
    {
        Cancel,
        Retry
    }

    private async Task MutateAsync(Mutation mutation)
    {
        if (Selected is not { } row || _client is null) return;

        try
        {
            _ = mutation == Mutation.Cancel
                ? await _client.CancelAsync(
                    new TransferCancelRequest(TransferQueueIpcContract.CurrentVersion, row.Id, row.Revision),
                    _lifetime.Token).ConfigureAwait(true)
                : await _client.RetryAsync(
                    new TransferRetryRequest(TransferQueueIpcContract.CurrentVersion, row.Id, row.Revision),
                    _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception error) when (error is IOException or TimeoutException or
            InvalidOperationException or ObjectDisposedException)
        {
            await DropClientAsync().ConfigureAwait(true);
            Message = Ui.Transfer.QueueUnavailable;
            return;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// A summary as six columns.
    /// </summary>
    /// <remarks>
    /// The paths are shown whole rather than shortened. The old shell elided the middle to fit a
    /// fixed column; the columns here are resizable, so eliding would throw away what the person
    /// widened the column to read.
    /// </remarks>
    internal static TransferRow ToRow(TransferQueueSummary transfer) => new(
        transfer.TransferId,
        transfer.Revision,
        UiEnumNames.Describe(transfer.Operation),
        transfer.SourcePath,
        transfer.DestinationPath,
        DescribeProgress(transfer),
        transfer.Attempt.ToString(CultureInfo.CurrentCulture),
        transfer.ErrorSummary is { Length: > 0 } summary
            ? summary
            : UiEnumNames.Describe(transfer.State),
        transfer.CanCancel,
        transfer.CanRetry,
        transfer.NeedsReconciliation);

    /// <summary>
    /// A percentage when the size is known, bytes moved when it is not.
    /// </summary>
    /// <remarks>
    /// ExpectedBytes is null for a provider that does not report a length before the transfer runs,
    /// and showing "0%" for those was the old shell's one persistent complaint about this column.
    /// </remarks>
    private static string DescribeProgress(TransferQueueSummary transfer)
    {
        if (transfer.ExpectedBytes is not { } expected || expected <= 0)
        {
            return transfer.ProgressBytes > 0
                ? UiFormatting.FormatBytes(transfer.ProgressBytes)
                : string.Empty;
        }

        var percent = Math.Clamp(transfer.ProgressBytes * 100d / expected, 0, 100);
        return string.Create(CultureInfo.CurrentCulture, $"{percent:0}%");
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
