using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// Synchronization: the profile editor, the behaviour presets, the run review and the history.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
///
/// Control names, glob patterns and the <c>.storagehub</c> staging path are absent on purpose:
/// they identify or match things rather than describe them.
/// </remarks>
[LocalizationSection("sync")]
internal sealed class SyncStrings : LocalizationModelBase
{
    // ---------------------------------------------------------- profile editor
    public string EditorTitle { get; set; } = "Sync Profiles — StorageHub";

    public string EditorAccessibleName { get; set; } = "Sync Profile Editor";

    public string EditorAccessibleDescription { get; set; } =
        "Create, review, and run a persisted synchronization profile.";

    public string EditorHeading { get; set; } = "Design your synchronization";

    public string EditorSubheading { get; set; } =
        "Choose two saved locations, then select exactly how StorageHub should compare and converge them.";

    public string WorkflowAccessibleName { get; set; } = "Synchronization profile workflow";

    public string StepLocations { get; set; } = "1. Choose locations";

    public string StepBehavior { get; set; } = "2. Choose behavior";

    public string StepSafety { get; set; } = "3. Limits and filters";

    public string SavedProfile { get; set; } = "Saved synchronization profile";

    public string CreateNewProfile { get; set; } = "Create a new profile";

    public string ProfileNameHint { get; set; } = "Shown in Sync tasks and run history.";

    public string ProfileStatusAccessibleName { get; set; } = "Synchronization profile status";

    public string SaveProfile { get; set; } = "Save profile";

    public string ReviewAndRun { get; set; } = "Review & run";

    public string SwapLocations { get; set; } = "Swap A / B";

    public string Enabled { get; set; } = "Enabled";

    public string DisabledProfilesHint { get; set; } = "Disabled profiles can still be reviewed manually.";

    public string SafeSynchronization { get; set; } = "Synchronization profiles";

    public string SafeSynchronizationHint { get; set; } =
        "Save a profile, review the plan it produces, then dispatch that exact revision.";

    public string SelectPresetHint { get; set; } =
        "Pick a preset. Deletions always wait for your approval.";

    // ------------------------------------------------------------- locations
    public string SavedConnection { get; set; } = "Saved connection";

    public string FolderInsideConnection { get; set; } = "Folder inside connection";

    public string ConnectionRelativePath { get; set; } = "Connection-relative path (empty = root)";

    public string ConnectionRootHint { get; set; } = "Connection root (choose Browse for a subfolder)";

    public string EmptyMeansRoot { get; set; } = "Empty means the saved connection root.";

    public string BrowseFoldersHint { get; set; } = "Browse folders inside the selected saved connection.";

    public string LocationsRelativeHint { get; set; } =
        "Each folder stays relative to its saved connection; credentials and trust never leave that connection.";

    public string SelectConnectionFirst { get; set; } = "Select a connection first";

    public string ConnectionDisabled { get; set; } = "This saved connection is disabled";

    public string BrowseLocationA { get; set; } = "Browse Location A folders";

    public string BrowseLocationB { get; set; } = "Browse Location B folders";

    /// <summary>{0} = the location's name, A or B.</summary>
    public string SelectConnectionForLocationFormat { get; set; } =
        "Select a saved connection for {0} first.";

    /// <summary>{0} = the location's name, {1} = the chosen folder.</summary>
    public string LocationSelectedFormat { get; set; } = "{0} folder selected: {1}";

    /// <summary>{0} = the location's name, {1} = the connection's display name.</summary>
    public string LocationUsesRootFormat { get; set; } = "{0} uses the root of {1}.";

    /// <summary>{0} = the connection's folder path.</summary>
    public string ConnectionRootFormat { get; set; } = "Connection root: {0}";

    /// <summary>{0} = the connection's display name.</summary>
    public string DisabledConnectionFormat { get; set; } = "{0} (disabled)";

    // --------------------------------------------------------------- filters
    public string IncludeGlobs { get; set; } = "Include globs";

    public string IncludeGlobFilters { get; set; } = "Include glob filters";

    public string ExcludeGlobs { get; set; } = "Exclude globs";

    public string ExcludeGlobFilters { get; set; } = "Exclude glob filters";

    public string FiltersHint { get; set; } =
        "Filters are applied before planning. Excluded content is never changed, deleted, or added to the baseline.";

    public string StagingExcludedHint { get; set; } = "StorageHub staging paths are excluded by default.";

