using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>One reason a draft cannot be saved, and which field it is about.</summary>
/// <param name="Field">
/// A key from <see cref="SyncProfileFields"/>, or <see cref="SyncProfileFields.Draft"/> when the
/// draft as a whole is what the agent would refuse.
/// </param>
internal sealed record SyncDraftProblem(string Field, string Message);

/// <summary>The parts of a sync profile a problem can be about.</summary>
internal static class SyncProfileFields
{
    internal const string Name = "name";
    internal const string LocationA = "locationA";
    internal const string LocationB = "locationB";
    internal const string DeletionCount = "deletionCount";
    internal const string DeletionPercentage = "deletionPercentage";
    internal const string TransferBuffer = "transferBuffer";
    internal const string IncludeGlobs = "includeGlobs";
    internal const string ExcludeGlobs = "excludeGlobs";

    /// <summary>Nothing more specific: the agent would refuse it and we cannot say where.</summary>
    internal const string Draft = "draft";
}

/// <summary>
/// Why the agent would refuse a draft, said one field at a time.
/// </summary>
/// <remarks>
/// <para>
/// The contract answers this with a single <c>HasValidV2Bounds</c> boolean, and the WinForms editor
/// turned every one of a dozen different failures into the same sentence -- "Choose two locations
/// that do not overlap, and keep the limits and filters within range" -- printed at the bottom of a
/// form with twelve fields on it. A name left blank and a buffer size of zero read identically.
/// </para>
/// <para>
/// These checks restate the contract's rules rather than call them, which risks drift, so the last
/// thing this does is ask the contract. If the contract refuses a draft these rules called good,
/// the draft is still reported as bad -- with the old general sentence, which is no worse than
/// before -- so the two can disagree without the screen ever offering to save something the agent
/// will reject.
/// </para>
/// </remarks>
internal static class SyncProfileDraftRules
{
    internal static IReadOnlyList<SyncDraftProblem> Validate(SyncProfileDraftDocument draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var problems = new List<SyncDraftProblem>();

        if (string.IsNullOrWhiteSpace(draft.DisplayName))
        {
            problems.Add(new SyncDraftProblem(SyncProfileFields.Name, Ui.Sync.ProfileNameRequired));
        }
        else if (!IsSafeText(draft.DisplayName, SyncManagementIpcLimits.MaximumDisplayNameLength))
        {
            problems.Add(new SyncDraftProblem(
                SyncProfileFields.Name,
                Ui.Format(Ui.Sync.TextTooLongFormat, SyncManagementIpcLimits.MaximumDisplayNameLength)));
        }

        Location(draft.LocationAConnectionId, draft.LocationARoot, SyncProfileFields.LocationA);
        Location(draft.LocationBConnectionId, draft.LocationBRoot, SyncProfileFields.LocationB);

        // Only worth saying once both locations are actually chosen; before that the overlap is a
        // consequence of the blanks rather than a second thing to fix.
        if (draft.LocationAConnectionId != Guid.Empty &&
            draft.LocationAConnectionId == draft.LocationBConnectionId &&
            Overlap(draft.LocationARoot, draft.LocationBRoot))
        {
            problems.Add(new SyncDraftProblem(
                SyncProfileFields.LocationB, Ui.Sync.LocationsOverlap));
        }

        if (draft.MaximumDeletionCount is < 1 or > SyncManagementIpcLimits.MaximumDeletionCount)
        {
            problems.Add(new SyncDraftProblem(
                SyncProfileFields.DeletionCount,
                Ui.Format(Ui.Sync.RangeFormat, 1, SyncManagementIpcLimits.MaximumDeletionCount)));
        }

        if (draft.MaximumDeletionPercentage is <= 0 or > 100)
        {
            problems.Add(new SyncDraftProblem(
                SyncProfileFields.DeletionPercentage, Ui.Format(Ui.Sync.RangeFormat, 1, 100)));
        }

        if (draft.TransferBufferSize is < 1 or > SyncManagementIpcLimits.MaximumTransferBufferSize)
        {
            problems.Add(new SyncDraftProblem(
                SyncProfileFields.TransferBuffer,
                Ui.Format(
                    Ui.Sync.RangeFormat, 1, SyncManagementIpcLimits.MaximumTransferBufferSize)));
        }

        Globs(draft.IncludeGlobs, SyncProfileFields.IncludeGlobs);
        Globs(draft.ExcludeGlobs, SyncProfileFields.ExcludeGlobs);

        // The contract has the last word. If it refuses something these rules let through, the
        // screen must not offer to save it -- the general sentence is what 1.x always showed.
        if (problems.Count == 0 && !draft.HasValidV2Bounds)
        {
            problems.Add(new SyncDraftProblem(
                SyncProfileFields.Draft, Ui.Sync.CompleteLocationsHint));
        }

        return problems;

        void Location(Guid connectionId, string root, string field)
        {
            if (connectionId == Guid.Empty)
            {
                problems.Add(new SyncDraftProblem(field, Ui.Sync.LocationConnectionRequired));
            }
            else if (!IsSafeText(root, SyncManagementIpcLimits.MaximumRelativeRootLength, allowEmpty: true))
            {
                problems.Add(new SyncDraftProblem(
                    field,
                    Ui.Format(
                        Ui.Sync.TextTooLongFormat,
                        SyncManagementIpcLimits.MaximumRelativeRootLength)));
            }
        }

        void Globs(string[] globs, string field)
        {
            if (globs.Length > SyncManagementIpcLimits.MaximumFilterCount)
            {
                problems.Add(new SyncDraftProblem(
                    field,
                    Ui.Format(Ui.Sync.TooManyFiltersFormat, SyncManagementIpcLimits.MaximumFilterCount)));
                return;
            }

            if (!globs.All(static glob =>
                IsSafeText(glob, SyncManagementIpcLimits.MaximumGlobLength) &&
                !string.IsNullOrWhiteSpace(glob)))
            {
                problems.Add(new SyncDraftProblem(
                    field,
                    Ui.Format(Ui.Sync.TextTooLongFormat, SyncManagementIpcLimits.MaximumGlobLength)));
            }
        }
    }

