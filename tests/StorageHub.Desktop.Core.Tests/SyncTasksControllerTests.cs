using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Loading the sync tasks screen.
/// </summary>
/// <remarks>
/// The first piece of sync to leave the WinForms shell, where this lived inside six hundred lines of
/// <c>TableLayoutPanel</c> and could not be reached without a window. The screen itself is a table
/// of names; what can actually be wrong is the paging behind it, and that is what is here.
/// </remarks>
public class SyncTasksControllerTests
{
    [Fact]
    public async Task ProfilesAreCountedByWhetherTheyAreEnabled()
    {
        var agent = new FakeSyncAgent
        {
            Profiles = [Profile("Nightly", enabled: true), Profile("Archive", enabled: false),
                Profile("Photos", enabled: true)]
        };

        var snapshot = await new SyncTasksController(() => agent).LoadAsync(CancellationToken.None);

        Assert.Equal(2, snapshot.EnabledCount);
        Assert.Equal(1, snapshot.DisabledCount);
        Assert.False(snapshot.Failed);
    }

    /// <summary>History arrives a page at a time, and every page is kept.</summary>
    [Fact]
    public async Task RunHistoryIsPagedThroughToTheEnd()
    {
        var agent = new FakeSyncAgent();
        agent.Page([Run(), Run()], "1");
        agent.Page([Run()], null);

        var snapshot = await new SyncTasksController(() => agent).LoadAsync(CancellationToken.None);

        Assert.Equal(3, snapshot.Runs.Count);
    }

