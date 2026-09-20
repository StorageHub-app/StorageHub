using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Reading a sync run and approving it.
/// </summary>
/// <remarks>
/// The screen this came from is the one place in StorageHub where pressing a button deletes files
/// somebody may not have meant to delete. In WinForms its protocol was spread through nine hundred
/// lines of grids and tab pages and could not be exercised without a window, so the checks that
/// make approval safe -- that the plan listed is the plan being approved, that the digest sent is
/// the one shown -- were never tested at all. They are what most of this file is about.
/// </remarks>
public class SyncRunReviewControllerTests
{
    private const string PlanDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ApprovalDigest = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    [Fact]
    public async Task ALoadedRunCarriesItsPlanAndItsConflicts()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new FakeReviewAgent { Status = run };
        agent.PlanPage(run, [Operation(1), Operation(2)], null);
        agent.ConflictPage([Conflict("photos/a.jpg")], null);

        var review = await Controller(agent).LoadRunAsync(run.SyncRunId, CancellationToken.None);

        Assert.False(review.Failed);
        Assert.Equal(2, review.Operations.Count);
        Assert.Single(review.Conflicts);
        Assert.True(review.CanApprove);
    }

    /// <summary>
    /// A plan page that belongs to a different plan is refused.
    /// </summary>
    /// <remarks>
    /// A run re-previewed between the status read and this page would otherwise have its new
    /// operations listed under the digest Approve is about to send: the reviewer reads one plan and
    /// authorises another. Nothing else on the screen would look wrong.
    /// </remarks>
    [Fact]
    public async Task APlanPageFromAnotherPlanIsRefused()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new FakeReviewAgent { Status = run };
        agent.PlanPage(run with { PlanId = Guid.NewGuid() }, [Operation(1)], null);

        var review = await Controller(agent).LoadRunAsync(run.SyncRunId, CancellationToken.None);

        Assert.Equal(Ui.Sync.PlanPageMismatch, review.ErrorMessage);
        Assert.Empty(review.Operations);
    }

    /// <summary>And so is one whose contents hash to something else.</summary>
    [Fact]
    public async Task APlanPageWithAnotherDigestIsRefused()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new FakeReviewAgent { Status = run };
        agent.PlanPage(run with { PlanSha256 = new string('c', 64) }, [Operation(1)], null);

        var review = await Controller(agent).LoadRunAsync(run.SyncRunId, CancellationToken.None);

        Assert.Equal(Ui.Sync.PlanPageMismatch, review.ErrorMessage);
    }

    /// <summary>
    /// The digest comparison ignores case.
    /// </summary>
    /// <remarks>
    /// Hex is hex. A correct page refused because one side spelled it in capitals would present as
    /// a tampering alarm on a run that is perfectly sound, which is the kind of false alarm that
    /// teaches people to ignore the real one.
    /// </remarks>
    [Fact]
    public async Task ADigestInTheOtherCaseIsStillTheSamePlan()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new FakeReviewAgent { Status = run };
        agent.PlanPage(run with { PlanSha256 = PlanDigest.ToUpperInvariant() }, [Operation(1)], null);
        agent.ConflictPage([], null);

        var review = await Controller(agent).LoadRunAsync(run.SyncRunId, CancellationToken.None);

        Assert.False(review.Failed);
        Assert.Single(review.Operations);
    }

    /// <summary>
    /// Approving sends the revision and digest of the run that was on screen.
    /// </summary>
    /// <remarks>
    /// Not a fresh read. The agent rejects a stale revision, and that rejection is the whole
    /// safety mechanism: re-reading here to get a revision the agent would accept would defeat it
    /// silently, and approve whatever the run had become instead of what was reviewed.
    /// </remarks>
    [Fact]
    public async Task ApprovalCarriesTheRevisionAndDigestThatWereReviewed()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval) with { Revision = 41 };
        var agent = new FakeReviewAgent { Status = run };
        agent.PlanPage(run, [Operation(1)], null);
        agent.ConflictPage([], null);
        agent.Dispatched = run with
        {
            Revision = 42,
            DispatchState = SyncIpcDispatchState.DurablyDispatched
        };

        var controller = Controller(agent);
        var review = await controller.LoadRunAsync(run.SyncRunId, CancellationToken.None);
        review = await controller.ApproveAndDispatchAsync(review, CancellationToken.None);

        Assert.False(review.Failed);
        Assert.Equal(41, agent.ApprovalSent!.ExpectedRevision);
        Assert.Equal(ApprovalDigest, agent.ApprovalSent.ApprovalSha256);
        Assert.Equal(SyncIpcDispatchState.DurablyDispatched, review.Run!.DispatchState);
    }

    /// <summary>A run the agent is not waiting on is not approved.</summary>
    [Fact]
    public async Task ARunThatIsNotAwaitingApprovalIsNotDispatched()
    {
        var agent = new FakeReviewAgent();
        var review = new SyncRunReview(Run(SyncIpcRunPhase.Scanning), [], []);

        var result = await Controller(agent).ApproveAndDispatchAsync(review, CancellationToken.None);

        Assert.Equal(Ui.Sync.NotAwaitingApproval, result.ErrorMessage);
        Assert.Null(agent.ApprovalSent);
    }

    /// <summary>
    /// A run already dispatched is left alone and reported as fine.
    /// </summary>
    /// <remarks>
    /// The request is idempotent on the agent, so sending it again would be accepted. It would also
    /// be a second apparently-successful approval of work already under way, which is exactly how
    /// somebody concludes the first one did not take.
    /// </remarks>
    [Fact]
    public async Task ARunAlreadyDispatchedIsNotDispatchedAgain()
    {
        var agent = new FakeReviewAgent();
        var review = new SyncRunReview(
            Run(SyncIpcRunPhase.Executing) with
            {
                DispatchState = SyncIpcDispatchState.DurablyDispatched
            },
            [],
            []);

        var result = await Controller(agent).ApproveAndDispatchAsync(review, CancellationToken.None);

        Assert.False(result.Failed);
        Assert.Null(agent.ApprovalSent);
    }

    /// <summary>
    /// Three ways an answer can look like agreement without being it.
    /// </summary>
    /// <remarks>
    /// Dispatch is the one thing on this screen that must never be reported optimistically: the
    /// operator walks away believing the sync is under way. Each of these has to be caught
    /// separately, because each leaves the other two looking correct.
    /// </remarks>
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public async Task ADispatchIsOnlyBelievedWhenTheAgentConfirmsIt(
        bool durablyDispatched,
        bool answersAnotherRun,
        bool stateChanged)
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new FakeReviewAgent
        {
            DurablyDispatched = durablyDispatched,
            Dispatched = run with
            {
                SyncRunId = answersAnotherRun ? Guid.NewGuid() : run.SyncRunId,
                DispatchState = stateChanged
                    ? SyncIpcDispatchState.DurablyDispatched
                    : SyncIpcDispatchState.NotDispatched
            }
        };

        var result = await Controller(agent)
            .ApproveAndDispatchAsync(new SyncRunReview(run, [], []), CancellationToken.None);

        Assert.Equal(Ui.Sync.DispatchNotConfirmed, result.ErrorMessage);
    }

    /// <summary>Approve is offered exactly where the agent would accept it.</summary>
    [Theory]
    [InlineData(SyncIpcRunPhase.AwaitingApproval, SyncIpcDispatchState.NotDispatched, true)]
    [InlineData(SyncIpcRunPhase.AwaitingApproval, SyncIpcDispatchState.DurablyDispatched, false)]
    [InlineData(SyncIpcRunPhase.Planning, SyncIpcDispatchState.NotDispatched, false)]
    [InlineData(SyncIpcRunPhase.Completed, SyncIpcDispatchState.DurablyDispatched, false)]
    public void ApprovalIsOfferedOnlyWhenItWouldBeAccepted(
        SyncIpcRunPhase phase,
        SyncIpcDispatchState dispatch,
        bool expected)
    {
        var review = new SyncRunReview(Run(phase) with { DispatchState = dispatch }, [], []);

        Assert.Equal(expected, review.CanApprove);
    }

    /// <summary>Nothing can be approved before a run is loaded.</summary>
    [Fact]
    public void AnEmptyReviewOffersNothing()
    {
        Assert.False(SyncRunReview.Empty.CanApprove);
        Assert.False(SyncRunReview.Empty.HasMoreOperations);
        Assert.False(SyncRunReview.Empty.HasMoreConflicts);
    }

    /// <summary>A second page of operations is added to the first, not put in its place.</summary>
    [Fact]
    public async Task MoreOperationsAreAppended()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new FakeReviewAgent { Status = run };
        agent.PlanPage(run, [Operation(1), Operation(2)], "50");
        agent.ConflictPage([], null);
        agent.PlanPage(run, [Operation(3)], null);

        var controller = Controller(agent);
        var review = await controller.LoadRunAsync(run.SyncRunId, CancellationToken.None);
        Assert.True(review.HasMoreOperations);

        review = await controller.LoadMoreOperationsAsync(review, CancellationToken.None);

        Assert.Equal([1, 2, 3], review.Operations.Select(static operation => operation.Sequence));
        Assert.False(review.HasMoreOperations);
    }

    /// <summary>And so is a second page of conflicts.</summary>
    [Fact]
    public async Task MoreConflictsAreAppended()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new FakeReviewAgent { Status = run };
        agent.PlanPage(run, [], null);
        agent.ConflictPage([Conflict("a")], "50");
        agent.ConflictPage([Conflict("b")], null);

        var controller = Controller(agent);
        var review = await controller.LoadRunAsync(run.SyncRunId, CancellationToken.None);
        review = await controller.LoadMoreConflictsAsync(review, CancellationToken.None);

        Assert.Equal(["a", "b"], review.Conflicts.Select(static conflict => conflict.RelativePath));
    }

    /// <summary>
    /// A continuation token that comes back unchanged is treated as the end.
    /// </summary>
    /// <remarks>
    /// The same guard the tasks screen needs, for the same reason: following it against a live pipe
    /// pages for ever, and a screen that hangs has nothing to report and no way out of it.
    /// </remarks>
    [Fact]
    public async Task ARepeatedPlanTokenEndsThePaging()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new FakeReviewAgent { Status = run };
        agent.PlanPage(run, [Operation(1)], "7");
        agent.ConflictPage([], null);
        agent.PlanPage(run, [Operation(2)], "7");

        var controller = Controller(agent);
        var review = await controller.LoadRunAsync(run.SyncRunId, CancellationToken.None);
        review = await controller.LoadMoreOperationsAsync(review, CancellationToken.None);

        Assert.False(review.HasMoreOperations);
    }

    /// <summary>History arrives a page at a time, and says whether there is another.</summary>
    [Fact]
    public async Task HistoryReportsWhetherThereIsAnotherPage()
    {
        var agent = new FakeReviewAgent();
        agent.HistoryPage([Run(SyncIpcRunPhase.Completed), Run(SyncIpcRunPhase.Failed)], "50");

        var page = await Controller(agent).LoadHistoryAsync(null, CancellationToken.None);

        Assert.Equal(2, page.Runs.Count);
        Assert.True(page.HasNextPage);
        Assert.Equal("50", page.NextPageToken);
    }

    /// <summary>A repeated history token is the end of the history.</summary>
    [Fact]
    public async Task ARepeatedHistoryTokenEndsTheHistory()
    {
        var agent = new FakeReviewAgent();
        agent.HistoryPage([Run(SyncIpcRunPhase.Completed)], "7");

        var page = await Controller(agent).LoadHistoryAsync("7", CancellationToken.None);

        Assert.False(page.HasNextPage);
    }

    /// <summary>
    /// Refreshing the phase leaves the plan where it was.
    /// </summary>
    /// <remarks>
    /// A plan is immutable once previewed, so re-reading its pages would cost a reviewer their
    /// place in a long list every five seconds and tell them nothing they did not have.
    /// </remarks>
    [Fact]
    public async Task RefreshingTheStatusKeepsTheLoadedPlan()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new FakeReviewAgent { Status = run };
        agent.PlanPage(run, [Operation(1), Operation(2)], null);
        agent.ConflictPage([Conflict("a")], null);

        var controller = Controller(agent);
        var review = await controller.LoadRunAsync(run.SyncRunId, CancellationToken.None);

        agent.Status = run with { Phase = SyncIpcRunPhase.Executing };
        review = await controller.RefreshStatusAsync(review, CancellationToken.None);

        Assert.Equal(SyncIpcRunPhase.Executing, review.Run!.Phase);
        Assert.Equal(2, review.Operations.Count);
        Assert.Single(review.Conflicts);
    }

    /// <summary>
    /// A failure mid-way leaves on screen what had already been read.
    /// </summary>
    /// <remarks>
    /// The alternative is that a transient error blanks a plan somebody was halfway through
    /// reviewing, which loses their place and looks exactly like a run with no operations in it.
    /// </remarks>
    [Fact]
    public async Task AFailedRefreshKeepsWhatWasAlreadyRead()
    {
        var run = Run(SyncIpcRunPhase.AwaitingApproval);
        var agent = new FakeReviewAgent { Status = run };
        agent.PlanPage(run, [Operation(1)], null);
        agent.ConflictPage([], null);

        var controller = Controller(agent);
        var review = await controller.LoadRunAsync(run.SyncRunId, CancellationToken.None);

        agent.Throws = new IOException("the pipe went away");
        review = await controller.RefreshStatusAsync(review, CancellationToken.None);

        Assert.True(review.Failed);
        Assert.Single(review.Operations);
        Assert.NotNull(review.Run);
    }

    /// <summary>A refused read is reported in the words the operator can act on.</summary>
    [Fact]
    public async Task ARefusalIsDescribedRatherThanRepeated()
    {
        var agent = new FakeReviewAgent
        {
            StatusFailure = new StorageIpcFailure(
                "sync.profile.not_found",
                StorageIpcFailureCategory.NotFound,
                "not found",
                IsTransient: false)
        };

        var review = await Controller(agent).LoadRunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(Ui.Sync.ProfileNotFound, review.ErrorMessage);
    }

    /// <summary>An empty run id never reaches the agent.</summary>
    [Fact]
    public async Task AnEmptyRunIdIsRefusedBeforeAsking()
    {
        var agent = new FakeReviewAgent();

        var review = await Controller(agent).LoadRunAsync(Guid.Empty, CancellationToken.None);

        Assert.Equal(Ui.Sync.EnterValidRunId, review.ErrorMessage);
        Assert.Equal(0, agent.StatusRequests);
    }

    /// <summary>An agent that is not there is a message, not an unhandled exception.</summary>
    [Fact]
    public async Task AnUnreachableAgentIsReported()
    {
        var agent = new FakeReviewAgent { Throws = new IOException("there is no agent") };

        var review = await Controller(agent).LoadRunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(review.Failed);
        Assert.True(agent.Disposed);
    }

    /// <summary>A run nobody is watching is not polled.</summary>
    [Fact]
    public void NothingIsPolledWithoutARun() => Assert.Null(SyncRunPolling.Interval(null));

    /// <summary>
    /// How closely a run is watched follows what the agent is doing with it.
    /// </summary>
    /// <remarks>
    /// A run being executed changes phase in seconds and is why somebody is looking at the screen.
    /// One merely open changes only if another client moves it. A finished one does not change at
    /// all, and continuing to ask is a pipe connection every five seconds for ever.
    /// </remarks>
    [Theory]
    [InlineData(SyncIpcRunPhase.AwaitingApproval, SyncIpcDispatchState.NotDispatched, 5000)]
    [InlineData(SyncIpcRunPhase.Executing, SyncIpcDispatchState.DurablyDispatched, 500)]
    [InlineData(SyncIpcRunPhase.Verifying, SyncIpcDispatchState.DurablyDispatched, 500)]
    public void AnUnfinishedRunIsWatchedAtAPaceThatSuitsIt(
        SyncIpcRunPhase phase,
        SyncIpcDispatchState dispatch,
        int expectedMilliseconds)
    {
        var interval = SyncRunPolling.Interval(Run(phase) with { DispatchState = dispatch });

        Assert.Equal(expectedMilliseconds, interval!.Value.TotalMilliseconds);
    }

    /// <summary>Every phase a run will not leave by itself stops the polling.</summary>
    [Theory]
    [InlineData(SyncIpcRunPhase.Completed)]
    [InlineData(SyncIpcRunPhase.Failed)]
    [InlineData(SyncIpcRunPhase.Cancelled)]
    [InlineData(SyncIpcRunPhase.Interrupted)]
    [InlineData(SyncIpcRunPhase.NeedsReconciliation)]
    [InlineData(SyncIpcRunPhase.BlockedConflict)]
    [InlineData(SyncIpcRunPhase.BlockedDeletionGuard)]
    [InlineData(SyncIpcRunPhase.BlockedEndpoint)]
    [InlineData(SyncIpcRunPhase.BlockedCredential)]
    [InlineData(SyncIpcRunPhase.BlockedTrust)]
    public void ASettledRunIsNotPolled(SyncIpcRunPhase phase)
    {
        Assert.True(SyncRunPolling.IsTerminal(phase));
        Assert.Null(SyncRunPolling.Interval(
            Run(phase) with { DispatchState = SyncIpcDispatchState.DurablyDispatched }));
    }

    private static SyncRunReviewController Controller(FakeReviewAgent agent) => new(() => agent);

    private static SyncRunSummary Run(SyncIpcRunPhase phase) => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, phase, SyncIpcStatusCode.None, 1,
        DateTimeOffset.UtcNow, Guid.NewGuid(), PlanDigest, ApprovalDigest, 0,
        SyncIpcDispatchState.NotDispatched, null, DateTimeOffset.UtcNow,
        0, 0, 0, LeftSnapshotComplete: true, RightSnapshotComplete: true);

    private static SyncPlanOperationSummary Operation(int sequence) => new(
        sequence, SyncIpcPlanOperationKind.Copy, Guid.NewGuid(), $"left/{sequence}",
        Guid.NewGuid(), $"right/{sequence}", 1024, IsDestructive: false);

    private static SyncConflictSummary Conflict(string path) => new(
        Guid.NewGuid(), path, "BothChanged", SyncIpcConflictState.Unresolved,
        "Both sides changed", DateTimeOffset.UtcNow, null, 1);

    private sealed class FakeReviewAgent : ISyncManagementAgentClient
    {
        private readonly Queue<(SyncRunSummary Plan, SyncPlanOperationSummary[] Operations, string? Token)> _plans = new();
        private readonly Queue<(SyncConflictSummary[] Conflicts, string? Token)> _conflicts = new();
        private readonly Queue<(SyncRunSummary[] Runs, string? Token)> _history = new();

        internal SyncRunSummary? Status { get; set; }

        internal StorageIpcFailure? StatusFailure { get; set; }

        internal SyncRunSummary? Dispatched { get; set; }

        internal bool DurablyDispatched { get; set; } = true;

        internal SyncApproveDispatchRequest? ApprovalSent { get; private set; }

        internal Exception? Throws { get; set; }

        internal int StatusRequests { get; private set; }

        internal bool Disposed { get; private set; }

        /// <param name="plan">The run whose plan identity the page claims to belong to.</param>
        internal void PlanPage(SyncRunSummary plan, SyncPlanOperationSummary[] operations, string? token) =>
            _plans.Enqueue((plan, operations, token));

        internal void ConflictPage(SyncConflictSummary[] conflicts, string? token) =>
            _conflicts.Enqueue((conflicts, token));

        internal void HistoryPage(SyncRunSummary[] runs, string? token) =>
            _history.Enqueue((runs, token));

        public Task<SyncRunStatusResponse> GetRunStatusAsync(
            SyncRunStatusRequest request, CancellationToken cancellationToken = default)
        {
            StatusRequests++;
            if (Throws is { } error) throw error;
            return Task.FromResult(new SyncRunStatusResponse(
                SyncManagementIpcContract.CurrentVersion, request.SyncRunId, Status, StatusFailure));
        }

        public Task<SyncPlanPageResponse> GetPlanPageAsync(
            SyncPlanPageRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            var page = _plans.Count > 0
                ? _plans.Dequeue()
                : (Status ?? throw new InvalidOperationException("No plan page was scripted."), [], null);

            return Task.FromResult(new SyncPlanPageResponse(
                SyncManagementIpcContract.CurrentVersion,
                request.SyncRunId,
                page.Plan.PlanId,
                page.Plan.PlanSha256,
                page.Operations.Length,
                page.Operations,
                page.Token));
        }

        public Task<SyncConflictPageResponse> GetConflictPageAsync(
            SyncConflictPageRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            var page = _conflicts.Count > 0 ? _conflicts.Dequeue() : ([], null);
            return Task.FromResult(new SyncConflictPageResponse(
                SyncManagementIpcContract.CurrentVersion,
                request.SyncRunId,
                page.Conflicts.Length,
                page.Conflicts,
                page.Token,
                IsTruncatedAtSource: false));
        }

        public Task<SyncRunListResponse> ListRunsAsync(
            SyncRunListRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            var page = _history.Count > 0 ? _history.Dequeue() : ([], null);
            return Task.FromResult(new SyncRunListResponse(
                SyncManagementIpcContract.CurrentVersion, page.Runs, page.Token));
        }

        public Task<SyncApproveDispatchResponse> ApproveAndDispatchAsync(
            SyncApproveDispatchRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            ApprovalSent = request;
            return Task.FromResult(new SyncApproveDispatchResponse(
                SyncManagementIpcContract.CurrentVersion,
                request.SyncRunId,
                DurablyDispatched,
                Dispatched));
        }

        public Task<SyncProfileListResponse> ListProfilesAsync(
            SyncProfileListRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncProfileGetResponse> GetProfileAsync(
            SyncProfileGetRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncProfileMutationResponse> CreateProfileAsync(
            SyncProfileCreateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncProfileMutationResponse> UpdateProfileAsync(
            SyncProfileUpdateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncPreviewGenerateResponse> GeneratePreviewAsync(
            SyncPreviewGenerateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
