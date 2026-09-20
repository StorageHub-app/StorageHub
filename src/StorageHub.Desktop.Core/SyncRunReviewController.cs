using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>One page of durable run history.</summary>
/// <param name="NextPageToken">
/// What to ask for to see the page after this one, or nothing when this is the last.
/// </param>
internal sealed record SyncRunHistoryPage(
    IReadOnlyList<SyncRunSummary> Runs,
    string? NextPageToken = null,
    string? ErrorMessage = null)
{
    internal static SyncRunHistoryPage Empty { get; } = new([]);

    internal bool Failed => ErrorMessage is not null;

    internal bool HasNextPage => NextPageToken is not null;
}

/// <summary>
/// One immutable run, as far as it has been read.
/// </summary>
/// <remarks>
/// The continuation tokens travel with the rest rather than living in the controller, so the
/// controller stays a set of functions over a value: given this much of a run, ask for the next
/// part of it. A screen can then hold two of these, or discard one mid-flight, without the
/// controller having a stale cursor to forget.
/// </remarks>
internal sealed record SyncRunReview(
    SyncRunSummary? Run,
    IReadOnlyList<SyncPlanOperationSummary> Operations,
    IReadOnlyList<SyncConflictSummary> Conflicts,
    string? PlanContinuation = null,
    string? ConflictContinuation = null,
    string? ErrorMessage = null)
{
    internal static SyncRunReview Empty { get; } = new(null, [], []);

    internal bool Failed => ErrorMessage is not null;

    internal bool HasMoreOperations => PlanContinuation is not null;

    internal bool HasMoreConflicts => ConflictContinuation is not null;

    /// <summary>
    /// Whether approving would be accepted.
    /// </summary>
    /// <remarks>
    /// The same two conditions the agent enforces, asked here so the button is unavailable rather
    /// than refused. A run already dispatched is not offered again: the request is idempotent on
    /// the agent, but a second Approve that appears to work is how somebody concludes the first one
    /// did not.
    /// </remarks>
    internal bool CanApprove => Run is
    {
        Phase: SyncIpcRunPhase.AwaitingApproval,
        DispatchState: SyncIpcDispatchState.NotDispatched
    };

    internal SyncRunReview WithError(string message) => this with { ErrorMessage = message };
}

/// <summary>
/// Browses durable sync history, reads one immutable run, and dispatches its exact approval.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>SyncRunsControl</c> and <c>SyncRunReviewControl</c>, which between them were
/// nine hundred and sixty lines of which the protocol was perhaps two hundred. What is here is the
/// part that can be wrong, and on this screen being wrong means copying or deleting somebody's
/// files: that the plan page shown belongs to the plan being approved, that the revision and digest
/// sent are the ones the reviewer actually saw, and that a dispatch is reported as durable only
/// when the agent said so.
/// </para>
/// <para>
/// Every call returns a value, including the failures. "The agent is not running" and "that run id
/// does not exist" are states this screen has to show, not exceptional events, and a failure is
/// folded into whatever was already loaded so a transient error does not blank a plan somebody was
/// halfway through reading.
/// </para>
/// <para>
/// A client per call rather than one held open, as <see cref="SyncTasksController"/> and the panes
/// already do, because the agent restarts and a connection held across that has to be discovered
/// broken before it can be replaced.
/// </para>
/// </remarks>
internal sealed class SyncRunReviewController(Func<ISyncManagementAgentClient> clients)
{
    private const int PageSize = SyncManagementIpcLimits.MaximumPageSize;

    private readonly Func<ISyncManagementAgentClient> _clients =
        clients ?? throw new ArgumentNullException(nameof(clients));