    public string HiddenContent { get; set; } = "Hidden content";

    public string IncludeHiddenFiles { get; set; } = "Include hidden files";

    public string HiddenFilesHint { get; set; } = "Clear to exclude dot-prefixed path segments.";

    // ---------------------------------------------------------------- safety
    public string ConflictPolicy { get; set; } = "Conflict policy";

    public string ConflictPolicyHint { get; set; } =
        "Block and review stops on a conflict. Keep both writes a second copy, and still needs approval.";

    public string BlockAndReview { get; set; } = "Block and review";

    public string KeepBoth { get; set; } = "Keep both (manual approval)";

    public string TransferBuffer { get; set; } = "Transfer buffer";

    public string TransferBufferHint { get; set; } = "Bounded from 1 byte through 1 MiB.";

    public string ItemLimitHint { get; set; } = "Stop when this item limit is exceeded.";

    public string PercentageLimitHint { get; set; } = "Stop when this percentage limit is exceeded.";

    public string AllowNonAtomicWrites { get; set; } = "Allow non-atomic destination writes";

    public string AllowNonAtomicWritesCompatibility { get; set; } =
        "Allow for FTP / SFTP compatibility";

    public string AllowNonAtomicWritesWarning { get; set; } =
        "WARNING: permits direct create/replace when a server cannot publish atomically. A concurrent destination change may be overwritten.";

    // ---------------------------------------------------------- editor status
    public string LoadingProfiles { get; set; } = "Loading saved profiles and connections…";

    public string SavingProfile { get; set; } = "Saving profile…";

    public string ScanningLocations { get; set; } =
        "Scanning both locations and preparing the sync plan…";

    public string PlanReady { get; set; } =
        "Sync plan ready. Review any approval-gated operations, then run it.";

    public string PlanReadyNonAtomic { get; set; } =
        "Sync plan ready. WARNING: this approved plan permits non-atomic destination writes.";

    public string EditorConnectsWhenShown { get; set; } =
        "The editor connects to the background agent when shown.";

    public string CompleteLocationsHint { get; set; } =
        "Choose two locations that do not overlap, and keep the limits and filters within range.";

    public string ProfileSaveFailed { get; set; } = "The sync profile could not be saved.";

    public string AgentIncompleteProfile { get; set; } = "The agent returned an incomplete profile.";

    public string AgentNoRevision { get; set; } = "The agent did not return the saved profile revision.";

    public string AgentNoPlanSummary { get; set; } = "The agent did not return the immutable plan summary.";

    public string AgentNoRun { get; set; } = "The agent did not return the generated synchronization run.";

    public string PlanRunMismatch { get; set; } = "The generated plan did not match its run summary.";

    public string ConnectionMissing { get; set; } =
        "A connection referenced by this profile is not available in the connection manager.";

    /// <summary>
    /// Shown for a named agent failure code instead of the agent's deliberately vague category
    /// text. The agent keeps messages generic so it never leaks endpoint detail; that is right for
    /// the wire but leaves the operator with "temporarily unavailable" for states they could fix
    /// in one click, so the known codes are named here.
    /// </summary>
    public string ProfileDisabledForSchedule { get; set; } =
        "This profile is disabled, so it will not run on a schedule. Tick Enabled to schedule it.";

    public string ProfileNotFound { get; set; } =
        "This sync profile no longer exists. Reload the profile list and try again.";

    public string EndpointUnauthorized { get; set; } =
        "A saved connection rejected its credentials. Open it in the connection manager and test it.";

    public string EndpointUnreachable { get; set; } =
        "A saved connection could not be reached. Test both connections, then try again.";

    public string LocationMissing { get; set; } =
        "A folder chosen for this profile no longer exists on its connection. Pick it again with Browse.";

    /// <summary>{0} = the agent's failure code.</summary>
    public string AgentFailureWithCodeFormat { get; set; } = "{0} ({1})";

    /// <summary>
    /// Warning shown after previewing a profile that is saved as disabled. The preview itself is
    /// allowed, so without this the operator would have no hint that nothing will ever run.
    /// </summary>
    public string PreviewedWhileDisabled { get; set; } =
        "Plan ready, but this profile is disabled and cannot run. Tick Enabled to run or schedule it.";

    public string ApprovalBlockedByDisabledProfile { get; set; } =
        "This profile is disabled, so its plan cannot be dispatched. Tick Enabled, then preview again.";

