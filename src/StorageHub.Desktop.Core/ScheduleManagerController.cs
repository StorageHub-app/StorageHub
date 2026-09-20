using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// What the schedule manager knows after it has asked the agent.
/// </summary>
/// <remarks>
/// The profiles come with the schedules because a schedule is meaningless without one: a schedule
/// names a profile, and a manager that cannot list profiles can show what exists but cannot make
/// anything new.
/// </remarks>
internal sealed record ScheduleWorkspace(
    IReadOnlyList<ScheduleDocument> Schedules,
    IReadOnlyList<SyncProfileSummary> Profiles,
    string? ScheduleError = null,
    string? ProfileError = null)
{
    internal static ScheduleWorkspace Empty { get; } = new([], []);

    internal bool Failed => ScheduleError is not null || ProfileError is not null;

    internal string Describe() => (ScheduleError, ProfileError) switch
    {
        (null, null) => Schedules.Count == 0
            ? Ui.Schedules.NoSchedulesYet
            : Ui.Format(Ui.Schedules.SchedulesLoadedFormat, Schedules.Count),
        (not null, _) => ScheduleError,
        _ => ProfileError
    };
}

/// <summary>The result of changing a schedule, or of trying to.</summary>
internal sealed record ScheduleChangeResult(
    ScheduleDocument? Schedule,
    string? ErrorMessage = null)
{
    internal bool Changed => ErrorMessage is null;
}

