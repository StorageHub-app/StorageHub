using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The durable transfer queue: its tabs, its commands, and the progress it reports.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
/// </remarks>
[LocalizationSection("transfer")]
internal sealed class TransferStrings : LocalizationModelBase
{
    // -------------------------------------------------------------------- tabs
    public string TabActive { get; set; } = "Active";

    public string TabQueued { get; set; } = "Queued";

    public string TabPaused { get; set; } = "Paused";

    public string TabFailed { get; set; } = "Failed";

    public string TabCompleted { get; set; } = "Completed";

    public string TabConflicts { get; set; } = "Conflicts";

    public string TabLogs { get; set; } = "Logs";

    /// <summary>{0} = the tab name, {1} = how many transfers it holds.</summary>
    public string TabCountFormat { get; set; } = "{0} ({1:N0})";

    /// <summary>
    /// {0} = how many transfers are in the tab.
    /// </summary>
    /// <remarks>
    /// The tab's own name is deliberately not composed into this sentence. A tab label is an
    /// adjective in English but has to agree with its noun in Danish and German, and interpolating
    /// it produced "Keine aktiv Übertragungen". The label is already announced as the tab's name.
    /// </remarks>
    public string TabDescriptionFormat { get; set; } = "{0:N0} transfers";

    // ---------------------------------------------------------------- headings
    public string QueueTitle { get; set; } = "Transfer queue";

    public string QueueDescription { get; set; } =
        "Durable background transfers, reconciliation actions, and activity logs.";

    public string QueueViews { get; set; } = "Transfer queue views";

    public string QueueCommands { get; set; } = "Transfer queue commands";

    public string HistoryCommands { get; set; } = "Transfer history commands";

    public string QueueStatusAccessibleName { get; set; } = "Current durable transfer queue status";

    public string ActivityLog { get; set; } = "Durable activity log";

    public string GridAccessible { get; set; } = "Durable transfers in this view.";

    /// <summary>{0} = the tab name.</summary>
    public string GridJobsAccessibleFormat { get; set; } = "{0} transfer jobs";

    // ---------------------------------------------------------------- commands
    public string Refresh { get; set; } = "Refresh";

    public string RefreshQueue { get; set; } = "Refresh queue";

    public string Cancel { get; set; } = "Cancel";

    public string CancelSelected { get; set; } = "Cancel selected transfer";

    public string Retry { get; set; } = "Retry";

    public string RetrySelected { get; set; } = "Retry selected transfer";

    public string Apply { get; set; } = "Apply";

    public string ApplyReconciliation { get; set; } = "Apply reconciliation action";

    public string ReconcileLabel { get; set; } = "Reconcile:";

    public string ReconciliationAction { get; set; } = "Reconciliation action";

    public string Next { get; set; } = "Next";

    public string NextPage { get; set; } = "Show the next queue page";

    public string ClearHistory { get; set; } = "Clear history";

    public string ClearSelectedHistory { get; set; } = "Clear selected history";

    public string CancelAndClearSelected { get; set; } = "Cancel and clear selected";

    public string NothingCouldBeCancelled { get; set; } =
        "None of the selected transfers could be cancelled, so none were cleared.";

    public string ClearAllHistory { get; set; } = "Clear all history...";

    // ----------------------------------------------------------------- columns
    public string ColumnOperation { get; set; } = "Operation";

    public string ColumnSource { get; set; } = "Source";

    public string ColumnDestination { get; set; } = "Destination";

    public string ColumnProgress { get; set; } = "Progress";

    public string ColumnAttempt { get; set; } = "Attempt";

    public string ColumnStatus { get; set; } = "Status";

    // ------------------------------------------------------------------ status
    public string QueueDeferred { get; set; } = "Queue will connect when the window is shown.";

    public string QueueUnavailable { get; set; } = "Agent queue unavailable; retrying automatically.";

    public string Refreshing { get; set; } = "Refreshing queue…";

    public string ApplyingAction { get; set; } = "Applying queue action…";

