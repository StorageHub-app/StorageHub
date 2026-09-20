using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Saving and previewing a sync profile, and saying why a draft will not save.
/// </summary>
/// <remarks>
/// The editor this came from was 1,153 lines of laying out fields, and its answer to every invalid
/// draft was one sentence at the bottom of the form. Most of what is here is about telling a dozen
/// different failures apart, plus the two things that make saving safe: that an update carries the
/// revision it was loaded at, and that a preview's plan belongs to the run it arrived with.
/// </remarks>
public class SyncProfileEditorControllerTests
{
    [Fact]
    public async Task ProfilesAndConnectionsAreBothLoaded()
    {
        var agent = new FakeEditorAgent
        {
            Profiles = [Summary("Nightly")],
            Connections = [Connection("Studio"), Connection("Backups")]
        };

        var workspace = await Controller(agent).LoadAsync(CancellationToken.None);

        Assert.Single(workspace.Profiles);
        Assert.Equal(2, workspace.Connections.Count);
        Assert.False(workspace.Failed);
        Assert.Equal(Ui.Format(Ui.Sync.ProfilesLoadedFormat, 1), workspace.Describe());
    }

    /// <summary>
    /// Two reads, so two failures, and the status says which one it was.
    /// </summary>
    /// <remarks>
    /// "Your saved connections did not load" and "the agent is unreachable" call for different
    /// things from whoever reads it, and the editor is still usable in the first case.
    /// </remarks>
    [Fact]
    public async Task EachHalfCanFailOnItsOwn()
    {
        var agent = new FakeEditorAgent
        {
            Profiles = [Summary("Nightly")],
            ConnectionFailure = new StorageIpcFailure(
                "storage.profile.store_unavailable",
                StorageIpcFailureCategory.Unavailable,
                "The connection store is busy.",
                IsTransient: true)
        };

        var workspace = await Controller(agent).LoadAsync(CancellationToken.None);

        Assert.Single(workspace.Profiles);
        Assert.Empty(workspace.Connections);
        Assert.True(workspace.Failed);
        Assert.Contains("busy", workspace.Describe(), StringComparison.Ordinal);
    }

    /// <summary>A profile that has never been saved is created, not updated.</summary>
    [Fact]
    public async Task ANewProfileIsCreated()
    {
        var agent = new FakeEditorAgent();

        var result = await Controller(agent).SaveAsync(null, Draft(), CancellationToken.None);

        Assert.True(result.Saved);
        Assert.NotNull(agent.Created);
        Assert.Null(agent.Updated);
    }