    /// <summary>
    /// Whether two roots on the same connection name the same folder, or one inside the other.
    /// </summary>
    /// <remarks>
    /// Mirrors the contract's own rule, including that two empty roots overlap: both would mean
    /// the connection root, and a profile synchronising a folder with itself would mirror or delete
    /// its way through the whole connection.
    /// </remarks>
    private static bool Overlap(string a, string b)
    {
        var left = a.Trim('/');
        var right = b.Trim('/');
        return left.Length == 0 || right.Length == 0 ||
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
            left.StartsWith(right + "/", StringComparison.OrdinalIgnoreCase) ||
            right.StartsWith(left + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeText(string? value, int maximumLength, bool allowEmpty = false) =>
        value is not null &&
        (allowEmpty || value.Length > 0) &&
        value.Length <= maximumLength &&
        !value.Any(char.IsControl);
}

/// <summary>
/// What the sync profile editor knows after it has asked the agent.
/// </summary>
/// <remarks>
/// Two independent reads, so two independent failures. The editor is still usable with the
/// connections but no profile list, and saying which one went missing is the difference between
/// "the agent is unreachable" and "your connections did not load".
/// </remarks>
internal sealed record SyncProfileWorkspace(
    IReadOnlyList<SyncProfileSummary> Profiles,
    IReadOnlyList<ConnectionSummary> Connections,
    string? ProfileError = null,
    string? ConnectionError = null)
{
    internal static SyncProfileWorkspace Empty { get; } = new([], []);

    internal bool Failed => ProfileError is not null || ConnectionError is not null;

    /// <summary>What to put in the status line, in the words that fit which half failed.</summary>
    internal string Describe() => (ProfileError, ConnectionError) switch
    {
        (null, null) => Profiles.Count == 0
            ? Ui.Sync.NoSavedProfileYet
            : Ui.Format(Ui.Sync.ProfilesLoadedFormat, Profiles.Count),
        (not null, null) => Ui.Format(Ui.Sync.ProfilesUnavailableFormat, ProfileError),
        (null, not null) => Ui.Format(Ui.Sync.ConnectionsUnavailableFormat, ConnectionError),
        _ => Ui.Format(Ui.Sync.BothUnavailableFormat, ProfileError, ConnectionError)
    };
}

/// <summary>The result of saving, or of trying to.</summary>
internal sealed record SyncProfileSaveResult(
    SyncProfileDocument? Profile,
    IReadOnlyList<SyncDraftProblem> Problems,
    string? ErrorMessage = null)
{
    internal bool Saved => Profile is not null;

    internal bool Rejected => Problems.Count > 0;
}

/// <summary>The result of asking the agent to scan and plan.</summary>
internal sealed record SyncPreviewResult(
    SyncProfileDocument? Profile,
    SyncRunSummary? Run,
    IReadOnlyList<SyncDraftProblem> Problems,
    string? ErrorMessage = null)
{
    internal bool Previewed => Run is not null;
}

/// <summary>
/// Loads, saves and previews sync profiles.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>SyncProfileEditorForm</c>, 1,153 lines of which perhaps a hundred and fifty
/// were this. What is here is the part that can be wrong: which of create and update to send, that
/// an update carries the revision it was loaded at so a concurrent edit is refused rather than
/// overwritten, and that a preview's plan really belongs to the run it came back with.
/// </para>
/// <para>
/// A client per call, as the rest of the shell does, because the agent restarts.
/// </para>
/// </remarks>
internal sealed class SyncProfileEditorController(
    Func<ISyncManagementAgentClient> syncClients,
    Func<IRemoteStorageAgentClient> storageClients)
{
    private readonly Func<ISyncManagementAgentClient> _syncClients =
        syncClients ?? throw new ArgumentNullException(nameof(syncClients));

    private readonly Func<IRemoteStorageAgentClient> _storageClients =
        storageClients ?? throw new ArgumentNullException(nameof(storageClients));

    /// <summary>Reads the saved profiles and the connections they can point at.</summary>
    internal async Task<SyncProfileWorkspace> LoadAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);
        var connections = await ReadConnectionsAsync(cancellationToken).ConfigureAwait(false);

        return new SyncProfileWorkspace(
            profiles.Profiles, connections.Connections, profiles.ErrorMessage, connections.ErrorMessage);
    }