    public string ClearingHistory { get; set; } = "Clearing transfer history...";

    public string NoHistoryToClear { get; set; } = "No transfer history to clear.";

    public string NoReconciliationNeeded { get; set; } = "No transfers require reconciliation.";

    public string NoTransfers { get; set; } = "No transfers in this view.";

    /// <summary>{0} = the percentage or bytes done, {1} = how much moves per second.</summary>
    public string ProgressRateFormat { get; set; } = "{0} · {1}/s";

    /// <summary>{0} = the percentage done, {1} = how much moves per second, {2} = the time left, as 2:05.</summary>
    public string ProgressRemainingFormat { get; set; } = "{0} · {1}/s · {2} left";

    /// <summary>{0} = how many transfers this view holds.</summary>
    public string TransferCountFormat { get; set; } = "{0} transfer(s).";

    /// <summary>
    /// {0} = how many were queued. Shown by the pane that queued them, so it says what happened
    /// without somebody having to look down at the queue to find out.
    /// </summary>
    public string QueuedFormat { get; set; } = "Queued {0:N0} transfer(s).";

    /// <summary>{0} = how many records were removed.</summary>
    public string ClearedHistoryFormat { get; set; } = "Cleared {0:N0} history record(s).";

    /// <summary>{0} = how many transfers were updated.</summary>
    public string UpdatedTransfersFormat { get; set; } = "Updated {0} transfer(s).";

    /// <summary>{0} = how many were updated, {1} = how many changed or need review.</summary>
    public string UpdatedWithConflictsFormat { get; set; } = "Updated {0}; {1} changed or require review.";

    /// <summary>{0} = the transfer state, {1} = what went wrong.</summary>
    public string TransferErrorFormat { get; set; } = "{0}: {1}";

    // ------------------------------------------------------- clear-all warning
    public string ClearAllTitle { get; set; } = "Clear all transfer history?";

    public string ClearAllAccessibleName { get; set; } = "Clear all transfer history warning";

    public string ClearAllBody { get; set; } =
        "This permanently removes all completed, cancelled, and failed transfer records. Active, queued, paused, and conflicted transfers are not affected.";

    public string ClearAllSuppress { get; set; } = "Don't show this warning again";

    public string WarningPreferenceFailed { get; set; } = "The warning preference could not be saved.";

    // ------------------------------------------------------------------- misc
    public string FileExplorerSource { get; set; } = "(File Explorer)";

    // ------------------------------------------------------------ activity log
    public string ActivityLogAccessibleDescription { get; set; } =
        "Recent transfer and synchronization state from the background Agent.";

    public string ActivityRefresh { get; set; } = "Refresh";

    public string ActivityNotLoaded { get; set; } = "Activity will load when this tab is opened.";

    public string ActivityStatusAccessibleDescription { get; set; } =
        "Current durable activity log status";

    public string ActivityGridAccessibleName { get; set; } = "Recent durable activity";

    public string ActivityColumnUpdated { get; set; } = "Updated";

    public string ActivityColumnArea { get; set; } = "Area";

    public string ActivityColumnItem { get; set; } = "Item";

    public string ActivityColumnState { get; set; } = "State";

    public string ActivityColumnDetails { get; set; } = "Details";

    public string ActivityRefreshing { get; set; } = "Refreshing durable activity\u2026";

    public string ActivityNone { get; set; } = "No transfer or synchronization activity yet.";

    /// <summary>{0} = how many durable events are shown.</summary>
    public string ActivityShowingFormat { get; set; } = "Showing {0:N0} recent durable event(s).";

    /// <summary>{0} = how many events are shown, {1} = how many are still pending.</summary>
    public string ActivityShowingPendingFormat { get; set; } =
        "Showing {0:N0} recent event(s), {1:N0} still pending.";

    public string ActivityAreaTransfer { get; set; } = "Transfer";

    public string ActivityAreaDrop { get; set; } = "Drop";

    public string ActivityAreaSync { get; set; } = "Sync";