    /// <summary>
    /// And an edit to a saved one carries the revision it was loaded at.
    /// </summary>
    /// <remarks>
    /// The agent refuses an update made against a stale revision, which is how two people editing
    /// the same profile find out rather than one silently losing their work. Sending anything else
    /// -- the latest revision, or none -- would turn that refusal off.
    /// </remarks>
    [Fact]
    public async Task AnEditCarriesTheRevisionItWasLoadedAt()
    {
        var agent = new FakeEditorAgent();
        var current = new SyncProfileDocument(
            Guid.NewGuid(), Draft(), 7, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        _ = await Controller(agent).SaveAsync(current, Draft("Renamed"), CancellationToken.None);

        Assert.NotNull(agent.Updated);
        Assert.Equal(7, agent.Updated!.ExpectedRevision);
        Assert.Equal(current.ProfileId, agent.Updated.ProfileId);
    }

    /// <summary>A refused save is reported and nothing is claimed to have been written.</summary>
    [Fact]
    public async Task ARefusedSaveIsReported()
    {
        var agent = new FakeEditorAgent { Outcome = SyncProfileMutationOutcome.RevisionConflict };

        var result = await Controller(agent).SaveAsync(null, Draft(), CancellationToken.None);

        Assert.False(result.Saved);
        Assert.NotNull(result.ErrorMessage);
    }

    /// <summary>
    /// An agreement with no profile in it is not an agreement.
    /// </summary>
    /// <remarks>
    /// The screen shows the saved revision afterwards, and the profile is where that comes from.
    /// Reporting success without one leaves the editor claiming a revision it never received.
    /// </remarks>
    [Fact]
    public async Task ASaveWithNoProfileBackIsNotSuccess()
    {
        var agent = new FakeEditorAgent { ReturnsProfile = false };

        var result = await Controller(agent).SaveAsync(null, Draft(), CancellationToken.None);

        Assert.False(result.Saved);
        Assert.Equal(Ui.Sync.AgentNoRevision, result.ErrorMessage);
    }

    /// <summary>An invalid draft never reaches the agent.</summary>
    [Fact]
    public async Task AnInvalidDraftIsNotSent()
    {
        var agent = new FakeEditorAgent();

        var result = await Controller(agent)
            .SaveAsync(null, Draft(name: "  "), CancellationToken.None);

        Assert.True(result.Rejected);
        Assert.Null(agent.Created);
        Assert.Equal(SyncProfileFields.Name, result.Problems[0].Field);
    }

    /// <summary>
    /// Previewing saves first, because a preview is planned from the stored profile.
    /// </summary>
    /// <remarks>
    /// Previewing an unsaved edit would scan and plan the previous version and put a plan for work
    /// nobody asked for in front of the reviewer.
    /// </remarks>
    [Fact]
    public async Task PreviewingSavesTheDraftFirst()
    {
        var agent = new FakeEditorAgent();

        var result = await Controller(agent).PreviewAsync(null, Draft(), CancellationToken.None);

        Assert.NotNull(agent.Created);
        Assert.True(result.Previewed);
        Assert.NotNull(result.Profile);
    }

    /// <summary>And a draft that cannot be saved is not previewed either.</summary>
    [Fact]
    public async Task AnInvalidDraftIsNotPreviewed()
    {
        var agent = new FakeEditorAgent();

        var result = await Controller(agent)
            .PreviewAsync(null, Draft(name: string.Empty), CancellationToken.None);

        Assert.False(result.Previewed);
        Assert.True(result.Problems.Count > 0);
        Assert.Equal(0, agent.PreviewRequests);
    }

    /// <summary>
    /// A plan overview that does not belong to the run it came with is refused.
    /// </summary>
    /// <remarks>
    /// The two arrive in one response and still have to agree: the overview carries the operation
    /// counts the review screen shows, so one describing a different plan puts the wrong numbers in
    /// front of whoever approves it.
    /// </remarks>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task APlanThatDoesNotMatchItsRunIsRefused(
        bool otherRunId,
        bool otherPlanId,
        bool otherDigest)
    {
        var agent = new FakeEditorAgent { MismatchPlan = (otherRunId, otherPlanId, otherDigest) };

        var result = await Controller(agent).PreviewAsync(null, Draft(), CancellationToken.None);

        Assert.False(result.Previewed);
        Assert.Equal(Ui.Sync.PlanRunMismatch, result.ErrorMessage);
    }

    /// <summary>Opening a profile the agent does not have is a message, not an exception.</summary>
    [Fact]
    public async Task OpeningAMissingProfileIsReported()
    {
        var agent = new FakeEditorAgent { GetReturnsProfile = false };

        var (profile, error) = await Controller(agent).OpenAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(profile);
        Assert.Equal(Ui.Sync.AgentIncompleteProfile, error);
    }

    /// <summary>An unreachable agent is a message too.</summary>
    [Fact]
    public async Task AnUnreachableAgentIsReported()
    {
        var agent = new FakeEditorAgent { Throws = new IOException("there is no agent") };

        var workspace = await Controller(agent).LoadAsync(CancellationToken.None);

        Assert.True(workspace.Failed);
        Assert.Empty(workspace.Profiles);
    }

    // ---------------------------------------------------------------- the draft rules

    /// <summary>Each field answers for itself.</summary>
    [Theory]
    [InlineData("name", SyncProfileFields.Name)]
    [InlineData("locationA", SyncProfileFields.LocationA)]
    [InlineData("locationB", SyncProfileFields.LocationB)]
    [InlineData("deletionCount", SyncProfileFields.DeletionCount)]
    [InlineData("deletionPercentage", SyncProfileFields.DeletionPercentage)]
    [InlineData("transferBuffer", SyncProfileFields.TransferBuffer)]
    public void AnInvalidFieldIsNamed(string broken, string expectedField)
    {
        var draft = broken switch
        {
            "name" => Draft(name: "   "),
            "locationA" => Draft() with { LeftConnectionId = Guid.Empty },
            "locationB" => Draft() with { RightConnectionId = Guid.Empty },
            "deletionCount" => Draft() with { MaximumDeletionCount = 0 },
            "deletionPercentage" => Draft() with { MaximumDeletionPercentage = 0 },
            _ => Draft() with { TransferBufferSize = 0 }
        };

        var problems = SyncProfileDraftRules.Validate(draft);

        Assert.Contains(problems, problem => problem.Field == expectedField);
    }

