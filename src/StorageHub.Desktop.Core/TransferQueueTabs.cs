using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>One tab of the transfer queue: its identity, its label, and the states it holds.</summary>
/// <param name="Key">
/// Stable and untranslated. It identifies the tab in code and in a saved selection, while
/// <see cref="Label"/> follows the language.
/// </param>
internal sealed record TransferQueueTabDefinition(
    string Key,
    Func<string> Label,
    UiGlyph Glyph,
    IReadOnlyList<TransferQueueState> States);

/// <summary>
/// How the queue's twenty states become six tabs.
/// </summary>
/// <remarks>
/// <para>
/// Lifted out of TransferQueueControl, where it was a static field on a UserControl. Nothing about
/// it draws: it is the answer to "which tab is this transfer on", which both shells need and which
/// is worth testing without one.
/// </para>
/// <para>
/// The grouping is the old shell's exactly, including the parts that are not obvious. Retrying sits
/// under Queued rather than Failed because a transfer waiting to retry is going to run; the two
/// Blocked states and RestartRequired sit under Paused because they are stopped and waiting for a
/// person; Cancelled sits under Completed because it is finished, whatever the outcome.
/// </para>
/// </remarks>
internal static class TransferQueueTabs
{
    internal const string ActiveKey = "Active";
    internal const string QueuedKey = "Queued";
    internal const string PausedKey = "Paused";
    internal const string FailedKey = "Failed";
    internal const string CompletedKey = "Completed";
    internal const string ConflictsKey = "Conflicts";

    /// <summary>The Logs tab, which shows the activity log rather than a state filter.</summary>
    internal const string LogsKey = "Logs";

    /// <summary>Whether a tab filters the queue, as against showing something else entirely.</summary>
    internal static bool IsStateFilter(string key) => StatesFor(key).Count > 0;

    internal static IReadOnlyList<TransferQueueTabDefinition> All { get; } =
    [
        new(ActiveKey, () => Ui.Transfer.TabActive, UiGlyph.Run,
        [
            TransferQueueState.Preparing,
            TransferQueueState.Connecting,
            TransferQueueState.Transferring,
            TransferQueueState.Verifying,
            TransferQueueState.Finalizing,
            TransferQueueState.CleanupPending
        ]),
        new(QueuedKey, () => Ui.Transfer.TabQueued, UiGlyph.More,
            [TransferQueueState.Pending, TransferQueueState.Retrying]),
        new(PausedKey, () => Ui.Transfer.TabPaused, UiGlyph.Pause,
        [
            TransferQueueState.Paused,
            TransferQueueState.BlockedCredential,
            TransferQueueState.BlockedTrust,
            TransferQueueState.RestartRequired
        ]),
        new(FailedKey, () => Ui.Transfer.TabFailed, UiGlyph.Warning,
            [TransferQueueState.Failed]),
        new(CompletedKey, () => Ui.Transfer.TabCompleted, UiGlyph.Test,
            [TransferQueueState.Completed, TransferQueueState.Cancelled]),
        new(ConflictsKey, () => Ui.Transfer.TabConflicts, UiGlyph.Compare,
            [TransferQueueState.Interrupted, TransferQueueState.NeedsReconciliation]),

        // The odd one out: it shows the activity log rather than a state filter, which is why its
        // state list is empty. A caller has to notice that and not ask the queue for it - a list
        // request with no states is refused by the contract, as it should be.
        new(LogsKey, () => Ui.Transfer.TabLogs, UiGlyph.Log, [])
    ];

    /// <summary>
    /// The states that mean a transfer is moving bytes or about to.
    /// </summary>
    /// <remarks>
    /// The queue polls twice a second while any transfer is in one of these and every two seconds
    /// otherwise, which is the difference between a progress bar that moves and a desktop that
    /// wakes the agent all day for nothing.
    /// </remarks>
    internal static bool IsActive(TransferQueueState state) =>
        All[0].States.Contains(state);

    /// <summary>The tab a state belongs to, or null for a state no tab claims.</summary>
    internal static string? TabFor(TransferQueueState state) =>
        All.FirstOrDefault(tab => tab.States.Contains(state))?.Key;

    /// <summary>The states behind a tab, or empty for one that is not a state filter.</summary>
    internal static IReadOnlyList<TransferQueueState> StatesFor(string key) =>
        All.FirstOrDefault(tab => string.Equals(tab.Key, key, StringComparison.Ordinal))?.States ?? [];
}
