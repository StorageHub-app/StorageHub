using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// The words for the enum values the shell shows.
/// </summary>
/// <remarks>
/// An enum member is an identifier, not a sentence. <c>NeedsReconciliation</c> is precise in the
/// contract and unreadable in a status column, and <c>ToString()</c> on it is English in every
/// language — which is how a queue toolbar ended up reading "Afstem: Review" in a Danish shell.
///
/// Naming and falling back are deliberately separate. <c>Name</c> returns null for a value it does
/// not know, and <c>Describe</c> turns that into the identifier — because these enums cross the IPC
/// boundary, where a newer agent can send a value this build has never heard of, and a status cell
/// is not worth throwing over.
///
/// Keeping them apart is what makes the gap testable. Comparing the rendered text to the identifier
/// could not: half of these words are their own identifier in English, so "Pending" is both a good
/// translation and a fallthrough. <c>UiEnumNameTests</c> asserts on <c>Name</c> instead, so a member
/// added later has no word, returns null, and fails there rather than on someone's screen.
/// </remarks>
internal static class UiEnumNames
{
    internal static string? Name(TransferQueueState state) => state switch
    {
        TransferQueueState.Pending => Ui.Transfer.StatePending,
        TransferQueueState.Preparing => Ui.Transfer.StatePreparing,
        TransferQueueState.Connecting => Ui.Transfer.StateConnecting,
        TransferQueueState.Transferring => Ui.Transfer.StateTransferring,
        TransferQueueState.Verifying => Ui.Transfer.StateVerifying,
        TransferQueueState.Finalizing => Ui.Transfer.StateFinalizing,
        TransferQueueState.Paused => Ui.Transfer.StatePaused,
        TransferQueueState.Retrying => Ui.Transfer.StateRetrying,
        TransferQueueState.BlockedCredential => Ui.Transfer.StateBlockedCredential,
        TransferQueueState.BlockedTrust => Ui.Transfer.StateBlockedTrust,
        TransferQueueState.Interrupted => Ui.Transfer.StateInterrupted,
        TransferQueueState.NeedsReconciliation => Ui.Transfer.StateNeedsReconciliation,
        TransferQueueState.RestartRequired => Ui.Transfer.StateRestartRequired,
        TransferQueueState.CleanupPending => Ui.Transfer.StateCleanupPending,
        TransferQueueState.Completed => Ui.Transfer.StateCompleted,
        TransferQueueState.Failed => Ui.Transfer.StateFailed,
        TransferQueueState.Cancelled => Ui.Transfer.StateCancelled,
        _ => null
    };

    internal static string? Name(TransferQueueOperation operation) => operation switch
    {
        TransferQueueOperation.Copy => Ui.Transfer.OperationCopy,
        TransferQueueOperation.Move => Ui.Transfer.OperationMove,
        _ => null
    };

    internal static string? Name(TransferReconciliationAction action) => action switch
    {
        TransferReconciliationAction.Review => Ui.Transfer.ReconcileReview,
        TransferReconciliationAction.Restart => Ui.Transfer.ReconcileRestart,
        TransferReconciliationAction.MarkCompleted => Ui.Transfer.ReconcileMarkCompleted,
        TransferReconciliationAction.MarkFailed => Ui.Transfer.ReconcileMarkFailed,
        TransferReconciliationAction.Cancel => Ui.Transfer.ReconcileCancel,
        _ => null
    };

    internal static string? Name(SyncIpcRunPhase phase) => phase switch
    {
        SyncIpcRunPhase.Pending => Ui.Sync.RunPhasePending,
        SyncIpcRunPhase.Scanning => Ui.Sync.RunPhaseScanning,
        SyncIpcRunPhase.Planning => Ui.Sync.RunPhasePlanning,
        SyncIpcRunPhase.AwaitingApproval => Ui.Sync.RunPhaseAwaitingApproval,
        SyncIpcRunPhase.Ready => Ui.Sync.RunPhaseReady,
        SyncIpcRunPhase.Executing => Ui.Sync.RunPhaseExecuting,
        SyncIpcRunPhase.Verifying => Ui.Sync.RunPhaseVerifying,
        SyncIpcRunPhase.CommittingBaseline => Ui.Sync.RunPhaseCommittingBaseline,
        SyncIpcRunPhase.BlockedConflict => Ui.Sync.RunPhaseBlockedConflict,
        SyncIpcRunPhase.BlockedDeletionGuard => Ui.Sync.RunPhaseBlockedDeletionGuard,
        SyncIpcRunPhase.BlockedEndpoint => Ui.Sync.RunPhaseBlockedEndpoint,
        SyncIpcRunPhase.BlockedCredential => Ui.Sync.RunPhaseBlockedCredential,
        SyncIpcRunPhase.BlockedTrust => Ui.Sync.RunPhaseBlockedTrust,
        SyncIpcRunPhase.Interrupted => Ui.Sync.RunPhaseInterrupted,
        SyncIpcRunPhase.NeedsReconciliation => Ui.Sync.RunPhaseNeedsReconciliation,
        SyncIpcRunPhase.Completed => Ui.Sync.RunPhaseCompleted,
        SyncIpcRunPhase.Failed => Ui.Sync.RunPhaseFailed,
        SyncIpcRunPhase.Cancelled => Ui.Sync.RunPhaseCancelled,
        _ => null
    };

