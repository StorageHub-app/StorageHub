using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The transfer queue against a stand-in agent.
/// </summary>
/// <remarks>
/// A fake client rather than a real one, because what is worth pinning here is the queue's own
/// behaviour: which states it asks for, what it does with the counts, and what happens when the
/// agent is not there. NamedPipeTransferQueueAgentClient has its own suite for the wire.
/// </remarks>
public class TransferQueueModelTests
{
    [AvaloniaFact]
    public async Task TheSelectedTabDecidesWhichStatesAreAskedFor()
    {
        var agent = new FakeQueueAgent();
        await using var queue = new TransferQueueModel(() => agent);

        await queue.RefreshAsync();
        Assert.Equal(TransferQueueTabs.StatesFor(TransferQueueTabs.ActiveKey), agent.LastStates);

        queue.SelectedTab = IndexOf(queue, TransferQueueTabs.FailedKey);
        await queue.RefreshAsync();

        Assert.Equal([TransferQueueState.Failed], agent.LastStates);
    }

    /// <summary>
    /// More than a page is read a page at a time, within the contract's limit, up to four pages;
    /// Next shows the rows after those, as 1.x's did, and Refresh comes back to the newest.
    /// </summary>
    [AvaloniaFact]
    public async Task MoreThanOnePageIsReadAPageAtATime()
    {
        var agent = new FakeQueueAgent();
        for (var index = 0; index < 250; index++) agent.Transfers.Add(Summary(TransferQueueState.Transferring));
        await using var queue = new TransferQueueModel(() => agent);

        await queue.RefreshAsync();

        Assert.Equal(200, queue.Rows.Count);
        Assert.False(queue.HasMessage);
        Assert.True(queue.NextPageCommand.CanExecute(null));

        queue.NextPageCommand.Execute(null);
        await queue.RefreshAsync();

        Assert.Equal(50, queue.Rows.Count);
        Assert.Equal(agent.Transfers[200].TransferId, queue.Rows[0].Id);
        Assert.False(queue.NextPageCommand.CanExecute(null));

        queue.RefreshCommand.Execute(null);
        await queue.RefreshAsync();

        Assert.Equal(agent.Transfers[0].TransferId, queue.Rows[0].Id);
        Assert.Equal(200, queue.Rows.Count);
    }

