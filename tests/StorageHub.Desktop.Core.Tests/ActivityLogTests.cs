using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The activity log: what goes into it, in what order, and what reading it from the agent reports.
/// </summary>
public sealed class ActivityLogTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewestComesFirstAcrossBothSources()
    {
        var entries = ActivityLog.Build(
            [Transfer(Noon.AddMinutes(-5)), Transfer(Noon.AddMinutes(-1))],
            [Run(Noon.AddMinutes(-3))]);

        Assert.Equal(
            [Noon.AddMinutes(-1), Noon.AddMinutes(-3), Noon.AddMinutes(-5)],
            entries.Select(entry => entry.UpdatedUtc));
        Assert.Equal(Ui.Transfer.ActivityAreaSync, entries[1].Area);
    }

    /// <summary>
    /// Two records written in the same tick keep one order on every poll. Without the last
    /// tie-break they could trade places between polls, which on screen reads as a flicker.
    /// </summary>
    [Fact]
    public void TiesAreBrokenTheSameWayEveryTime()
    {
        var first = Transfer(Noon);
        var second = Transfer(Noon);

        var one = ActivityLog.Build([first, second], []);
        var other = ActivityLog.Build([second, first], []);

        Assert.Equal(one.Select(entry => entry.Key), other.Select(entry => entry.Key));
    }

    [Fact]
    public void TheLogIsCapped()
    {
        var transfers = Enumerable.Range(0, ActivityLog.MaximumEntries + 25)
            .Select(minute => Transfer(Noon.AddMinutes(-minute)));

        var entries = ActivityLog.Build(transfers, []);

        Assert.Equal(ActivityLog.MaximumEntries, entries.Count);
        Assert.Equal(Noon, entries[0].UpdatedUtc);
    }

    /// <summary>A transfer that failed says why, rather than where it was going.</summary>
    [Fact]
    public void AnErrorSummaryReplacesTheRoute()
    {
        var entry = ActivityLog.FromTransfer(Transfer(Noon) with { ErrorSummary = "Access denied." });

        Assert.Equal("Access denied.", entry.Details);
    }

    /// <summary>
    /// The operation and the dispatch state are words, not identifiers.
    /// </summary>
    /// <remarks>
    /// The WinForms log formatted both enums directly, so the details column said "NotDispatched"
    /// in English, Danish and German alike.
    /// </remarks>
    [Fact]
    public void EnumsAreDescribedRatherThanPrinted()
    {
        var transfer = ActivityLog.FromTransfer(Transfer(Noon));
        var run = ActivityLog.FromRun(Run(Noon));

        Assert.Contains(UiEnumNames.Name(TransferQueueOperation.Copy)!, transfer.Details, StringComparison.Ordinal);
        Assert.Contains(UiEnumNames.Name(SyncIpcDispatchState.NotDispatched)!, run.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(SyncIpcDispatchState.NotDispatched), run.Details, StringComparison.Ordinal);
        Assert.Equal(UiEnumNames.Name(TransferQueueState.Transferring), transfer.State);
    }

    [Fact]
    public void AnEmptyPathReadsAsTheRoot()
    {
        var entry = ActivityLog.FromTransfer(Transfer(Noon) with { SourcePath = string.Empty });

        Assert.Contains("/ ", entry.Details, StringComparison.Ordinal);
    }

    /// <summary>A run with conflicts says how many, which is what somebody opening the log wants.</summary>
    [Fact]
    public void ARunWithConflictsCountsThem()
    {
        var entry = ActivityLog.FromRun(Run(Noon) with { ConflictCount = 3 });

        Assert.Equal(Ui.Format(Ui.Transfer.ActivitySyncConflictsFormat, 7L, 3), entry.Details);
    }

    /// <summary>
    /// The key is what lets the screen update a row in place, so it has to be stable for a record
    /// and distinct between areas.
    /// </summary>
    [Fact]
    public void EachRecordHasAStableKey()
    {
        var transfer = Transfer(Noon);

        Assert.Equal(ActivityLog.FromTransfer(transfer).Key, ActivityLog.FromTransfer(transfer with { UpdatedUtc = Noon.AddHours(1) }).Key);
        Assert.StartsWith(ActivityLog.TransferKeyPrefix, ActivityLog.FromTransfer(transfer).Key, StringComparison.Ordinal);
        Assert.StartsWith(ActivityLog.SyncKeyPrefix, ActivityLog.FromRun(Run(Noon)).Key, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStatusLineSaysWhatIsShown()
    {
        Assert.Equal(Ui.Transfer.ActivityNone, new ActivityLogResult([], 0).Describe());
        Assert.Equal(
            Ui.Format(Ui.Transfer.ActivityShowingFormat, 1),
            new ActivityLogResult([ActivityLog.FromRun(Run(Noon))], 0).Describe());
        Assert.Equal("The agent is not running.", new ActivityLogResult([], 0, "The agent is not running.").Describe());
    }

    [Fact]
    public async Task ReadingMergesBothListingsAndClosesTheClients()
    {
        var transfers = new StubActivityTransfers { Answer = () => Transfers(Transfer(Noon)) };
        var runs = new StubActivityRuns { Answer = () => Runs(Run(Noon.AddMinutes(-1))) };

        var result = await new ActivityLogReader(() => transfers, () => runs).ReadAsync(CancellationToken.None);

        Assert.False(result.Failed);
        Assert.Equal(2, result.Entries.Count);
        Assert.True(transfers.Disposed);
        Assert.True(runs.Disposed);
    }

    /// <summary>
    /// Either listing failing fails the read. A log showing transfers and silently missing its sync
    /// runs would look complete and not be.
    /// </summary>
    [Fact]
    public async Task EitherListingFailingFailsTheRead()
    {
        var failure = new StorageIpcFailure("sync.recovering", StorageIpcFailureCategory.Conflict, "Sync is recovering.", IsTransient: true);
        var transfers = new StubActivityTransfers { Answer = () => Transfers(Transfer(Noon)) };
        var runs = new StubActivityRuns { Answer = () => Runs() with { Failure = failure } };

        var result = await new ActivityLogReader(() => transfers, () => runs).ReadAsync(CancellationToken.None);

        Assert.True(result.Failed);
        Assert.Empty(result.Entries);
        Assert.Equal("Sync is recovering.", result.Describe());
    }

    /// <summary>An agent that is not there is reported in words, not thrown.</summary>
    [Fact]
    public async Task AnAbsentAgentIsReportedRatherThanThrown()
    {
        var transfers = new StubActivityTransfers { Answer = () => throw new IOException("Pipe not found.") };
        var runs = new StubActivityRuns { Answer = () => Runs() };

        var result = await new ActivityLogReader(() => transfers, () => runs).ReadAsync(CancellationToken.None);

        Assert.True(result.Failed);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    private static TransferListResponse Transfers(params TransferQueueSummary[] transfers) =>
        new(TransferQueueIpcContract.CurrentVersion, transfers, null);

    private static SyncRunListResponse Runs(params SyncRunSummary[] runs) =>
        new(SyncManagementIpcContract.CurrentVersion, runs, null);

    private static TransferQueueSummary Transfer(DateTimeOffset updated) => new(
        Guid.NewGuid(),
        TransferQueueOperation.Copy,
        Guid.NewGuid(),
        "/photos/a.jpg",
        Guid.NewGuid(),
        "/backup/a.jpg",
        TransferQueueState.Transferring,
        Revision: 1,
        Attempt: 1,
        Priority: 0,
        ExpectedBytes: 100,
        ProgressBytes: 50,
        UpdatedUtc: updated,
        RetryAvailableUtc: null,
        ErrorCode: null,
        ErrorSummary: null,
        CanCancel: true,
        CanRetry: false,
        NeedsReconciliation: false);

    private static SyncRunSummary Run(DateTimeOffset updated) => RunTemplate with
    {
        SyncRunId = Guid.NewGuid(),
        UpdatedUtc = updated
    };
    private static readonly SyncRunSummary RunTemplate = new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Generation: 7,
        SyncIpcRunPhase.Pending,
        SyncIpcStatusCode.None,
        Revision: 1,
        UpdatedUtc: Noon,
        PlanId: Guid.NewGuid(),
        PlanSha256: string.Empty,
        ApprovalSha256: string.Empty,
        ConflictCount: 0,
        SyncIpcDispatchState.NotDispatched,
        DispatchedUtc: null,
        CreatedUtc: Noon,
        BaselineItemCount: 0,
        LeftItemCount: 0,
        RightItemCount: 0,
        LeftSnapshotComplete: true,
        RightSnapshotComplete: true);
}

/// <summary>
/// A ITransferQueueAgentClient that answers ListAsync with whatever the test gave it and refuses everything else.
/// </summary>
/// <remarks>Generated from the interface, so a method added to it is a compile error here rather than a gap.</remarks>
internal sealed class StubActivityTransfers : ITransferQueueAgentClient
{
    internal Func<TransferListResponse> Answer { get; init; } = () => throw new InvalidOperationException("No answer set.");

    internal bool Disposed { get; private set; }

    public Task<TransferEnqueueResponse> EnqueueAsync(TransferEnqueueRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls EnqueueAsync.");

    public Task<TransferListResponse> ListAsync(TransferListRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Answer());

    public Task<TransferStatusResponse> GetStatusAsync(TransferStatusRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls GetStatusAsync.");

    public Task<TransferMutationResponse> CancelAsync(TransferCancelRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls CancelAsync.");

    public Task<TransferMutationResponse> RetryAsync(TransferRetryRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls RetryAsync.");

    public Task<TransferMutationResponse> ReconcileAsync(TransferReconcileRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls ReconcileAsync.");

    public Task<TransferHistoryClearResponse> ClearHistoryAsync(TransferHistoryClearRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls ClearHistoryAsync.");

    public Task<ShellImportPlanResponse> PlanShellImportAsync(ShellImportPlanRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls PlanShellImportAsync.");

    public Task<ShellImportCommitResponse> CommitShellImportAsync(ShellImportCommitRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls CommitShellImportAsync.");

    public Task<ShellExportPrepareResponse> PrepareShellExportAsync(ShellExportPrepareRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls PrepareShellExportAsync.");

    public Task<ExplorerDropBeginResponse> BeginExplorerDropAsync(ExplorerDropBeginRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls BeginExplorerDropAsync.");

    public Task<ExplorerDropCommitResponse> CommitExplorerDropAsync(ExplorerDropCommitRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls CommitExplorerDropAsync.");

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A ISyncManagementAgentClient that answers ListRunsAsync with whatever the test gave it and refuses everything else.
/// </summary>
/// <remarks>Generated from the interface, so a method added to it is a compile error here rather than a gap.</remarks>
internal sealed class StubActivityRuns : ISyncManagementAgentClient
{
    internal Func<SyncRunListResponse> Answer { get; init; } = () => throw new InvalidOperationException("No answer set.");

    internal bool Disposed { get; private set; }

    public Task<SyncProfileListResponse> ListProfilesAsync(SyncProfileListRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls ListProfilesAsync.");

    public Task<SyncProfileGetResponse> GetProfileAsync(SyncProfileGetRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls GetProfileAsync.");

    public Task<SyncProfileMutationResponse> CreateProfileAsync(SyncProfileCreateRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls CreateProfileAsync.");

    public Task<SyncProfileMutationResponse> UpdateProfileAsync(SyncProfileUpdateRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls UpdateProfileAsync.");

    public Task<SyncPreviewGenerateResponse> GeneratePreviewAsync(SyncPreviewGenerateRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls GeneratePreviewAsync.");

    public Task<SyncRunStatusResponse> GetRunStatusAsync(SyncRunStatusRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls GetRunStatusAsync.");

    public Task<SyncRunListResponse> ListRunsAsync(SyncRunListRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Answer());

    public Task<SyncPlanPageResponse> GetPlanPageAsync(SyncPlanPageRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls GetPlanPageAsync.");

    public Task<SyncConflictPageResponse> GetConflictPageAsync(SyncConflictPageRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls GetConflictPageAsync.");

    public Task<SyncApproveDispatchResponse> ApproveAndDispatchAsync(SyncApproveDispatchRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The activity log never calls ApproveAndDispatchAsync.");

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