/// <summary>
/// Lists, saves, enables and deletes sync schedules.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>ScheduleManagerForm</c>, 1,111 lines of which this was about a hundred and
/// fifty. What is here is what can be wrong: that every change carries the revision it was read
/// at, that an outcome is only treated as success when the agent says so, and that a refusal
/// because a run is in progress is reported as that rather than as a generic failure -- it is the
/// one refusal where waiting a minute and trying again is the right answer.
/// </para>
/// <para>
/// The cron translation is not here. It is <see cref="ScheduleRecurrence"/>, which needs no agent
/// and is the part with the most ways to be subtly wrong.
/// </para>
/// </remarks>
internal sealed class ScheduleManagerController(
    Func<IScheduleManagementAgentClient> scheduleClients,
    Func<ISyncManagementAgentClient> syncClients)
{
    private readonly Func<IScheduleManagementAgentClient> _scheduleClients =
        scheduleClients ?? throw new ArgumentNullException(nameof(scheduleClients));

    private readonly Func<ISyncManagementAgentClient> _syncClients =
        syncClients ?? throw new ArgumentNullException(nameof(syncClients));

    /// <summary>Reads the saved schedules and the profiles they can name.</summary>
    internal async Task<ScheduleWorkspace> LoadAsync(CancellationToken cancellationToken = default)
    {
        var schedules = await ReadSchedulesAsync(cancellationToken).ConfigureAwait(false);
        var profiles = await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);

        return new ScheduleWorkspace(
            schedules.Schedules, profiles.Profiles, schedules.ErrorMessage, profiles.ErrorMessage);
    }

    /// <summary>
    /// Creates or updates a schedule from a draft.
    /// </summary>
    /// <param name="current">
    /// The schedule this draft came from, or nothing to create one. It supplies the revision the
    /// update is made against.
    /// </param>
    internal async Task<ScheduleChangeResult> SaveAsync(
        ScheduleDocument? current,
        ScheduleDraftDocument draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!draft.HasValidBounds) return new ScheduleChangeResult(null, Ui.Schedules.CronHint);

        await using var client = _scheduleClients();
        try
        {
            var response = current is null
                ? await client.CreateAsync(
                    new ScheduleCreateRequest(
                        ScheduleManagementIpcContract.CurrentVersion, Guid.NewGuid(), draft),
                    cancellationToken).ConfigureAwait(false)
                : await client.UpdateAsync(
                    new ScheduleUpdateRequest(
                        ScheduleManagementIpcContract.CurrentVersion,
                        current.ScheduleId,
                        current.Revision,
                        draft),
                    cancellationToken).ConfigureAwait(false);

            return Interpret(response, Ui.Schedules.ScheduleChangeFailed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return new ScheduleChangeResult(null, DesktopAgentAvailability.ReportFailure(error));
        }
    }

    /// <summary>Turns a schedule on or off, which is a change of its own on the agent.</summary>
    internal async Task<ScheduleChangeResult> SetEnabledAsync(
        ScheduleDocument current,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);

        await using var client = _scheduleClients();
        try
        {
            var response = await client.SetEnabledAsync(
                new ScheduleSetEnabledRequest(
                    ScheduleManagementIpcContract.CurrentVersion,
                    current.ScheduleId,
                    current.Revision,
                    enabled),
                cancellationToken).ConfigureAwait(false);

            return Interpret(response, Ui.Schedules.ScheduleChangeFailed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return new ScheduleChangeResult(null, DesktopAgentAvailability.ReportFailure(error));
        }
    }

    /// <summary>
    /// Deletes a schedule. Its run history stays.
    /// </summary>
    /// <remarks>
    /// A delete answers with no schedule, which is correct and not a failure -- so unlike the other
    /// two it cannot use the presence of a document to tell success from refusal.
    /// </remarks>
    internal async Task<ScheduleChangeResult> DeleteAsync(
        ScheduleDocument current,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);

        await using var client = _scheduleClients();
        try
        {
            var response = await client.DeleteAsync(
                new ScheduleDeleteRequest(
                    ScheduleManagementIpcContract.CurrentVersion,
                    current.ScheduleId,
                    current.Revision),
                cancellationToken).ConfigureAwait(false);

            return Accepted(response)
                ? new ScheduleChangeResult(null)
                : new ScheduleChangeResult(null, Refusal(response, Ui.Schedules.ScheduleDeleteFailed));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return new ScheduleChangeResult(null, DesktopAgentAvailability.ReportFailure(error));
        }
    }

    private static ScheduleChangeResult Interpret(
        ScheduleMutationResponse response,
        string fallback)
    {
        if (!Accepted(response))
        {
            return new ScheduleChangeResult(null, Refusal(response, fallback));
        }

        // Accepted, but the screen shows the new revision and the next occurrence, and both come
        // from the document. Reporting success without one leaves the manager displaying the
        // values from before the change as though they were after it.
        return response.Schedule is { } schedule
            ? new ScheduleChangeResult(schedule)
            : new ScheduleChangeResult(null, Ui.Schedules.NoScheduleRevision);
    }

    private static bool Accepted(ScheduleMutationResponse response) =>
        response.Failure is null &&
        response.Outcome is ScheduleMutationOutcome.Succeeded or ScheduleMutationOutcome.AlreadyApplied;

    /// <summary>
    /// Why a change was refused, in words that say what to do about it.
    /// </summary>
    /// <remarks>
    /// <see cref="ScheduleMutationOutcome.ActiveRun"/> is the one worth naming: it means the
    /// schedule is running right now and the change will be accepted once it is not. A generic
    /// "the schedule could not be changed" reads as broken rather than as busy.
    /// </remarks>
    private static string Refusal(ScheduleMutationResponse response, string fallback) =>
        response.Outcome switch
        {
            ScheduleMutationOutcome.ActiveRun => Ui.Schedules.ActiveRunsBlock,
            ScheduleMutationOutcome.NotFound => Ui.Schedules.SelectScheduleFirst,
            _ => response.Failure?.Message ?? fallback
        };

    private async Task<(IReadOnlyList<ScheduleDocument> Schedules, string? ErrorMessage)> ReadSchedulesAsync(
        CancellationToken cancellationToken)
    {
        await using var client = _scheduleClients();
        try
        {
            var response = await client.ListAsync(
                new ScheduleListRequest(IncludeDisabled: true), cancellationToken)
                .ConfigureAwait(false);

            return response.Failure is { } failure
                ? ([], failure.Message)
                : (response.Schedules, null);
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

            return response.Failure is { } failure
                ? ([], SyncFailureMessages.Describe(failure))
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
}