    /// <summary>
    /// A cap on how much history the screen holds.
    /// </summary>
    /// <remarks>
    /// This screen shows recent runs; the review screen looks a particular one up by id. A profile
    /// that has run every ten minutes for a year would otherwise have the screen page through tens
    /// of thousands of rows before drawing anything.
    /// </remarks>
    [Fact]
    public async Task HistoryStopsAtTheCapEvenWhenMoreIsOffered()
    {
        var agent = new FakeSyncAgent();
        for (var page = 0; page < 10; page++)
        {
            agent.Page(
                [.. Enumerable.Range(0, 100).Select(static _ => Run())],
                (page + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var snapshot = await new SyncTasksController(() => agent).LoadAsync(CancellationToken.None);

        Assert.Equal(200, snapshot.Runs.Count);

        // And it stopped asking rather than reading every page and throwing most away.
        Assert.Equal(2, agent.RunRequests);
    }

    /// <summary>
    /// An agent that stops advancing is not followed round for ever.
    /// </summary>
    /// <remarks>
    /// A continuation token that comes back the same as the one just sent is a loop, and following
    /// it against a live pipe hangs the screen rather than failing it -- which is much worse, since
    /// a hang has nothing to report and no way out.
    /// </remarks>
    [Fact]
    public async Task ARepeatedContinuationTokenStopsTheLoad()
    {
        var agent = new FakeSyncAgent();
        agent.Page([Run()], "7");
        agent.Page([Run()], "7");
        agent.Page([Run()], "7");

        var snapshot = await new SyncTasksController(() => agent).LoadAsync(CancellationToken.None);

        Assert.Equal(Ui.Sync.RepeatedHistoryToken, snapshot.ErrorMessage);
    }

    /// <summary>A refused listing is reported, not thrown.</summary>
    [Fact]
    public async Task AFailedProfileListingIsCarriedOnTheSnapshot()
    {
        var agent = new FakeSyncAgent
        {
            ProfileFailure = new StorageIpcFailure(
                "sync.profiles.unavailable",
                StorageIpcFailureCategory.Unavailable,
                "The sync store is locked.",
                IsTransient: true)
        };

        var snapshot = await new SyncTasksController(() => agent).LoadAsync(CancellationToken.None);

        Assert.True(snapshot.Failed);
        Assert.Equal("The sync store is locked.", snapshot.ErrorMessage);
        Assert.Empty(snapshot.Profiles);
    }

    /// <summary>
    /// And history that fails still leaves the profiles on screen.
    /// </summary>
    /// <remarks>
    /// The two come from different calls and a person came to this screen to see their tasks.
    /// Throwing the profiles away because the history could not be read would replace a partial
    /// answer with none.
    /// </remarks>
    [Fact]
    public async Task ProfilesSurviveAHistoryFailure()
    {
        var agent = new FakeSyncAgent { Profiles = [Profile("Nightly", enabled: true)] };
        agent.PageFailure(new StorageIpcFailure(
            "sync.runs.unavailable",
            StorageIpcFailureCategory.Unavailable,
            "The run store is busy.",
            IsTransient: true));

        var snapshot = await new SyncTasksController(() => agent).LoadAsync(CancellationToken.None);

        Assert.Single(snapshot.Profiles);
        Assert.Equal("The run store is busy.", snapshot.ErrorMessage);
    }

    /// <summary>An agent that is not there is a message rather than an unhandled exception.</summary>
    [Fact]
    public async Task AnUnreachableAgentIsReported()
    {
        var agent = new FakeSyncAgent { Throws = new IOException("there is no agent") };

        var snapshot = await new SyncTasksController(() => agent).LoadAsync(CancellationToken.None);

        Assert.True(snapshot.Failed);
        Assert.Empty(snapshot.Profiles);
    }

    /// <summary>The client is disposed whichever way the load ended.</summary>
    [Fact]
    public async Task TheClientIsClosedAfterALoad()
    {
        var agent = new FakeSyncAgent { Throws = new IOException("there is no agent") };

        _ = await new SyncTasksController(() => agent).LoadAsync(CancellationToken.None);

        Assert.True(agent.Disposed);
    }

    private static SyncProfileSummary Profile(string name, bool enabled) => new(
        Guid.NewGuid(), name, Guid.NewGuid(), Guid.NewGuid(),
        SyncIpcDirection.TwoWay, SyncIpcDeletionMode.Mirror, enabled, 1,
        DateTimeOffset.UtcNow);

    private static SyncRunSummary Run() => new(
        Guid.NewGuid(), Guid.NewGuid(), 1, SyncIpcRunPhase.Completed, SyncIpcStatusCode.None, 1,
        DateTimeOffset.UtcNow, Guid.NewGuid(), new string('a', 64), new string('b', 64), 0,
        SyncIpcDispatchState.DurablyDispatched, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        0, 0, 0, LeftSnapshotComplete: true, RightSnapshotComplete: true);

    private sealed class FakeSyncAgent : ISyncManagementAgentClient
    {
        private readonly Queue<(SyncRunSummary[] Runs, string? Token, StorageIpcFailure? Failure)> _pages = new();

        internal SyncProfileSummary[] Profiles { get; set; } = [];

        internal StorageIpcFailure? ProfileFailure { get; set; }

        internal Exception? Throws { get; set; }

        internal int RunRequests { get; private set; }

        internal bool Disposed { get; private set; }

        internal void Page(SyncRunSummary[] runs, string? token) => _pages.Enqueue((runs, token, null));

        internal void PageFailure(StorageIpcFailure failure) => _pages.Enqueue(([], null, failure));

        public Task<SyncProfileListResponse> ListProfilesAsync(
            SyncProfileListRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            return Task.FromResult(new SyncProfileListResponse(
                SyncManagementIpcContract.CurrentVersion, Profiles, ProfileFailure));
        }

        public Task<SyncRunListResponse> ListRunsAsync(
            SyncRunListRequest request, CancellationToken cancellationToken = default)
        {
            RunRequests++;
            if (!_pages.TryDequeue(out var page))
            {
                return Task.FromResult(new SyncRunListResponse(
                    SyncManagementIpcContract.CurrentVersion, [], null));
            }

            return Task.FromResult(new SyncRunListResponse(
                SyncManagementIpcContract.CurrentVersion, page.Runs, page.Token, page.Failure));
        }

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

        public Task<SyncRunStatusResponse> GetRunStatusAsync(
            SyncRunStatusRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncPlanPageResponse> GetPlanPageAsync(
            SyncPlanPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncConflictPageResponse> GetConflictPageAsync(
            SyncConflictPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SyncApproveDispatchResponse> ApproveAndDispatchAsync(
            SyncApproveDispatchRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