    /// <summary>{0} = how many profiles were loaded.</summary>
    public string ProfilesLoadedFormat { get; set; } = "Loaded {0} saved profile(s).";

    /// <summary>{0} = the profile revision.</summary>
    public string ProfileRevisionLoadedFormat { get; set; } = "Loaded profile revision {0}.";

    /// <summary>{0} = the underlying error message.</summary>
    public string ProfilesUnavailableFormat { get; set; } =
        "Connections loaded, but sync profiles are unavailable: {0}";

    /// <summary>{0} = the underlying error message.</summary>
    public string ConnectionsUnavailableFormat { get; set; } =
        "Sync profiles loaded, but connections are unavailable: {0}";

    /// <summary>{0} and {1} = the two underlying error messages.</summary>
    public string BothUnavailableFormat { get; set; } =
        "Profiles and connections are unavailable: {0} {1}";

    // ------------------------------------------------------- behaviour picker
    public string BehaviorAccessibleName { get; set; } = "Synchronization behavior";

    public string BehaviorHint { get; set; } =
        "Choose a preset. Each one sets the direction and what happens to changed and deleted files.";

    public string BehaviorSelect { get; set; } = "Select this behavior";

    public string BehaviorSelected { get; set; } = "Selected";

    public string BehaviorUnknown { get; set; } = "Unknown synchronization behavior.";

    public string BehaviorCompareOnly { get; set; } = "Compare only";

    public string BehaviorCompareOnlySummary { get; set; } =
        "Scan both locations and build a plan without changing either.";

    public string BehaviorCopyNewAtoB { get; set; } = "Copy new files A to B";

    public string BehaviorCopyNewAtoBSummary { get; set; } =
        "Existing files at Location B stay untouched.";

    public string BehaviorCopyNewBtoA { get; set; } = "Copy new files B to A";

    public string BehaviorCopyNewBtoASummary { get; set; } =
        "Existing files at Location A stay untouched.";

    public string BehaviorUpdateAtoB { get; set; } = "Update A to B";

    public string BehaviorUpdateAtoBSummary { get; set; } =
        "Copy new files and replace changed files at B.";

    public string BehaviorUpdateBtoA { get; set; } = "Update B to A";

    public string BehaviorUpdateBtoASummary { get; set; } =
        "Copy new files and replace changed files at A.";

    public string BehaviorMirrorAtoB { get; set; } = "Mirror A to B";

    public string BehaviorMirrorAtoBSummary { get; set; } =
        "Make B match A, including deletions after approval.";

    public string BehaviorMirrorBtoA { get; set; } = "Mirror B to A";

    public string BehaviorMirrorBtoASummary { get; set; } =
        "Make A match B, including deletions after approval.";

    public string BehaviorTwoWay { get; set; } = "Two-way sync";

    public string BehaviorTwoWaySummary { get; set; } =
        "Merge new and changed files using the last complete baseline.";

    public string BehaviorTwoWayDeletions { get; set; } = "Two-way with deletions";

    public string BehaviorTwoWayDeletionsSummary { get; set; } =
        "Merge changes and propagate deletions after approval.";

    public string DirectionOneWayFromA { get; set; } = "One-way from Location A";

    public string DirectionOneWayFromB { get; set; } = "One-way from Location B";

    public string DirectionBoth { get; set; } = "Both locations";

    public string BadgeReadOnly { get; set; } = "READ ONLY";

    public string BadgeCreateOnly { get; set; } = "CREATE ONLY";

    public string BadgeSafeUpdate { get; set; } = "UPDATE";

    public string BadgeDeletions { get; set; } = "DELETIONS";

    public string BadgeMerge { get; set; } = "MERGE";

    public string BadgeDefault { get; set; } = "DEFAULT";

    /// <summary>{0} = the preset's summary, {1} = its badge.</summary>
    public string BehaviorAccessibleFormat { get; set; } = "{0} {1}.";

    // -------------------------------------------------------- location picker
    public string PickerConnectionRoot { get; set; } = "Connection root";

    public string PickerFolder { get; set; } = "Folder";

    public string PickerPath { get; set; } = "Path";

    public string PickerRoot { get; set; } = "Root";

    public string PickerRefresh { get; set; } = "Refresh";

    public string PickerLoadMore { get; set; } = "Load more";

