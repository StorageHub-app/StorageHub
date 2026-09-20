using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Listing, saving, enabling and deleting sync schedules.
/// </summary>
/// <remarks>
/// A schedule runs unattended, so the things worth checking are the ones nobody is present for:
/// that a change carries the revision it was read at, that an outcome is only success when the
/// agent says so, and that "a run is in progress" is reported as that and not as a breakage.
/// </remarks>
public class ScheduleManagerControllerTests
{
    [Fact]
    public async Task SchedulesAndProfilesAreBothLoaded()
    {
        var agent = new FakeScheduleAgent
        {
            Schedules = [Schedule("Nightly photos")],
            Profiles = [Profile("Nightly photos")]
        };

        var workspace = await Controller(agent).LoadAsync(CancellationToken.None);

        Assert.Single(workspace.Schedules);
        Assert.Single(workspace.Profiles);
        Assert.False(workspace.Failed);
        Assert.Equal(Ui.Format(Ui.Schedules.SchedulesLoadedFormat, 1), workspace.Describe());
    }

    /// <summary>Nothing saved yet says so rather than reading as an empty failure.</summary>
    [Fact]
    public async Task NoSchedulesSaysSo()
    {
        var workspace = await Controller(new FakeScheduleAgent()).LoadAsync(CancellationToken.None);

        Assert.Equal(Ui.Schedules.NoSchedulesYet, workspace.Describe());
        Assert.False(workspace.Failed);
    }

    /// <summary>A new schedule is created; an existing one is updated at its revision.</summary>
    [Fact]
    public async Task ANewScheduleIsCreatedAndAnEditIsAnUpdate()
    {
        var agent = new FakeScheduleAgent();
        var controller = Controller(agent);

        _ = await controller.SaveAsync(null, Draft(), CancellationToken.None);
        Assert.NotNull(agent.Created);
        Assert.Null(agent.Updated);

        var current = Schedule("Nightly photos") with { Revision = 5 };
        _ = await controller.SaveAsync(current, Draft(), CancellationToken.None);

        Assert.Equal(5, agent.Updated!.ExpectedRevision);
        Assert.Equal(current.ScheduleId, agent.Updated.ScheduleId);
    }

    /// <summary>Enabling and deleting carry the revision too.</summary>
    [Fact]
    public async Task EveryChangeCarriesTheRevisionItWasReadAt()
    {
        var agent = new FakeScheduleAgent();
        var current = Schedule("Nightly photos") with { Revision = 11 };
        var controller = Controller(agent);

        _ = await controller.SetEnabledAsync(current, enabled: false, CancellationToken.None);
        _ = await controller.DeleteAsync(current, CancellationToken.None);

        Assert.Equal(11, agent.Enabled!.ExpectedRevision);
        Assert.Equal(11, agent.Deleted!.ExpectedRevision);
    }