    internal static string? Name(SyncIpcDispatchState state) => state switch
    {
        SyncIpcDispatchState.NotDispatched => Ui.Sync.DispatchNotDispatched,
        SyncIpcDispatchState.DurablyDispatched => Ui.Sync.DispatchDurablyDispatched,
        _ => null
    };

    internal static string? Name(SyncIpcPlanOperationKind kind) => kind switch
    {
        SyncIpcPlanOperationKind.Copy => Ui.Sync.PlanOperationCopy,
        SyncIpcPlanOperationKind.Delete => Ui.Sync.PlanOperationDelete,
        SyncIpcPlanOperationKind.CreateDirectory => Ui.Sync.PlanOperationCreateDirectory,
        _ => null
    };

    internal static string? Name(SyncIpcConflictState state) => state switch
    {
        SyncIpcConflictState.Unresolved => Ui.Sync.ConflictStateUnresolved,
        SyncIpcConflictState.Resolved => Ui.Sync.ConflictStateResolved,
        SyncIpcConflictState.Dismissed => Ui.Sync.ConflictStateDismissed,
        _ => null
    };

    internal static string? Name(KeyStoreMaterialKind kind) => kind switch
    {
        KeyStoreMaterialKind.Pkcs12Certificate => Ui.KeyStore.Certificate,
        KeyStoreMaterialKind.SshPrivateKey => Ui.KeyStore.SSHKey,
        _ => null
    };

    internal static string? Name(KeyStorePrivateKeyFormat format) => format switch
    {
        KeyStorePrivateKeyFormat.OpenSsh => Ui.KeyStore.OpenSSHOpensshKeyV1,
        KeyStorePrivateKeyFormat.Pem => Ui.KeyStore.LegacyPEM,
        KeyStorePrivateKeyFormat.Pkcs8 => Ui.KeyStore.PKCS8,
        _ => null
    };

    internal static string Describe(KeyStoreMaterialKind kind) => Name(kind) ?? kind.ToString();

    internal static string Describe(KeyStorePrivateKeyFormat format) => Name(format) ?? format.ToString();


    internal static string Describe(TransferQueueState state) => Name(state) ?? state.ToString();

    internal static string Describe(TransferQueueOperation operation) => Name(operation) ?? operation.ToString();

    internal static string Describe(TransferReconciliationAction action) => Name(action) ?? action.ToString();

    internal static string Describe(SyncIpcRunPhase phase) => Name(phase) ?? phase.ToString();

    internal static string Describe(SyncIpcDispatchState state) => Name(state) ?? state.ToString();

    internal static string Describe(SyncIpcPlanOperationKind kind) => Name(kind) ?? kind.ToString();

    internal static string Describe(SyncIpcConflictState state) => Name(state) ?? state.ToString();

    /// <summary>
    /// A weekday, in the shell's language.
    /// </summary>
    /// <remarks>
    /// Deliberately not translated in the shipped files. .NET already carries the day names for
    /// every culture it knows, so translating seven strings by hand would be more code, more to
    /// keep in step, and wrong in the languages StorageHub does not ship. It reads
    /// <see cref="Ui.Culture"/> rather than the current culture: the day belongs to the words on
    /// screen, not to the Windows regional format.
    ///
    /// Taken exactly as .NET gives it, with no case applied. Danish writes "mandag" in lower case
    /// and German writes "Montag" in upper, and each culture's data already says which — title
    /// casing them here would produce a capital Monday that is simply wrong in Danish.
    /// </remarks>
    internal static string Describe(DayOfWeek day) =>
        Ui.Culture.DateTimeFormat.GetDayName(day) is { Length: > 0 } name ? name : day.ToString();
}