    /// <summary>
    /// Several rows chosen in the table stay chosen through the polls, where the table was
    /// scrolled to, and on their tab while another is looked at; they are acted on together, a
    /// right-click opens the menu on the row it lands on, and clearing all history asks first.
    /// </summary>
    /// <remarks>
    /// The rows were rebuilt on every poll, which took the selection with them every 0.5 to 2
    /// seconds. The agent lists the most recently changed first, so a running transfer moves up as
    /// it goes, and a table lets go of a row that moves, then scrolled back to the row when it was
    /// chosen again: the second poll here moves one. The first right-click opened no menu.
    /// </remarks>
    [AvaloniaFact]
    public async Task SeveralRowsStayChosenThroughThePollsAndAreActedOnTogether()
    {
        var agent = new FakeQueueAgent();
        var third = Summary(TransferQueueState.Transferring, progress: 300);
        agent.Transfers.AddRange([
            Summary(TransferQueueState.Transferring, progress: 100),
            Summary(TransferQueueState.Transferring, progress: 200),
            third]);
        for (var index = 0; index < 40; index++) agent.Transfers.Add(Summary(TransferQueueState.Transferring));
        var dialogs = new KeyStoreTests.RecordingDialogs { Choice = DialogChoice.Ok };
        var stopped = false;
        await using var queue = new TransferQueueModel(() => agent, dialogs: dialogs)
        {
            StopClearAllConfirmation = () => stopped = true
        };
        var window = new Window
        {
            Width = 1000,
            Height = 320,
            Content = new TransferQueueView { DataContext = queue }
        };
        window.Show();
        await queue.RefreshAsync();
        var table = window.GetVisualDescendants().OfType<TableView>().First(each => each.IsVisible);
        var scroller = table.GetVisualDescendants().OfType<ScrollViewer>().First();
        var moving = queue.Rows[2];

        // How many are shown, as 1.x said after every read.
        Assert.Equal(Ui.Format(Ui.Transfer.TransferCountFormat, 43), queue.Message);

        table.Selection.Select(1);
        table.Selection.Select(2);

        // Straight away, not on the next poll.
        Assert.Equal(2, queue.SelectedRows.Count);
        Assert.True(queue.CancelCommand.CanExecute(null));
        Assert.True(queue.CancelAndClearCommand.CanExecute(null));
        Assert.False(queue.ClearSelectedCommand.CanExecute(null));

        scroller.Offset = new Vector(0, 400);
        Settle(window);
        agent.Transfers.Remove(third);
        agent.Transfers.Insert(0, third with { ProgressBytes = 900, Revision = 2 });
        await queue.RefreshAsync();
        Settle(window);

        Assert.Same(moving, queue.Rows[0]);
        Assert.Equal("90%", moving.Progress);
        Assert.Equal(2, table.Selection.SelectedItems.Count);
        Assert.Contains(moving, table.Selection.SelectedItems.Cast<TransferRow>());
        Assert.Equal(400, scroller.Offset.Y);

        queue.SelectedTab = IndexOf(queue, TransferQueueTabs.FailedKey);
        await queue.RefreshAsync();
        queue.SelectedTab = IndexOf(queue, TransferQueueTabs.ActiveKey);
        await queue.RefreshAsync();

        // New rows by then, since the tab between had none; the same transfers.
        Assert.Equal(2, table.Selection.SelectedItems.Count);
        Assert.Contains(moving.Id, queue.SelectedRows.Select(static row => row.Id));

        queue.CancelCommand.Execute(null);
        await queue.RefreshAsync();

        Assert.Equal(
            queue.SelectedRows.Select(static row => (row.Id, row.Revision)).Order(),
            agent.Cancelled.Order());

        // A real right-click, on a row outside the selection: it becomes the selection, and the
        // menu opens the first time.
        scroller.Offset = default;
        Settle(window);
        var clicked = queue.Rows[1];
        var cell = window.GetVisualDescendants().OfType<TextBlock>()
            .First(each => ReferenceEquals(each.DataContext, clicked) && each.IsEffectivelyVisible);
        var point = cell.TranslatePoint(new Point(4, 4), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Settle(window);

        Assert.Same(clicked, Assert.Single(queue.SelectedRows));
        Assert.True(table.ContextMenu!.IsOpen);
        table.ContextMenu.Close();

        queue.ClearAllHistoryCommand.Execute(null);
        dialogs.LastRequest!.CheckBoxAnswered!(true);

        Assert.Equal(Ui.Transfer.ClearAllTitle, dialogs.LastRequest.Title);
        Assert.Equal(Ui.Transfer.ClearHistory, dialogs.LastRequest.Accept);
        Assert.True(Assert.Single(agent.Cleared).ClearAll);
        Assert.True(stopped);
    }

    [AvaloniaFact]
    public async Task EveryTabShowsTheCountTheAgentReportedForIt()
    {
        var agent = new FakeQueueAgent
        {
            Counts = new Dictionary<TransferQueueState, int>
            {
                [TransferQueueState.Transferring] = 2,
                [TransferQueueState.Verifying] = 1,
                [TransferQueueState.Pending] = 4,
                [TransferQueueState.Failed] = 3
            }
        };
        await using var queue = new TransferQueueModel(() => agent);

        await queue.RefreshAsync();

        // Active is the sum of six states, not one of them.
        Assert.Equal(3, Tab(queue, TransferQueueTabs.ActiveKey).Count);
        Assert.Equal(4, Tab(queue, TransferQueueTabs.QueuedKey).Count);
        Assert.Equal(3, Tab(queue, TransferQueueTabs.FailedKey).Count);
        Assert.Equal(0, Tab(queue, TransferQueueTabs.PausedKey).Count);
        Assert.Contains("(3)", Tab(queue, TransferQueueTabs.ActiveKey).Title, StringComparison.Ordinal);
        Assert.Equal(3, queue.ActiveCount);
    }

    /// <summary>
    /// The status bar's speed is the agent's total for everything running, whichever tab is open,
    /// and drops to nothing when the agent stops reporting one.
    /// </summary>
    [AvaloniaFact]
    public async Task TheTotalSpeedIsTheAgentsWhicheverTabIsOpen()
    {
        var agent = new FakeQueueAgent { TotalBytesPerSecond = 5_000 };
        await using var queue = new TransferQueueModel(() => agent);
        var raised = new List<string?>();
        queue.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        queue.SelectedTab = IndexOf(queue, TransferQueueTabs.FailedKey);
        await queue.RefreshAsync();
        Assert.Equal(5_000, queue.BytesPerSecond);
        Assert.Contains(nameof(TransferQueueModel.BytesPerSecond), raised);

        agent.TotalBytesPerSecond = null;
        await queue.RefreshAsync();
        Assert.Equal(0, queue.BytesPerSecond);
    }

    [AvaloniaFact]
    public async Task ARowCarriesWhatTheColumnsShow()
    {
        var agent = new FakeQueueAgent();
        agent.Transfers.Add(Summary(state: TransferQueueState.Transferring, expected: 1000, progress: 250));
        await using var queue = new TransferQueueModel(() => agent);

        await queue.RefreshAsync();

        var row = Assert.Single(queue.Rows);
        Assert.Equal("25%", row.Progress);
        Assert.Equal("/from/file.bin", row.Source);
        Assert.Equal("/to/file.bin", row.Destination);
        Assert.True(row.CanCancel);
    }

    /// <summary>
    /// A provider that cannot say how big a file is gets bytes moved, not "0%".
    /// </summary>
    /// <remarks>
    /// ExpectedBytes is null for those, and the old shell showed 0% for the whole transfer -- its
    /// one persistent complaint about this column.
    /// </remarks>
    [AvaloniaFact]
    public async Task AnUnknownSizeShowsBytesMovedRatherThanZeroPercent()
    {
        var agent = new FakeQueueAgent();
        agent.Transfers.Add(Summary(TransferQueueState.Transferring, expected: null, progress: 2048));
        await using var queue = new TransferQueueModel(() => agent);

        await queue.RefreshAsync();

        var row = Assert.Single(queue.Rows);
        Assert.DoesNotContain("%", row.Progress, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(row.Progress));
    }

    /// <summary>
    /// Cancel sends the revision the row was read at.
    /// </summary>
    /// <remarks>
    /// The queue is revision-checked, so a cancel built from a row the agent has since moved on
    /// from is refused rather than applied to whatever the transfer is doing now.
    /// </remarks>
    [AvaloniaFact]
    public async Task CancellingSendsTheRevisionTheRowWasReadAt()
    {
        var agent = new FakeQueueAgent();
        agent.Transfers.Add(Summary(TransferQueueState.Transferring, revision: 77));
        await using var queue = new TransferQueueModel(() => agent);
        await queue.RefreshAsync();

        queue.Selected = queue.Rows[0];
        queue.CancelCommand.Execute(null);
        await queue.RefreshAsync();

        Assert.Equal(77, agent.CancelledRevision);
    }

    /// <summary>Reconciling sends the action the drop-down is on, for the selected transfer.</summary>
    [AvaloniaFact]
    public async Task ReconcilingSendsTheChosenAction()
    {
        var agent = new FakeQueueAgent();
        agent.Transfers.Add(Summary(TransferQueueState.NeedsReconciliation, needsReconciliation: true));
        await using var queue = new TransferQueueModel(() => agent);
        queue.SelectedTab = IndexOf(queue, TransferQueueTabs.ConflictsKey);
        await queue.RefreshAsync();

        queue.Selected = queue.Rows[0];
        queue.SelectedReconcileAction = 2;
        queue.ApplyReconcileCommand.Execute(null);
        await queue.RefreshAsync();

        Assert.Equal(TransferReconciliationAction.MarkCompleted, agent.ReconciledAs);
    }

    [AvaloniaFact]
    public async Task AnAgentThatIsNotThereLeavesAMessageRatherThanThrowing()
    {
        var agent = new FakeQueueAgent { Throw = true };
        await using var queue = new TransferQueueModel(() => agent);

        await queue.RefreshAsync();

        Assert.Empty(queue.Rows);
        Assert.True(queue.HasMessage);
        Assert.False(string.IsNullOrWhiteSpace(queue.Message));
    }

    /// <summary>The next poll reconnects rather than reusing a client that broke.</summary>
    [AvaloniaFact]
    public async Task ABrokenClientIsReplacedOnTheNextPoll()
    {
        var connects = 0;
        var agent = new FakeQueueAgent { Throw = true };
        await using var queue = new TransferQueueModel(() => { connects++; return agent; });

        await queue.RefreshAsync();
        await queue.RefreshAsync();

        Assert.Equal(2, connects);
    }

    [AvaloniaFact]
    public async Task AnEmptyTabSaysSoRatherThanShowingNothing()
    {
        await using var queue = new TransferQueueModel(() => new FakeQueueAgent());

        await queue.RefreshAsync();

        Assert.Empty(queue.Rows);
        Assert.True(queue.HasMessage);
    }

    private static TransferQueueTab Tab(TransferQueueModel queue, string key) =>
        queue.Tabs.Single(tab => tab.Key == key);

    private static int IndexOf(TransferQueueModel queue, string key)
    {
        for (var index = 0; index < queue.Tabs.Count; index++)
        {
            if (queue.Tabs[index].Key == key) return index;
        }

        throw new ArgumentOutOfRangeException(nameof(key), key, "No such tab.");
    }

    /// <summary>Runs what input and a poll queued, and lays the window out again.</summary>
    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static TransferQueueSummary Summary(
        TransferQueueState state,
        long? expected = 1000,
        long progress = 0,
        long revision = 1,
        bool needsReconciliation = false) =>
        new(
            Guid.NewGuid(),
            TransferQueueOperation.Copy,
            Guid.NewGuid(),
            "/from/file.bin",
            Guid.NewGuid(),
            "/to/file.bin",
            state,
            revision,
            Attempt: 1,
            Priority: 0,
            expected,
            progress,
            DateTimeOffset.UtcNow,
            RetryAvailableUtc: null,
            ErrorCode: null,
            ErrorSummary: null,
            CanCancel: true,
            CanRetry: false,
            needsReconciliation);

    /// <summary>A queue client that answers from memory and records what it was asked.</summary>
    private sealed class FakeQueueAgent : ITransferQueueAgentClient
    {
        internal List<TransferQueueSummary> Transfers { get; } = [];

        internal Dictionary<TransferQueueState, int>? Counts { get; set; }

        internal long? TotalBytesPerSecond { get; set; }

        internal IReadOnlyList<TransferQueueState> LastStates { get; private set; } = [];

        internal bool Throw { get; set; }

        internal long? CancelledRevision { get; private set; }

        internal List<(Guid Id, long Revision)> Cancelled { get; } = [];

        internal List<TransferHistoryClearRequest> Cleared { get; } = [];

        public Task<TransferListResponse> ListAsync(
            TransferListRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Throw) throw new IOException("The agent is not listening.");

            // As the real client does: a request outside the contract is refused before it is sent.
            // The fake used to accept anything, which is how a page size of 100 against a limit of
            // 50 passed every test here while the real queue never listed a thing.
            if (!request.HasValidBounds)
            {
                throw new ArgumentException("The transfer request is outside the negotiated IPC contract bounds.", nameof(request));
            }

            LastStates = request.States;
            var matching = Transfers.Where(t => request.States.Contains(t.State)).ToArray();
            var start = request.ContinuationToken is { } token ? int.Parse(token, System.Globalization.CultureInfo.InvariantCulture) : 0;
            var page = matching.Skip(start).Take(request.PageSize).ToArray();
            var next = start + page.Length < matching.Length
                ? (start + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
            return Task.FromResult(new TransferListResponse(
                TransferQueueIpcContract.CurrentVersion,
                page,
                ContinuationToken: next,
                Failure: null,
                Counts,
                TotalBytesPerSecond));
        }

        public Task<TransferMutationResponse> CancelAsync(
            TransferCancelRequest request,
            CancellationToken cancellationToken = default)
        {
            CancelledRevision = request.ExpectedRevision;
            Cancelled.Add((request.TransferId, request.ExpectedRevision));
            return Task.FromResult(new TransferMutationResponse(
                TransferQueueIpcContract.CurrentVersion,
                request.TransferId,
                TransferQueueMutationOutcome.Applied));
        }

        public Task<TransferMutationResponse> RetryAsync(
            TransferRetryRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TransferMutationResponse(
                TransferQueueIpcContract.CurrentVersion,
                request.TransferId,
                TransferQueueMutationOutcome.Applied));

        public Task<TransferEnqueueResponse> EnqueueAsync(
            TransferEnqueueRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TransferStatusResponse> GetStatusAsync(
            TransferStatusRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        internal TransferReconciliationAction? ReconciledAs { get; private set; }

        public Task<TransferMutationResponse> ReconcileAsync(
            TransferReconcileRequest request,
            CancellationToken cancellationToken = default)
        {
            ReconciledAs = request.Action;
            return Task.FromResult(new TransferMutationResponse(
                TransferQueueIpcContract.CurrentVersion,
                request.TransferId,
                TransferQueueMutationOutcome.Applied));
        }

        public Task<TransferHistoryClearResponse> ClearHistoryAsync(
            TransferHistoryClearRequest request,
            CancellationToken cancellationToken = default)
        {
            Cleared.Add(request);
            return Task.FromResult(new TransferHistoryClearResponse(
                TransferQueueIpcContract.CurrentVersion, ClearedCount: 0));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