    /// <summary>
    /// A schedule that is running now is busy, not broken.
    /// </summary>
    /// <remarks>
    /// The agent refuses a destructive change while an occurrence is in flight, and the answer is
    /// to try again in a minute. "The schedule could not be changed" reads as something to
    /// investigate, and is how somebody ends up editing the store by hand.
    /// </remarks>
    [Fact]
    public async Task ARefusalWhileARunIsActiveSaysSo()
    {
        var agent = new FakeScheduleAgent { Outcome = ScheduleMutationOutcome.ActiveRun };

        var result = await Controller(agent)
            .DeleteAsync(Schedule("Nightly photos"), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Equal(Ui.Schedules.ActiveRunsBlock, result.ErrorMessage);
    }

    /// <summary>A schedule somebody else deleted is reported as gone, not as a failure to parse.</summary>
    [Fact]
    public async Task AMissingScheduleSaysSo()
    {
        var agent = new FakeScheduleAgent { Outcome = ScheduleMutationOutcome.NotFound };

        var result = await Controller(agent)
            .SetEnabledAsync(Schedule("Nightly photos"), true, CancellationToken.None);

        Assert.Equal(Ui.Schedules.SelectScheduleFirst, result.ErrorMessage);
    }

    /// <summary>
    /// AlreadyApplied is success.
    /// </summary>
    /// <remarks>
    /// It means the agent recognised this exact change at this exact revision and did not write it
    /// twice -- which is what a retry after a dropped connection looks like.
    /// </remarks>
    [Fact]
    public async Task AnAlreadyAppliedChangeIsSuccess()
    {
        var agent = new FakeScheduleAgent { Outcome = ScheduleMutationOutcome.AlreadyApplied };

        var result = await Controller(agent).SaveAsync(null, Draft(), CancellationToken.None);

        Assert.True(result.Changed);
        Assert.NotNull(result.Schedule);
    }

    /// <summary>
    /// An accepted change with no schedule back is not accepted.
    /// </summary>
    /// <remarks>
    /// The screen shows the new revision and the next occurrence, and both come from the document.
    /// Reporting success without one leaves the manager showing the values from before the change
    /// as though they were after it -- including a "next run" that has not moved.
    /// </remarks>
    [Fact]
    public async Task AChangeWithNoScheduleBackIsNotSuccess()
    {
        var agent = new FakeScheduleAgent { ReturnsSchedule = false };

        var result = await Controller(agent).SaveAsync(null, Draft(), CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Equal(Ui.Schedules.NoScheduleRevision, result.ErrorMessage);
    }

    /// <summary>
    /// A delete answers with no schedule, and that is correct.
    /// </summary>
    /// <remarks>
    /// The other two changes prove themselves by handing back the document. A delete cannot, so it
    /// is judged on the outcome alone -- which is why it does not share their check.
    /// </remarks>
    [Fact]
    public async Task ADeleteNeedsNoScheduleBack()
    {
        var agent = new FakeScheduleAgent { ReturnsSchedule = false };

        var result = await Controller(agent)
            .DeleteAsync(Schedule("Nightly photos"), CancellationToken.None);

        Assert.True(result.Changed);
        Assert.Null(result.Schedule);
    }

    /// <summary>A draft the agent would refuse never leaves the desktop.</summary>
    [Fact]
    public async Task AnInvalidDraftIsNotSent()
    {
        var agent = new FakeScheduleAgent();

        var result = await Controller(agent)
            .SaveAsync(null, Draft() with { CronExpression = "   " }, CancellationToken.None);

        Assert.False(result.Changed);
        Assert.Equal(Ui.Schedules.CronHint, result.ErrorMessage);
        Assert.Null(agent.Created);
    }

    /// <summary>An unreachable agent is a message, not an unhandled exception.</summary>
    [Fact]
    public async Task AnUnreachableAgentIsReported()
    {
        var agent = new FakeScheduleAgent { Throws = new IOException("there is no agent") };

        var workspace = await Controller(agent).LoadAsync(CancellationToken.None);

        Assert.True(workspace.Failed);
        Assert.Empty(workspace.Schedules);
    }

    private static ScheduleManagerController Controller(FakeScheduleAgent agent) =>
        new(() => agent, () => agent);

    private static ScheduleDraftDocument Draft() => new(
        Guid.NewGuid(), "0 2 * * *", "UTC", 900, QueueOneWhileRunning: true, Enabled: true);

    private static ScheduleDocument Schedule(string profileName) => new(
        Guid.NewGuid(), Guid.NewGuid(), profileName, "0 2 * * *", "UTC", 900,
        QueueOneWhileRunning: true, Enabled: true, NextOccurrenceUtc: DateTimeOffset.UtcNow.AddHours(4),
        QueuedOccurrenceUtc: null, IsBusy: false, LastRunOutcome: null, LastErrorCode: null,
        Revision: 1);

    private static SyncProfileSummary Profile(string name) => new(
        Guid.NewGuid(), name, Guid.NewGuid(), Guid.NewGuid(),
        SyncIpcDirection.LeftToRight, SyncIpcDeletionMode.Disabled, true, 1, DateTimeOffset.UtcNow);

    /// <summary>Both agent surfaces, scripted by the test.</summary>
    private sealed class FakeScheduleAgent
        : IScheduleManagementAgentClient, ISyncManagementAgentClient
    {
        internal ScheduleDocument[] Schedules { get; set; } = [];

        internal SyncProfileSummary[] Profiles { get; set; } = [];

        internal ScheduleMutationOutcome Outcome { get; set; } = ScheduleMutationOutcome.Succeeded;

        internal bool ReturnsSchedule { get; set; } = true;

        internal Exception? Throws { get; set; }

        internal ScheduleCreateRequest? Created { get; private set; }

        internal ScheduleUpdateRequest? Updated { get; private set; }

        internal ScheduleSetEnabledRequest? Enabled { get; private set; }

        internal ScheduleDeleteRequest? Deleted { get; private set; }

        public Task<ScheduleListResponse> ListAsync(
            ScheduleListRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            return Task.FromResult(new ScheduleListResponse(
                ScheduleManagementIpcContract.CurrentVersion, Schedules));
        }

        public Task<ScheduleGetResponse> GetAsync(
            ScheduleGetRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScheduleGetResponse(
                ScheduleManagementIpcContract.CurrentVersion,
                request.ScheduleId,
                Schedules.FirstOrDefault()));

        public Task<ScheduleMutationResponse> CreateAsync(
            ScheduleCreateRequest request, CancellationToken cancellationToken = default)
        {
            Created = request;
            return Task.FromResult(Mutation(request.ScheduleId));
        }

        public Task<ScheduleMutationResponse> UpdateAsync(
            ScheduleUpdateRequest request, CancellationToken cancellationToken = default)
        {
            Updated = request;
            return Task.FromResult(Mutation(request.ScheduleId));
        }

        public Task<ScheduleMutationResponse> SetEnabledAsync(
            ScheduleSetEnabledRequest request, CancellationToken cancellationToken = default)
        {
            Enabled = request;
            return Task.FromResult(Mutation(request.ScheduleId));
        }

        public Task<ScheduleMutationResponse> DeleteAsync(
            ScheduleDeleteRequest request, CancellationToken cancellationToken = default)
        {
            Deleted = request;
            return Task.FromResult(Mutation(request.ScheduleId));
        }

        public Task<SyncProfileListResponse> ListProfilesAsync(
            SyncProfileListRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            return Task.FromResult(new SyncProfileListResponse(
                SyncManagementIpcContract.CurrentVersion, Profiles));
        }

        private ScheduleMutationResponse Mutation(Guid scheduleId) => new(
            ScheduleManagementIpcContract.CurrentVersion,
            scheduleId,
            Outcome,
            ReturnsSchedule ? Schedule("Nightly photos") with { ScheduleId = scheduleId } : null);

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