    public string PickerSelectFolder { get; set; } = "Select this folder";

    public string PickerLoadingFolders { get; set; } = "Loading folders...";

    public string PickerLoadingMoreFolders { get; set; } = "Loading more folders...";

    public string PickerStatusAccessibleName { get; set; } = "Folder browser status";

    public string PickerFoldersAccessibleName { get; set; } = "Folders in the current connection path";

    public string PickerAddressAccessibleName { get; set; } = "Connection-relative folder path";

    public string PickerInvalidPath { get; set; } = "Enter a valid connection-relative folder path.";

    public string PickerDisplayLimit { get; set; } =
        "This folder has too many entries to list. Enter a narrower path.";

    /// <summary>{0} = the location's name.</summary>
    public string PickerTitleFormat { get; set; } = "Choose {0} folder";

    /// <summary>{0} = the location's name, {1} = the connection's display name.</summary>
    public string PickerTitleWithConnectionFormat { get; set; } = "Choose {0} folder - {1}";

    /// <summary>{0} = the connection's display name.</summary>
    public string PickerFoldersOfFormat { get; set; } = "{0} folders";

    /// <summary>{0} = the provider name.</summary>
    public string PickerBrowseHintFormat { get; set; } =
        "Browse this saved {0} connection. The selected path is stored relative to its root.";

    /// <summary>{0} = how many folders are shown.</summary>
    public string PickerRootCountFormat { get; set; } = "Connection root - {0} folder(s) shown.";

    /// <summary>{0} = the current path, {1} = how many folders are shown.</summary>
    public string PickerPathCountFormat { get; set; } = "{0} - {1} folder(s) shown.";

    // ------------------------------------------------------------ run review
    public string RunReviewAccessibleName { get; set; } = "Synchronization run review";

    public string RunReviewAccessibleDescription { get; set; } =
        "Immutable sync operations, conflicts, approval, and live run status.";

    public string PlanTab { get; set; } = "Plan";

    public string ConflictsTab { get; set; } = "Conflicts";

    public string PlanOperations { get; set; } = "Immutable plan operations";

    public string PlanConflicts { get; set; } = "Synchronization conflicts";

    public string PlanDetails { get; set; } = "Sync plan details";

    public string NoPlanLoaded { get; set; } = "No sync plan loaded";

    public string NoRunLoaded { get; set; } = "No sync run is loaded.";

    public string ChooseReviewAndRun { get; set; } =
        "Choose Review & run from a sync profile, or load an existing run.";

    public string ApproveAndDispatch { get; set; } = "Approve & dispatch";

    public string ApproveHint { get; set; } =
        "Durably enqueue the exact reviewed plan. This does not report provider execution complete.";

    public string AwaitingApproval { get; set; } = "Awaiting approval. No provider changes have started.";

    public string NotAwaitingApproval { get; set; } = "This run is not awaiting approval.";

    public string RefreshStatus { get; set; } = "Refresh status";

    public string RunStatusAccessibleName { get; set; } = "SyncRunStatus";

    public string LoadNextOperations { get; set; } = "Load next operations";

    public string LoadNextConflicts { get; set; } = "Load next conflicts";

    public string PlanPageMismatch { get; set; } =
        "The plan page no longer matches the reviewed immutable plan.";

    public string DispatchNotConfirmed { get; set; } = "The agent did not confirm durable sync dispatch.";

    public string IncompleteSyncResponse { get; set; } = "The agent returned an incomplete sync response.";

    public string DestructiveApprovalRequired { get; set; } = "Deletes data — approval required";

    public string Guarded { get; set; } = "Not required";

    public string ColumnSequence { get; set; } = "Sequence";

    public string ColumnKind { get; set; } = "Kind";

    public string ColumnPath { get; set; } = "Path";

    public string ColumnFromLocation { get; set; } = "From location";

    public string ColumnToLocation { get; set; } = "To location";

    public string ColumnBytes { get; set; } = "Bytes";

    public string ColumnExpectedBytes { get; set; } = "Expected bytes";

    public string ColumnSafety { get; set; } = "Approval";

    public string ColumnSafeReason { get; set; } = "Reason";

    public string ColumnReason { get; set; } = "Reason";

    public string ColumnAction { get; set; } = "Action";

    public string ColumnState { get; set; } = "State";

    public string PhaseQueued { get; set; } = "Synchronization is queued in the background agent.";

