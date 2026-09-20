using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The sync tasks screen showing what the agent actually said.
/// </summary>
/// <remarks>
/// It used to be a record of constants: three metrics reading zero, one row saying "No sync tasks
/// configured", and a footer timestamped whenever the shell started. Saving a profile changed none
/// of it, and nothing failed -- there was nothing to fail, which is the point of these.
/// </remarks>
public class SyncTasksLoadingTests
{
    [Fact]
    public async Task SavedProfilesReachTheTable()
    {
        var agent = new StubSyncAgent
        {
            Profiles =
            [
                Profile("Nightly photos", enabled: true),
                Profile("Archive", enabled: false)
            ]
        };
        var model = SyncTasksModel.Create(() => agent);

        await model.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Archive", "Nightly photos"], model.Tasks.Select(static task => task.Name));
        Assert.Equal(Ui.Sync.Enabled, model.Tasks[1].State);
        Assert.Equal(Ui.Sync.TaskDisabled, model.Tasks[0].State);
    }

    /// <summary>The cards count what was loaded rather than reading zero for ever.</summary>
    [Fact]
    public async Task TheMetricsCountWhatWasLoaded()
    {
        var agent = new StubSyncAgent
        {
            Profiles = [Profile("A", true), Profile("B", true), Profile("C", false)]
        };
        var model = SyncTasksModel.Create(() => agent);

        await model.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["2", "1", "0"], model.Metrics.Select(static metric => metric.Value));
    }

    /// <summary>
    /// A behaviour is named, not numbered.
    /// </summary>
    /// <remarks>
    /// The catalogue that names the nine behaviours used to live inside the picker control that
    /// drew them, so a table that only wanted the words had to instantiate nine buttons to get at
    /// them. It is in Desktop.Core now, which is also what the profile editor will read.
    /// </remarks>
    [Fact]
    public async Task ABehaviourIsNamedInWords()
    {
        var agent = new StubSyncAgent { Profiles = [Profile("Nightly", enabled: true)] };
        var model = SyncTasksModel.Create(() => agent);

        await model.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(model.Tasks[0].Behavior));
        Assert.DoesNotContain(model.Tasks[0].Behavior, "0123456789", StringComparison.Ordinal);
    }

    /// <summary>
    /// A run names its profile, not its profile's id.
    /// </summary>
    [Fact]
    public async Task ARunIsListedUnderItsProfilesName()
    {
        var profile = Profile("Nightly photos", enabled: true);
        var agent = new StubSyncAgent { Profiles = [profile], Runs = [Run(profile.ProfileId)] };
        var model = SyncTasksModel.Create(() => agent);

        await model.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Nightly photos", model.LastSyncs[0].Name);
    }

    /// <summary>
    /// And a run whose profile has been deleted still reads as something.
    /// </summary>
    /// <remarks>
    /// Runs outlive profiles: deleting a task does not delete its history. Falling through to the
    /// raw id would put a GUID in a column of names.
    /// </remarks>
    [Fact]
    public async Task ARunWhoseProfileIsGoneIsStillReadable()
    {
        var agent = new StubSyncAgent { Runs = [Run(Guid.NewGuid())] };
        var model = SyncTasksModel.Create(() => agent);

        await model.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Sync.UnknownProfile, model.LastSyncs[0].Name);
    }

    /// <summary>
    /// A failure says why, and does not look like an empty account.
    /// </summary>
    /// <remarks>
    /// An empty table is the same picture whether nothing is configured or nothing could be
    /// reached, and those call for very different things from the reader.
    /// </remarks>
    [Fact]
    public async Task AFailedLoadSaysWhyRatherThanShowingNothing()
    {
        var agent = new StubSyncAgent { Throws = new IOException("the agent is not listening") };
        var model = SyncTasksModel.Create(() => agent);

        await model.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(Ui.Sync.NoTasksConfigured, model.Status);
        Assert.False(string.IsNullOrWhiteSpace(model.Status));
    }

    /// <summary>Refreshing twice at once does not load twice.</summary>
    [Fact]
    public async Task ARefreshAlreadyRunningIsNotStartedAgain()
    {
        var agent = new StubSyncAgent { Profiles = [Profile("Nightly", enabled: true)] };
        var model = SyncTasksModel.Create(() => agent);

        await Task.WhenAll(
            model.RefreshAsync(TestContext.Current.CancellationToken),
            model.RefreshAsync(TestContext.Current.CancellationToken));

        Assert.Equal(1, agent.Loads);
    }

    /// <summary>A screen with no agent behind it shows its empty state and offers no refresh.</summary>
    [Fact]
    public void AModelWithNoAgentCannotBeRefreshed()
    {
        var model = SyncTasksModel.Create();

        Assert.False(model.RefreshCommand.CanExecute(null));
        Assert.Equal(Ui.Sync.NoTasksConfigured, model.Tasks.Single().Name);
    }

    private static SyncProfileSummary Profile(string name, bool enabled) => new(
        Guid.NewGuid(), name, Guid.NewGuid(), Guid.NewGuid(),
        SyncIpcDirection.TwoWay, SyncIpcDeletionMode.Mirror, enabled, 1, DateTimeOffset.UtcNow);

    private static SyncRunSummary Run(Guid profileId) => new(
        Guid.NewGuid(), profileId, 1, SyncIpcRunPhase.Completed, SyncIpcStatusCode.None, 1,
        DateTimeOffset.UtcNow, Guid.NewGuid(), new string('a', 64), new string('b', 64), 0,
        SyncIpcDispatchState.DurablyDispatched, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        0, 0, 0, LeftSnapshotComplete: true, RightSnapshotComplete: true);

    /// <summary>An agent that answers once with whatever it was given.</summary>
    private sealed class StubSyncAgent : ISyncManagementAgentClient
    {
        private bool _served;

        internal SyncProfileSummary[] Profiles { get; set; } = [];

        internal SyncRunSummary[] Runs { get; set; } = [];

        internal Exception? Throws { get; set; }

        internal int Loads { get; private set; }

        public async Task<SyncProfileListResponse> ListProfilesAsync(
            SyncProfileListRequest request, CancellationToken cancellationToken = default)
        {
            // Yields, as a call over a pipe does. A stub that answered synchronously would let a
            // refresh finish before the call that started it returned, so the guard against two
            // overlapping loads could never be reached -- and a test for it would pass whether or
            // not the guard existed.
            await Task.Yield();
            Loads++;
            if (Throws is { } error) throw error;
            return new SyncProfileListResponse(SyncManagementIpcContract.CurrentVersion, Profiles);
        }

        public Task<SyncRunListResponse> ListRunsAsync(
            SyncRunListRequest request, CancellationToken cancellationToken = default)
        {
            var runs = _served ? [] : Runs;
            _served = true;
            return Task.FromResult(new SyncRunListResponse(
                SyncManagementIpcContract.CurrentVersion, runs, null));
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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