    /// <summary>Reads one page of history, from the given cursor.</summary>
    internal async Task<SyncRunHistoryPage> LoadHistoryAsync(
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        await using var client = _clients();
        try
        {
            var response = await client.ListRunsAsync(
                new SyncRunListRequest(PageSize: PageSize, ContinuationToken: continuationToken),
                cancellationToken).ConfigureAwait(false);

            return Describe(response.Failure) is { } message
                ? new SyncRunHistoryPage([], null, message)
                : new SyncRunHistoryPage(
                    response.Runs, Advance(response.ContinuationToken, continuationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return new SyncRunHistoryPage([], null, DesktopAgentAvailability.ReportFailure(error));
        }
    }

    /// <summary>Reads a run and the first page of its plan and of its conflicts.</summary>
    internal async Task<SyncRunReview> LoadRunAsync(
        Guid syncRunId,
        CancellationToken cancellationToken = default)
    {
        if (syncRunId == Guid.Empty) return SyncRunReview.Empty.WithError(Ui.Sync.EnterValidRunId);

        await using var client = _clients();
        try
        {
            var review = await ReadStatusAsync(
                client, SyncRunReview.Empty, syncRunId, cancellationToken).ConfigureAwait(false);
            if (review.Failed) return review;

            review = await ReadPlanAsync(client, review, reset: true, cancellationToken)
                .ConfigureAwait(false);
            return review.Failed
                ? review
                : await ReadConflictsAsync(client, review, reset: true, cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return SyncRunReview.Empty.WithError(DesktopAgentAvailability.ReportFailure(error));
        }
    }

    /// <summary>
    /// Re-reads the run itself, leaving the pages already loaded where they are.
    /// </summary>
    /// <remarks>
    /// A plan is immutable once previewed, so only the phase, the revision and the dispatch state
    /// can have moved. Re-reading the pages as well would throw away a reviewer's place in a long
    /// plan every five seconds, for no new information.
    /// </remarks>
    internal async Task<SyncRunReview> RefreshStatusAsync(
        SyncRunReview current,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Run is not { } run) return current.WithError(Ui.Sync.NoRunLoaded);

        await using var client = _clients();
        try
        {
            return await ReadStatusAsync(client, current, run.SyncRunId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return current.WithError(DesktopAgentAvailability.ReportFailure(error));
        }
    }

    /// <summary>Appends the next page of plan operations.</summary>
    internal Task<SyncRunReview> LoadMoreOperationsAsync(
        SyncRunReview current,
        CancellationToken cancellationToken = default) =>
        ContinueAsync(current, ReadPlanAsync, cancellationToken);

    /// <summary>Appends the next page of conflicts.</summary>
    internal Task<SyncRunReview> LoadMoreConflictsAsync(
        SyncRunReview current,
        CancellationToken cancellationToken = default) =>
        ContinueAsync(current, ReadConflictsAsync, cancellationToken);

    /// <summary>
    /// Approves the loaded run and asks the agent to make the apply request durable.
    /// </summary>
    /// <remarks>
    /// Success here means the request is on disk and will survive a restart. It never means the
    /// providers have copied or deleted anything -- that is what refreshing the phase afterwards is
    /// for, and saying otherwise is how a half-finished sync gets reported as done.
    /// </remarks>
    internal async Task<SyncRunReview> ApproveAndDispatchAsync(
        SyncRunReview current,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Run is not { } run) return current.WithError(Ui.Sync.NoRunLoaded);

        // Already dispatched is success, not a second dispatch. Asking again would be accepted and
        // would tell the reviewer nothing they did not already know.
        if (run.DispatchState == SyncIpcDispatchState.DurablyDispatched)
        {
            return current with { ErrorMessage = null };
        }

        if (run.Phase != SyncIpcRunPhase.AwaitingApproval)
        {
            return current.WithError(Ui.Sync.NotAwaitingApproval);
        }

        await using var client = _clients();
        try
        {
            // The revision and the digest are taken from the summary the reviewer was shown, not
            // from a fresh read. Re-reading here would silently approve whatever the run had become
            // in the meantime, which is the one thing this screen exists to prevent.
            var response = await client.ApproveAndDispatchAsync(
                new SyncApproveDispatchRequest(
                    SyncManagementIpcContract.CurrentVersion,
                    run.SyncRunId,
                    run.Revision,
                    run.ApprovalSha256),
                cancellationToken).ConfigureAwait(false);

            if (Describe(response.Failure) is { } message) return current.WithError(message);
            if (response.Run is not { } dispatched)
            {
                return current.WithError(Ui.Sync.IncompleteSyncResponse);
            }

            // Three ways a response can look like agreement without being it: the flag unset, a
            // different run answered, or a dispatch state that never changed.
            if (!response.DurablyDispatched ||
                dispatched.SyncRunId != run.SyncRunId ||
                dispatched.DispatchState != SyncIpcDispatchState.DurablyDispatched)
            {
                return current.WithError(Ui.Sync.DispatchNotConfirmed);
            }

            return current with { Run = dispatched, ErrorMessage = null };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return current.WithError(DesktopAgentAvailability.ReportFailure(error));
        }
    }

    private async Task<SyncRunReview> ContinueAsync(
        SyncRunReview current,
        Func<ISyncManagementAgentClient, SyncRunReview, bool, CancellationToken, Task<SyncRunReview>> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.Run is null) return current.WithError(Ui.Sync.NoRunLoaded);

        await using var client = _clients();
        try
        {
            return await read(client, current, false, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return current.WithError(DesktopAgentAvailability.ReportFailure(error));
        }
    }

    private static async Task<SyncRunReview> ReadStatusAsync(
        ISyncManagementAgentClient client,
        SyncRunReview review,
        Guid syncRunId,
        CancellationToken cancellationToken)
    {
        var response = await client.GetRunStatusAsync(
            new SyncRunStatusRequest(SyncManagementIpcContract.CurrentVersion, syncRunId),
            cancellationToken).ConfigureAwait(false);

        if (Describe(response.Failure) is { } message) return review.WithError(message);
        return response.Run is { } run
            ? review with { Run = run, ErrorMessage = null }
            : review.WithError(Ui.Sync.IncompleteSyncResponse);
    }

    private static async Task<SyncRunReview> ReadPlanAsync(
        ISyncManagementAgentClient client,
        SyncRunReview review,
        bool reset,
        CancellationToken cancellationToken)
    {
        if (review.Run is not { } run) return review.WithError(Ui.Sync.NoRunLoaded);
        var continuation = reset ? null : review.PlanContinuation;
        if (!reset && continuation is null) return review;

        var response = await client.GetPlanPageAsync(
            new SyncPlanPageRequest(
                SyncManagementIpcContract.CurrentVersion, run.SyncRunId, PageSize, continuation),
            cancellationToken).ConfigureAwait(false);

        if (Describe(response.Failure) is { } message) return review.WithError(message);

        // The page has to belong to the plan the reviewer is looking at. A run re-previewed between
        // the status read and this page would otherwise list its new operations under the digest
        // that Approve is about to send -- a plan shown that is not the plan approved.
        if (response.PlanId != run.PlanId ||
            !string.Equals(response.PlanSha256, run.PlanSha256, StringComparison.OrdinalIgnoreCase))
        {
            return review.WithError(Ui.Sync.PlanPageMismatch);
        }

        return review with
        {
            Operations = reset
                ? response.Operations
                : [.. review.Operations, .. response.Operations],
            PlanContinuation = Advance(response.ContinuationToken, continuation),
            ErrorMessage = null
        };
    }

    private static async Task<SyncRunReview> ReadConflictsAsync(
        ISyncManagementAgentClient client,
        SyncRunReview review,
        bool reset,
        CancellationToken cancellationToken)
    {
        if (review.Run is not { } run) return review.WithError(Ui.Sync.NoRunLoaded);
        var continuation = reset ? null : review.ConflictContinuation;
        if (!reset && continuation is null) return review;

        var response = await client.GetConflictPageAsync(
            new SyncConflictPageRequest(
                SyncManagementIpcContract.CurrentVersion,
                run.SyncRunId,
                State: null,
                PageSize: PageSize,
                ContinuationToken: continuation),
            cancellationToken).ConfigureAwait(false);

        if (Describe(response.Failure) is { } message) return review.WithError(message);

        return review with
        {
            Conflicts = reset
                ? response.Conflicts
                : [.. review.Conflicts, .. response.Conflicts],
            ConflictContinuation = Advance(response.ContinuationToken, continuation),
            ErrorMessage = null
        };
    }

    /// <summary>
    /// The cursor to use next, or nothing when there is no more to read.
    /// </summary>
    /// <remarks>
    /// A token that comes back identical to the one just sent means the agent is not advancing.
    /// Following it pages forever against a live pipe, which presents as a screen that hangs rather
    /// than one that fails, so it is treated as the end.
    /// </remarks>
    private static string? Advance(string? returned, string? sent) =>
        string.Equals(returned, sent, StringComparison.Ordinal) ? null : returned;

    private static string? Describe(StorageIpcFailure? failure) =>
        failure is null ? null : SyncFailureMessages.Describe(failure);
}

/// <summary>
/// How often a loaded run is worth re-reading.
/// </summary>
/// <remarks>
/// The WinForms screen had two mechanisms for this -- a five second timer for as long as a run was
/// open, and a separate burst of twenty half-second reads straight after dispatching -- which
/// between them had three states and two places to get the terminal check wrong. It is one rule
/// here, derived from the run: a run the agent is acting on is worth watching closely, one that is
/// merely open is worth watching occasionally, and a finished run is not worth watching at all.
/// </remarks>
internal static class SyncRunPolling
{
    /// <summary>While the agent is acting on the run.</summary>
    internal static readonly TimeSpan Active = TimeSpan.FromMilliseconds(500);

    /// <summary>While a run is merely open, in case another client moves it.</summary>
    internal static readonly TimeSpan Idle = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait before reading the run again, or nothing to stop reading.</summary>
    internal static TimeSpan? Interval(SyncRunSummary? run) => run switch
    {
        null => null,
        { } settled when IsTerminal(settled.Phase) => null,
        { DispatchState: SyncIpcDispatchState.DurablyDispatched } => Active,
        _ => Idle
    };

    /// <summary>Whether the run has reached a phase it will not leave on its own.</summary>
    internal static bool IsTerminal(SyncIpcRunPhase phase) => phase is
        SyncIpcRunPhase.Completed or
        SyncIpcRunPhase.Failed or
        SyncIpcRunPhase.Cancelled or
        SyncIpcRunPhase.Interrupted or
        SyncIpcRunPhase.NeedsReconciliation or
        SyncIpcRunPhase.BlockedConflict or
        SyncIpcRunPhase.BlockedDeletionGuard or
        SyncIpcRunPhase.BlockedEndpoint or
        SyncIpcRunPhase.BlockedCredential or
        SyncIpcRunPhase.BlockedTrust;
}