    public string PhaseSynchronizing { get; set; } = "Synchronizing provider content…";

    public string PhaseVerifying { get; set; } = "Provider changes finished; verifying both locations…";

    public string PhaseCommitting { get; set; } = "Locations verified; committing the new baseline…";

    public string PhaseCompleted { get; set; } =
        "Synchronization completed and both locations were verified.";

    public string PhaseFailed { get; set; } =
        "Synchronization failed. Review the run status before retrying.";

    public string PhaseCancelled { get; set; } = "Synchronization was cancelled before completion.";

    public string PhaseUncertain { get; set; } =
        "Synchronization stopped with uncertain provider state and requires reconciliation.";

    public string PhaseRunning { get; set; } =
        "Synchronization is still running in the background. Use Refresh status for the latest result.";

    /// <summary>{0} = the run's phase.</summary>
    public string RunPhaseFormat { get; set; } =
        "Run phase: {0}. No provider execution completion is inferred from this status.";

    /// <summary>{0} = the run id, {1} = its phase, {2} = its revision.</summary>
    public string RunHeaderFormat { get; set; } = "Run {0:D} · {1} · revision {2}";

    // ------------------------------------------------------------ run history
    public string RunsAccessibleName { get; set; } = "Synchronization history and run review";

    public string RunsTitle { get; set; } = "Synchronization runs";

    public string RunsDescription { get; set; } =
        "Load, review, and durably dispatch a known synchronization run.";

    public string RunsHistoryAccessibleName { get; set; } = "Durable synchronization run history";

    public string RunsStatusAccessibleName { get; set; } =
        "Current synchronization run history and review status";

    public string RunIdLabel { get; set; } = "Run ID";

    public string RunIdAccessibleName { get; set; } = "Synchronization run ID";

    public string LoadRun { get; set; } = "Load run";

    public string RefreshHistory { get; set; } = "Refresh history";

    public string NextPage { get; set; } = "Next page";

    public string EnterRunId { get; set; } = "Enter a synchronization run ID.";

    public string EnterValidRunId { get; set; } = "Enter a valid non-empty run ID.";

    public string RunLoadFailed { get; set; } = "The sync run could not be loaded.";

    public string LoadingHistory { get; set; } = "Loading durable run history…";

    public string LoadingPlan { get; set; } = "Loading immutable synchronization plan…";

    public string RunLoaded { get; set; } =
        "Run loaded. Status polling is active while this view is visible.";

    public string NoRunsYet { get; set; } = "No synchronization runs yet.";

    public string ColumnRun { get; set; } = "Run";

    public string ColumnPhase { get; set; } = "Phase";

    public string ColumnUpdated { get; set; } = "Updated";

    public string ColumnDispatch { get; set; } = "Dispatch";

    /// <summary>{0} = how many runs are shown.</summary>
    public string ShowingRunsFormat { get; set; } = "Showing {0} durable run(s).";

    // -------------------------------------------------------- tasks overview
    public string TasksAccessibleDescription { get; set; } =
        "Saved synchronization profiles and durable run history from the background agent.";

    public string SavedTasks { get; set; } = "Saved sync tasks";

    public string RunHistoryAndReview { get; set; } = "Run history and review";

    public string NewSyncProfile { get; set; } = "New sync profile";

    public string TasksRefresh { get; set; } = "Refresh";

    public string TasksRefreshing { get; set; } = "Refreshing sync tasks...";

    public string NoTasksConfigured { get; set; } = "No sync tasks configured";

    public string NoRunOpened { get; set; } = "No sync run opened in this session";

    public string EnabledTasks { get; set; } = "Enabled tasks";

    public string DisabledTasks { get; set; } = "Disabled tasks";

    public string LastSyncs { get; set; } = "Last syncs";

    public string RunsThisSession { get; set; } = "Runs this session";

    public string TaskDisabled { get; set; } = "Disabled";

    public string ColumnName { get; set; } = "Name";

    public string ColumnBehavior { get; set; } = "Behavior";

    // ------------------------------------------------- editor labels and state
    public string LocationA { get; set; } = "Location A";

    public string LocationB { get; set; } = "Location B";

    public string LocationAConnection { get; set; } = "Location A connection";

    public string LocationBConnection { get; set; } = "Location B connection";

    public string LocationAFolderAccessibleName { get; set; } =
        "Location A connection-relative folder";

