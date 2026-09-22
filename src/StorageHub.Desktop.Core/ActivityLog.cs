using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>One line of the activity log.</summary>
/// <param name="Key">
/// What makes the line the same line on the next poll: its area and the record's id. Not shown.
/// The screen reconciles rows by it, so a transfer that changes state rewrites its own row rather
/// than every row below it moving.
/// </param>
internal sealed record ActivityEntry(
    string Key,
    DateTimeOffset UpdatedUtc,
    string Area,
    string Item,
    string State,
    string Details);

/// <summary>What one read of the log produced, or why it could not be read.</summary>
internal sealed record ActivityLogResult(
    IReadOnlyList<ActivityEntry> Entries,
    int PendingDrops,
    string? ErrorMessage = null)
{
    internal bool Failed => ErrorMessage is not null;

    /// <summary>The status line under the log.</summary>
    internal string Describe() => ErrorMessage ?? (Entries.Count switch
    {
        0 => Ui.Transfer.ActivityNone,
        var count when PendingDrops == 0 => Ui.Format(Ui.Transfer.ActivityShowingFormat, count),
        var count => Ui.Format(Ui.Transfer.ActivityShowingPendingFormat, count, PendingDrops)
    });
}

/// <summary>
/// A bounded, non-secret log of recent durable work: transfers, sync runs, and drags that have not
/// become transfers yet.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>ActivityLogControl</c>, where the entries were a private record inside a
/// UserControl and the merge of two agent listings happened in a click handler.
/// </para>
/// <para>
/// Two corrections on the way through. The transfer's operation and a run's dispatch state were
/// formatted as raw enum names, so the details column read "Copy" in English and
/// "NotDispatched" in every language; both go through <see cref="UiEnumNames"/> now, as the state
/// column already did. And the row identity is carried with the entry rather than recovered from
/// its position, which is what lets the screen update a row in place when it changes.
/// </para>
/// </remarks>
internal static class ActivityLog
{
    /// <summary>The most lines shown. The log is a glance, not an audit trail.</summary>
    internal const int MaximumEntries = 100;

    internal const string TransferKeyPrefix = "transfer:";
    internal const string SyncKeyPrefix = "sync:";
    internal const string DropKeyPrefix = "drop:";

    /// <summary>
    /// Merges the three sources, newest first, capped at <see cref="MaximumEntries"/>.
    /// </summary>
    /// <remarks>
    /// Ties are broken by area and then by key, so two records written in the same tick keep the
    /// same order on every poll instead of trading places, which on screen looks like a flicker.
    /// </remarks>
    internal static IReadOnlyList<ActivityEntry> Build(
        IEnumerable<TransferQueueSummary> transfers,
        IEnumerable<SyncRunSummary> runs,
        IEnumerable<PendingDropEntry>? pending = null)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(runs);