    /// <summary>{0} = the operation, {1} = the source path, {2} = the destination path.</summary>
    public string ActivityTransferDetailsFormat { get; set; } = "{0}: {1} \u2192 {2}";

    /// <summary>{0} = what is being dragged.</summary>
    public string ActivityDropToExplorerFormat { get; set; } = "Copy: {0} \u2192 File Explorer";

    /// <summary>{0} = what is being dragged, {1} = where it is going.</summary>
    public string ActivityDropToDestinationFormat { get; set; } = "Copy: {0} \u2192 {1}";

    /// <summary>{0} = the baseline generation, {1} = the dispatch state.</summary>
    public string ActivitySyncGenerationFormat { get; set; } = "Generation {0}; {1}";

    /// <summary>{0} = the baseline generation, {1} = how many conflicts it found.</summary>
    public string ActivitySyncConflictsFormat { get; set; } = "Generation {0}; {1:N0} conflict(s)";

    // ----------------------------------------------------------- pending drops
    public string DropWaitingForDestination { get; set; } = "Waiting for destination";

    public string DropQueued { get; set; } = "Queued";

    public string DropGathering { get; set; } = "Gathering folders and files...";

    public string DropGatheringFormat { get; set; } = "Gathering folders and files... {0} queued, {1} folders";

    public string DropCancelled { get; set; } = "Cancelled";

    /// <summary>{0} = why the drop was cancelled.</summary>
    public string DropCancelledFormat { get; set; } = "Cancelled: {0}";

    public string DropFailed { get; set; } = "Failed";

    /// <summary>{0} = why the drop failed.</summary>
    public string DropFailedFormat { get; set; } = "Failed: {0}";

    /// <summary>{0} = how many items, {1} = where they came from.</summary>
    public string DropItemsFromFormat { get; set; } = "{0:N0} items from {1}";

    /// <summary>Stands in for the source pane when a drag does not name one.</summary>
    public string DropSelection { get; set; } = "selection";

    // -------------------------------------------------------------- queue states
    /// <remarks>
    /// One word per <c>TransferQueueState</c>, read through <c>UiEnumNames</c>. These are what a
    /// status column says, so they are the state in plain words rather than the contract's
    /// identifier: "Needs reconciliation", not "NeedsReconciliation".
    /// </remarks>
    public string StatePending { get; set; } = "Pending";

    public string StatePreparing { get; set; } = "Preparing";

    public string StateConnecting { get; set; } = "Connecting";

    public string StateTransferring { get; set; } = "Transferring";

    public string StateVerifying { get; set; } = "Verifying";

    public string StateFinalizing { get; set; } = "Finalizing";

    public string StatePaused { get; set; } = "Paused";

    public string StateRetrying { get; set; } = "Retrying";

    public string StateBlockedCredential { get; set; } = "Blocked: credential";

    public string StateBlockedTrust { get; set; } = "Blocked: trust";

    public string StateInterrupted { get; set; } = "Interrupted";

    public string StateNeedsReconciliation { get; set; } = "Needs reconciliation";

    public string StateRestartRequired { get; set; } = "Restart required";

    public string StateCleanupPending { get; set; } = "Cleanup pending";

    public string StateCompleted { get; set; } = "Completed";

    public string StateFailed { get; set; } = "Failed";

    public string StateCancelled { get; set; } = "Cancelled";

    // ---------------------------------------------------------------- operations
    public string OperationCopy { get; set; } = "Copy";

    public string OperationMove { get; set; } = "Move";

    // ----------------------------------------------------- reconciliation actions
    public string ReconcileReview { get; set; } = "Review";

    public string ReconcileRestart { get; set; } = "Restart";

    public string ReconcileMarkCompleted { get; set; } = "Mark completed";

    public string ReconcileMarkFailed { get; set; } = "Mark failed";

    public string ReconcileCancel { get; set; } = "Cancel";

    /// <summary>{0} = the state, {1} = what went wrong.</summary>
    public string StateWithErrorFormat { get; set; } = "{0}: {1}";
}