    /// <summary>Reads one saved profile in full, including the parts the list does not carry.</summary>
    internal async Task<(SyncProfileDocument? Profile, string? ErrorMessage)> OpenAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty) return (null, Ui.Sync.ProfileNotFound);

        await using var client = _syncClients();
        try
        {
            var response = await client.GetProfileAsync(
                new SyncProfileGetRequest(SyncManagementIpcContract.CurrentVersion, profileId),
                cancellationToken).ConfigureAwait(false);

            if (Describe(response.Failure) is { } message) return (null, message);
            return response.Profile is { } profile
                ? (profile, null)
                : (null, Ui.Sync.AgentIncompleteProfile);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return (null, DesktopAgentAvailability.ReportFailure(error));
        }
    }

    /// <summary>
    /// Creates or updates a profile from a draft.
    /// </summary>
    /// <param name="current">
    /// The profile this draft came from, or nothing to create a new one. It supplies the revision
    /// the update is made against, which is what makes a concurrent edit a refusal rather than a
    /// silent overwrite.
    /// </param>
    internal async Task<SyncProfileSaveResult> SaveAsync(
        SyncProfileDocument? current,
        SyncProfileDraftDocument draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var problems = SyncProfileDraftRules.Validate(draft);
        if (problems.Count > 0) return new SyncProfileSaveResult(null, problems);

        await using var client = _syncClients();
        try
        {
            var response = current is null
                ? await client.CreateProfileAsync(
                    new SyncProfileCreateRequest(
                        SyncManagementIpcContract.CurrentVersion, Guid.NewGuid(), draft),
                    cancellationToken).ConfigureAwait(false)
                : await client.UpdateProfileAsync(
                    new SyncProfileUpdateRequest(
                        SyncManagementIpcContract.CurrentVersion,
                        current.ProfileId,
                        current.Revision,
                        draft),
                    cancellationToken).ConfigureAwait(false);

            // AlreadyApplied is success: the agent recognised this exact draft at this exact
            // revision and did not write it twice. Anything else, including a revision conflict,
            // is a refusal and has to be reported as one.
            if (response.Outcome is not (SyncProfileMutationOutcome.Succeeded
                or SyncProfileMutationOutcome.AlreadyApplied))
            {
                return new SyncProfileSaveResult(
                    null, [], Describe(response.Failure) ?? Ui.Sync.ProfileSaveFailed);
            }

            return response.Profile is { } saved
                ? new SyncProfileSaveResult(saved, [])
                : new SyncProfileSaveResult(null, [], Ui.Sync.AgentNoRevision);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return new SyncProfileSaveResult(null, [], DesktopAgentAvailability.ReportFailure(error));
        }
    }

    /// <summary>
    /// Saves, then asks the agent to scan both locations and plan the work.
    /// </summary>
    /// <remarks>
    /// Saving first is not a convenience: a preview is generated from the stored profile, so
    /// previewing an unsaved edit would plan the previous version of it and show a plan for work
    /// nobody asked for.
    /// </remarks>
    internal async Task<SyncPreviewResult> PreviewAsync(
        SyncProfileDocument? current,
        SyncProfileDraftDocument draft,
        CancellationToken cancellationToken = default)
    {
        var saved = await SaveAsync(current, draft, cancellationToken).ConfigureAwait(false);
        if (saved.Profile is not { } profile)
        {
            return new SyncPreviewResult(null, null, saved.Problems, saved.ErrorMessage);
        }

        await using var client = _syncClients();
        try
        {
            var response = await client.GeneratePreviewAsync(
                new SyncPreviewGenerateRequest(
                    SyncManagementIpcContract.CurrentVersion, profile.ProfileId, Guid.NewGuid()),
                cancellationToken).ConfigureAwait(false);

            if (Describe(response.Failure) is { } message)
            {
                return new SyncPreviewResult(profile, null, [], message);
            }

            if (response.Run is not { } run || response.Plan is not { } plan)
            {
                return new SyncPreviewResult(
                    profile, null, [], response.Run is null ? Ui.Sync.AgentNoRun : Ui.Sync.AgentNoPlanSummary);
            }

            // The overview and the run arrive in one response and still have to agree. They are
            // what the review screen is then handed, and an overview describing a different plan
            // would put the wrong operation counts in front of whoever approves it.
            if (plan.SyncRunId != run.SyncRunId ||
                plan.PlanId != run.PlanId ||
                !string.Equals(plan.PlanSha256, run.PlanSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new SyncPreviewResult(profile, null, [], Ui.Sync.PlanRunMismatch);
            }

            return new SyncPreviewResult(profile, run, []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return new SyncPreviewResult(
                profile, null, [], DesktopAgentAvailability.ReportFailure(error));
        }
    }

    private async Task<(IReadOnlyList<SyncProfileSummary> Profiles, string? ErrorMessage)> ReadProfilesAsync(
        CancellationToken cancellationToken)
    {
        await using var client = _syncClients();
        try
        {
            var response = await client.ListProfilesAsync(
                new SyncProfileListRequest(
                    IncludeDisabled: true,
                    MaximumCount: SyncManagementIpcLimits.MaximumProfileResults),
                cancellationToken).ConfigureAwait(false);

            return Describe(response.Failure) is { } message
                ? ([], message)
                : (response.Profiles, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return ([], DesktopAgentAvailability.ReportFailure(error));
        }
    }

    private async Task<(IReadOnlyList<ConnectionSummary> Connections, string? ErrorMessage)> ReadConnectionsAsync(
        CancellationToken cancellationToken)
    {
        await using var client = _storageClients();
        try
        {
            var response = await client.ListConnectionsAsync(
                new ConnectionListRequest(
                    StorageIpcContract.CurrentVersion,
                    IncludeDisabled: true,
                    Limit: StorageIpcLimits.MaximumConnectionResults),
                cancellationToken).ConfigureAwait(false);

            return response.Failure is { } failure
                ? ([], failure.Message)
                : (response.Connections, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return ([], DesktopAgentAvailability.ReportFailure(error));
        }
    }

    private static string? Describe(StorageIpcFailure? failure) =>
        failure is null ? null : SyncFailureMessages.Describe(failure);
}