        return
        [
            .. transfers.Select(FromTransfer)
                .Concat(runs.Select(FromRun))
                .Concat((pending ?? []).Select(FromPendingDrop))
                .OrderByDescending(static entry => entry.UpdatedUtc)
                .ThenBy(static entry => entry.Area, StringComparer.Ordinal)
                .ThenBy(static entry => entry.Key, StringComparer.Ordinal)
                .Take(MaximumEntries)
        ];
    }

    internal static ActivityEntry FromTransfer(TransferQueueSummary transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        return new ActivityEntry(
            TransferKeyPrefix + transfer.TransferId.ToString("N"),
            transfer.UpdatedUtc,
            Ui.Transfer.ActivityAreaTransfer,
            Short(transfer.TransferId),
            UiEnumNames.Name(transfer.State) ?? transfer.State.ToString(),
            transfer.ErrorSummary ?? Ui.Format(
                Ui.Transfer.ActivityTransferDetailsFormat,
                UiEnumNames.Name(transfer.Operation) ?? transfer.Operation.ToString(),
                DisplayPath(transfer.SourcePath),
                DisplayPath(transfer.DestinationPath)));
    }

    internal static ActivityEntry FromRun(SyncRunSummary run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new ActivityEntry(
            SyncKeyPrefix + run.SyncRunId.ToString("N"),
            run.UpdatedUtc,
            Ui.Transfer.ActivityAreaSync,
            Short(run.SyncRunId),
            UiEnumNames.Name(run.Phase) ?? run.Phase.ToString(),
            run.ConflictCount == 0
                ? Ui.Format(
                    Ui.Transfer.ActivitySyncGenerationFormat,
                    run.Generation,
                    UiEnumNames.Name(run.DispatchState) ?? run.DispatchState.ToString())
                : Ui.Format(Ui.Transfer.ActivitySyncConflictsFormat, run.Generation, run.ConflictCount));
    }

    /// <summary>
    /// A drag that has left a pane and has no durable record yet.
    /// </summary>
    /// <remarks>
    /// Shown under its own area so it reads as pending rather than as something the agent has
    /// committed to.
    /// </remarks>
    internal static ActivityEntry FromPendingDrop(PendingDropEntry drop)
    {
        ArgumentNullException.ThrowIfNull(drop);
        return new ActivityEntry(
            DropKeyPrefix + drop.Token,
            drop.UpdatedUtc,
            Ui.Transfer.ActivityAreaDrop,
            drop.Token.Length > 8 ? drop.Token[..8] : drop.Token,
            drop.Describe(),
            drop.Destination is null
                ? Ui.Format(Ui.Transfer.ActivityDropToExplorerFormat, drop.DescribeSource())
                : Ui.Format(Ui.Transfer.ActivityDropToDestinationFormat, drop.DescribeSource(), drop.Destination));
    }

    /// <summary>The first eight hex digits of an id, which is enough to tell rows apart at a glance.</summary>
    private static string Short(Guid id) => id.ToString("N")[..8];

    private static string DisplayPath(string value) => value.Length == 0 ? "/" : value;
}

/// <summary>
/// Reads the activity log from the agent.
/// </summary>
/// <remarks>
/// The two listings go out together and either failing fails the read: a log that showed transfers
/// and silently left out sync runs would look complete and not be.
/// </remarks>
internal sealed class ActivityLogReader(
    Func<ITransferQueueAgentClient> transfers,
    Func<ISyncManagementAgentClient> sync,
    Func<IReadOnlyList<PendingDropEntry>>? pending = null)
{
    private readonly Func<ITransferQueueAgentClient> _transfers =
        transfers ?? throw new ArgumentNullException(nameof(transfers));

    private readonly Func<ISyncManagementAgentClient> _sync =
        sync ?? throw new ArgumentNullException(nameof(sync));

    internal async Task<ActivityLogResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transferClient = _transfers();
            await using var syncClient = _sync();

            var transfersTask = transferClient.ListAsync(
                new TransferListRequest(
                    TransferQueueIpcContract.CurrentVersion,
                    Enum.GetValues<TransferQueueState>(),
                    TransferQueueIpcLimits.MaximumPageSize),
                cancellationToken);
            var runsTask = syncClient.ListRunsAsync(
                new SyncRunListRequest(PageSize: SyncManagementIpcLimits.MaximumPageSize),
                cancellationToken);
            await Task.WhenAll(transfersTask, runsTask).ConfigureAwait(false);

            var transferList = await transfersTask.ConfigureAwait(false);
            var runList = await runsTask.ConfigureAwait(false);
            if (transferList.Failure is { } transferFailure)
            {
                return new ActivityLogResult([], 0, transferFailure.Message);
            }

            if (runList.Failure is { } runFailure)
            {
                return new ActivityLogResult([], 0, runFailure.Message);
            }

            var drops = pending?.Invoke() ?? [];
            return new ActivityLogResult(ActivityLog.Build(transferList.Transfers, runList.Runs, drops), drops.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidOperationException or TimeoutException or ObjectDisposedException or
            System.Text.Json.JsonException)
        {
            return new ActivityLogResult([], 0, DesktopAgentAvailability.ReportFailure(error));
        }
    }
}