    /// <summary>
    /// Two locations on one connection have to be separate folders.
    /// </summary>
    /// <remarks>
    /// Equal roots, one inside the other, and two empty roots are all the same mistake: a profile
    /// synchronising a folder with itself, which under a mirror behaviour would work its way
    /// through the whole connection.
    /// </remarks>
    [Theory]
    [InlineData("photos", "photos")]
    [InlineData("photos", "photos/2019")]
    [InlineData("photos/2019", "photos")]
    [InlineData("Photos", "photos")]
    [InlineData("", "")]
    [InlineData("/photos/", "photos")]
    public void OverlappingLocationsOnOneConnectionAreRefused(string left, string right)
    {
        var connection = Guid.NewGuid();
        var draft = Draft() with
        {
            LeftConnectionId = connection,
            LeftRoot = left,
            RightConnectionId = connection,
            RightRoot = right
        };

        var problems = SyncProfileDraftRules.Validate(draft);

        Assert.Contains(problems, problem => problem.Message == Ui.Sync.LocationsOverlap);
    }

    /// <summary>The same two folders on different connections are fine.</summary>
    [Fact]
    public void TheSameFolderOnTwoConnectionsIsAllowed()
    {
        var draft = Draft() with { LeftRoot = "photos", RightRoot = "photos" };

        Assert.Empty(SyncProfileDraftRules.Validate(draft));
    }

    /// <summary>Too many filters, and a filter that is too long, are told apart.</summary>
    [Fact]
    public void FilterLimitsAreReportedAgainstTheFilterThatBrokeThem()
    {
        var many = Draft() with
        {
            IncludeGlobs = [.. Enumerable.Range(0, SyncManagementIpcLimits.MaximumFilterCount + 1)
                .Select(static index => $"**/*.{index}")]
        };
        var oversized = Draft() with
        {
            ExcludeGlobs = [new string('x', SyncManagementIpcLimits.MaximumGlobLength + 1)]
        };

        Assert.Contains(
            SyncProfileDraftRules.Validate(many),
            problem => problem.Field == SyncProfileFields.IncludeGlobs);
        Assert.Contains(
            SyncProfileDraftRules.Validate(oversized),
            problem => problem.Field == SyncProfileFields.ExcludeGlobs);
    }

    /// <summary>
    /// Anything the contract refuses that these rules missed is still refused.
    /// </summary>
    /// <remarks>
    /// The rules restate the contract rather than call it, so they can drift. This is the backstop
    /// that keeps a drift from becoming a draft the screen offers to save and the agent rejects.
    /// </remarks>
    [Fact]
    public void AControlCharacterAnywhereIsStillRefused()
    {
        var draft = Draft(name: "Nightly\u0007photos");

        Assert.NotEmpty(SyncProfileDraftRules.Validate(draft));
    }

    /// <summary>A draft with nothing wrong with it has nothing reported.</summary>
    [Fact]
    public void AGoodDraftHasNoProblems() => Assert.Empty(SyncProfileDraftRules.Validate(Draft()));

    private static SyncProfileEditorController Controller(FakeEditorAgent agent) =>
        new(() => agent, () => agent);

    private static SyncProfileDraftDocument Draft(string name = "Nightly photos") => new(
        name,
        Guid.NewGuid(),
        "left",
        Guid.NewGuid(),
        "right",
        SyncIpcBehavior.UpdateAToB,
        SyncIpcConflictPolicy.Block,
        includeGlobs: [],
        excludeGlobs: [".storagehub"],
        includeHiddenFiles: true,
        maximumDeletionCount: 100,
        maximumDeletionPercentage: 10m,
        transferBufferSize: 64 * 1024,
        enabled: true);

    private static SyncProfileSummary Summary(string name) => new(
        Guid.NewGuid(), name, Guid.NewGuid(), Guid.NewGuid(),
        SyncIpcDirection.LeftToRight, SyncIpcDeletionMode.Disabled, true, 1, DateTimeOffset.UtcNow);

    private static ConnectionSummary Connection(string name) => new(
        Guid.NewGuid(), name, StorageConnectionProvider.Local, null, [], false, true,
        null, null, 1);