    public string LocationBFolderAccessibleName { get; set; } =
        "Location B connection-relative folder";

    /// <summary>{0} = the location's letter.</summary>
    public string LocationHeadingFormat { get; set; } = "Location {0}";

    public string ProfileName { get; set; } = "Profile name";

    public string ProfileState { get; set; } = "Profile state";

    public string NewProfile { get; set; } = "New profile";

    public string NewProfileTitle { get; set; } = "New Sync Profile";

    public string NewProfileDraft { get; set; } =
        "New profile draft. Saving does not change either provider.";

    public string NoSavedProfileYet { get; set; } =
        "No saved profile yet. Configure two saved connections, then choose Review & run.";

    public string PlanAndRun { get; set; } = "Plan & run";

    public string PreparingPlan { get; set; } = "Preparing sync plan…";

    public string NonAtomicWrites { get; set; } = "Non-atomic writes";

    public string MaximumDeletes { get; set; } = "Maximum deletes";

    public string MaximumBaselinePercent { get; set; } = "Maximum baseline %";

    public string GlobHint { get; set; } = "Optional; one forward-slash glob per line.";

    // ------------------------------------------------------- tasks tab chrome
    public string TasksTitle { get; set; } = "Sync tasks";

    public string TasksViews { get; set; } = "Sync task views";

    public string TasksOverviewAccessibleName { get; set; } = "Synchronization task overview";

    public string TasksHeading { get; set; } = "Synchronization tasks";

    public string TasksDeferred { get; set; } = "Sync tasks will load when this tab is opened.";

    public string TasksRunHistory { get; set; } = "Synchronization run history and review";

    public string UseReviewAndRun { get; set; } = "Use Review & run or open an existing run";

    public string RepeatedHistoryToken { get; set; } =
        "The agent returned a repeated sync-history page token.";

    /// <summary>{0} = the time of the refresh, {1} = how many runs are shown.</summary>
    public string TasksUpdatedFormat { get; set; } = "Updated {0:t}. Showing {1:N0} durable run(s).";

    /// <summary>{0} = the revision the profile was saved at.</summary>
    public string ProfileSavedFormat { get; set; } =
        "Profile saved at revision {0}. No provider changes were requested.";

    /// <summary>{0} = the revision the profile was saved at.</summary>
    public string ProfileSavedNonAtomicFormat { get; set; } =
        "Profile saved at revision {0}. WARNING: non-atomic destination writes are enabled.";

    /// <summary>{0} = the expected type's name.</summary>
    public string SelectValidValueFormat { get; set; } = "Select a valid {0} value.";

    public string Browse { get; set; } = "Browse...";

    public string ProfileTab { get; set; } = "Profile";

    // ----------------------------------------------------------------- run phases
    /// <remarks>
    /// One word per <c>SyncRunPhase</c>, read through <c>UiEnumNames</c>. The blocked phases name
    /// what is blocking rather than that something is: a run stopped by a deletion guard and one
    /// stopped by an unverified host key need different actions from the reader.
    /// </remarks>
    public string RunPhasePending { get; set; } = "Pending";

    public string RunPhaseScanning { get; set; } = "Scanning";

    public string RunPhasePlanning { get; set; } = "Planning";

    public string RunPhaseAwaitingApproval { get; set; } = "Awaiting approval";

    public string RunPhaseReady { get; set; } = "Ready";

    public string RunPhaseExecuting { get; set; } = "Executing";

    public string RunPhaseVerifying { get; set; } = "Verifying";

    public string RunPhaseCommittingBaseline { get; set; } = "Committing baseline";

    public string RunPhaseBlockedConflict { get; set; } = "Blocked: conflict";

    public string RunPhaseBlockedDeletionGuard { get; set; } = "Blocked: deletion guard";

    public string RunPhaseBlockedEndpoint { get; set; } = "Blocked: endpoint";

    public string RunPhaseBlockedCredential { get; set; } = "Blocked: credential";

    public string RunPhaseBlockedTrust { get; set; } = "Blocked: trust";

    public string RunPhaseInterrupted { get; set; } = "Interrupted";

    public string RunPhaseNeedsReconciliation { get; set; } = "Needs reconciliation";

    public string RunPhaseCompleted { get; set; } = "Completed";

    public string RunPhaseFailed { get; set; } = "Failed";

    public string RunPhaseCancelled { get; set; } = "Cancelled";
}
