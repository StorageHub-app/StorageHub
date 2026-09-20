using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop;

/// <summary>
/// What the sync tasks screen knows after it has asked the agent.
/// </summary>
/// <param name="ErrorMessage">
/// Why the load did not work, or nothing. A snapshot carries its failure rather than throwing it,
/// because "the agent is not running" is a state this screen has to be able to show, not an
/// exceptional event.
/// </param>
internal sealed record SyncTasksSnapshot(
    IReadOnlyList<SyncProfileSummary> Profiles,
    IReadOnlyList<SyncRunSummary> Runs,
    string? ErrorMessage = null)
{
    internal static SyncTasksSnapshot Empty { get; } = new([], []);

    internal int EnabledCount => Profiles.Count(static profile => profile.Enabled);

    internal int DisabledCount => Profiles.Count - EnabledCount;

    internal bool Failed => ErrorMessage is not null;
}

/// <summary>
/// Loads the sync tasks screen: the saved profiles, and the runs behind them.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>SyncTasksOverviewControl</c>, where it was mixed into six hundred lines of
/// building a <c>TableLayoutPanel</c>. Nothing here draws. What is here is the part that can be
/// wrong: how many pages of history to ask for, what to do when the agent stops making progress,
/// and what a failure leaves on the screen.
/// </para>
/// <para>
/// A client per load rather than one held open. The agent restarts -- on an update, or because a
/// terminal found it stale -- and a connection held across that is a connection that has to be
/// noticed as broken before it can be replaced. This is the same choice the panes and the transfer
/// queue already make.
/// </para>
/// </remarks>
internal sealed class SyncTasksController(Func<ISyncManagementAgentClient> clients)
{
    /// <summary>
    /// How much history the screen holds.
    /// </summary>
    /// <remarks>
    /// The screen shows recent runs, not an archive: the review screen is where a particular run is
    /// looked up by id. Without a cap, a profile that has run every ten minutes for a year would
    /// have this screen page through fifty thousand rows before showing anything.
    /// </remarks>
    private const int MaximumLoadedRuns = 200;

    private readonly Func<ISyncManagementAgentClient> _clients =
        clients ?? throw new ArgumentNullException(nameof(clients));

    internal async Task<SyncTasksSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var client = _clients();
        try
        {
            var profiles = await client.ListProfilesAsync(
                new SyncProfileListRequest(
                    IncludeDisabled: true,
                    MaximumCount: SyncManagementIpcLimits.MaximumProfileResults),
                cancellationToken).ConfigureAwait(false);

            if (profiles.Failure is { } failure)
            {
                return new SyncTasksSnapshot([], [], failure.Message);
            }

            var runs = await LoadRunsAsync(client, cancellationToken).ConfigureAwait(false);
            return runs.ErrorMessage is null
                ? new SyncTasksSnapshot(profiles.Profiles, runs.Runs)
                : new SyncTasksSnapshot(profiles.Profiles, [], runs.ErrorMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (RemoteBrowserErrors.IsExpected(error))
        {
            return new SyncTasksSnapshot([], [], DesktopAgentAvailability.ReportFailure(error));
        }
    }

    /// <summary>
    /// Pages through the run history until it runs out or the cap is reached.
    /// </summary>
    /// <remarks>
    /// A continuation token that comes back the same as the one just sent means the agent is not
    /// advancing, and following it would be an infinite loop against a live pipe -- the screen
    /// would hang rather than fail. Saying so stops it.
    /// </remarks>
    private static async Task<(IReadOnlyList<SyncRunSummary> Runs, string? ErrorMessage)> LoadRunsAsync(
        ISyncManagementAgentClient client,
        CancellationToken cancellationToken)
    {
        var runs = new List<SyncRunSummary>();
        string? continuation = null;
        do
        {
            var response = await client.ListRunsAsync(
                new SyncRunListRequest(
                    PageSize: Math.Min(
                        SyncManagementIpcLimits.MaximumPageSize, MaximumLoadedRuns - runs.Count),
                    ContinuationToken: continuation),
                cancellationToken).ConfigureAwait(false);

            if (response.Failure is { } failure) return ([], failure.Message);

            runs.AddRange(response.Runs);
            if (runs.Count >= MaximumLoadedRuns) break;

            if (response.ContinuationToken is not null &&
                string.Equals(response.ContinuationToken, continuation, StringComparison.Ordinal))
            {
                return ([], Localization.Ui.Sync.RepeatedHistoryToken);
            }

            continuation = response.ContinuationToken;
        }
        while (continuation is not null);

        return (runs, null);
    }
}