    /// <summary>
    /// The agent, scripted by the test.
    /// </summary>
    /// <remarks>
    /// One object standing in for both clients, because the editor asks each exactly once and
    /// keeping the two scripts together is what makes a half-failed load easy to set up.
    /// </remarks>
    private sealed class FakeEditorAgent : ISyncManagementAgentClient, IRemoteStorageAgentClient
    {
        internal SyncProfileSummary[] Profiles { get; set; } = [];

        internal ConnectionSummary[] Connections { get; set; } = [];

        internal StorageIpcFailure? ProfileFailure { get; set; }

        internal StorageIpcFailure? ConnectionFailure { get; set; }

        internal SyncProfileMutationOutcome Outcome { get; set; } = SyncProfileMutationOutcome.Succeeded;

        internal bool ReturnsProfile { get; set; } = true;

        internal bool GetReturnsProfile { get; set; } = true;

        internal (bool RunId, bool PlanId, bool Digest) MismatchPlan { get; set; }

        internal Exception? Throws { get; set; }

        internal SyncProfileCreateRequest? Created { get; private set; }

        internal SyncProfileUpdateRequest? Updated { get; private set; }

        internal int PreviewRequests { get; private set; }

        public Task<SyncProfileListResponse> ListProfilesAsync(
            SyncProfileListRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            return Task.FromResult(new SyncProfileListResponse(
                SyncManagementIpcContract.CurrentVersion, Profiles, ProfileFailure));
        }

        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            return Task.FromResult(new ConnectionListResponse(
                StorageIpcContract.CurrentVersion, Connections, ConnectionFailure));
        }

        public Task<SyncProfileGetResponse> GetProfileAsync(
            SyncProfileGetRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            return Task.FromResult(new SyncProfileGetResponse(
                SyncManagementIpcContract.CurrentVersion,
                request.ProfileId,
                GetReturnsProfile ? Document(request.ProfileId, Draft()) : null));
        }

        public Task<SyncProfileMutationResponse> CreateProfileAsync(
            SyncProfileCreateRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            Created = request;
            return Task.FromResult(Mutation(request.ProfileId, request.Draft));
        }

        public Task<SyncProfileMutationResponse> UpdateProfileAsync(
            SyncProfileUpdateRequest request, CancellationToken cancellationToken = default)
        {
            if (Throws is { } error) throw error;
            Updated = request;
            return Task.FromResult(Mutation(request.ProfileId, request.Draft));
        }

        public Task<SyncPreviewGenerateResponse> GeneratePreviewAsync(
            SyncPreviewGenerateRequest request, CancellationToken cancellationToken = default)
        {
            PreviewRequests++;
            if (Throws is { } error) throw error;

            var run = new SyncRunSummary(
                Guid.NewGuid(), request.ProfileId, 1, SyncIpcRunPhase.AwaitingApproval,
                SyncIpcStatusCode.None, 1, DateTimeOffset.UtcNow, Guid.NewGuid(),
                new string('a', 64), new string('b', 64), 0, SyncIpcDispatchState.NotDispatched,
                null, DateTimeOffset.UtcNow, 0, 0, 0, true, true);

            var plan = new SyncPlanOverview(
                MismatchPlan.RunId ? Guid.NewGuid() : run.SyncRunId,
                MismatchPlan.PlanId ? Guid.NewGuid() : run.PlanId,
                MismatchPlan.Digest ? new string('c', 64) : run.PlanSha256,
                1, 4, 4, 0, 0, DateTimeOffset.UtcNow);

            return Task.FromResult(new SyncPreviewGenerateResponse(
                SyncManagementIpcContract.CurrentVersion, request.ProfileId, run, plan));
        }

        private SyncProfileMutationResponse Mutation(Guid profileId, SyncProfileDraftDocument draft) =>
            new(SyncManagementIpcContract.CurrentVersion,
                profileId,
                Outcome,
                ReturnsProfile && Outcome is SyncProfileMutationOutcome.Succeeded
                    or SyncProfileMutationOutcome.AlreadyApplied
                    ? Document(profileId, draft)
                    : null,
                Outcome == SyncProfileMutationOutcome.RevisionConflict ? 9 : null,
                Outcome is SyncProfileMutationOutcome.Succeeded or SyncProfileMutationOutcome.AlreadyApplied
                    ? null
                    : new StorageIpcFailure(
                        "sync.profile.revision_conflict",
                        StorageIpcFailureCategory.Conflict,
                        "The profile changed while you were editing it.",
                        IsTransient: false));

        private static SyncProfileDocument Document(Guid profileId, SyncProfileDraftDocument draft) =>
            new(profileId, draft, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

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

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
