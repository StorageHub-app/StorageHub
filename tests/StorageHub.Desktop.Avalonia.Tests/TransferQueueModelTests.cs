using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

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

        internal IReadOnlyList<TransferQueueState> LastStates { get; private set; } = [];

        internal bool Throw { get; set; }

        internal long? CancelledRevision { get; private set; }

        public Task<TransferListResponse> ListAsync(
            TransferListRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Throw) throw new IOException("The agent is not listening.");

            LastStates = request.States;
            var matching = Transfers.Where(t => request.States.Contains(t.State)).ToArray();
            return Task.FromResult(new TransferListResponse(
                TransferQueueIpcContract.CurrentVersion,
                matching,
                ContinuationToken: null,
                Failure: null,
                Counts));
        }

        public Task<TransferMutationResponse> CancelAsync(
            TransferCancelRequest request,
            CancellationToken cancellationToken = default)
        {
            CancelledRevision = request.ExpectedRevision;
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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
