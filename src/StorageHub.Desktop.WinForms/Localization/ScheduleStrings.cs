using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The synchronization schedule manager.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
///
/// Cron expressions and time formats are absent on purpose: they are syntax the agent parses,
/// not words a reader interprets, and translating one would produce an unschedulable profile.
/// </remarks>
[LocalizationSection("schedules")]
internal sealed class ScheduleStrings : LocalizationModelBase
{
    // ------------------------------------------------------------------ window
    public string WindowTitle { get; set; } = "Synchronization Schedules — StorageHub";

    public string WindowAccessibleName { get; set; } = "Synchronization schedule manager";

    public string WindowAccessibleDescription { get; set; } =
        "Manage schedules that prepare a plan, or run one when nothing needs approval.";

    public string Close { get; set; } = "Close";

    public string Refresh { get; set; } = "Refresh";

    public string Delete { get; set; } = "Delete";

    public string Enable { get; set; } = "Enable";

    public string Disable { get; set; } = "Disable";

    public string SaveSchedule { get; set; } = "Save schedule";

    public string RunSchedule { get; set; } = "Run schedule";

    public string NewSchedule { get; set; } = "New schedule";

    // ----------------------------------------------------------------- fields
    public string Schedule { get; set; } = "Schedule";

    public string SyncSchedule { get; set; } = "Sync schedule";

    public string SyncProfile { get; set; } = "Sync profile";

    public string Profile { get; set; } = "Profile";

    public string ScheduledProfile { get; set; } = "Scheduled sync profile";

    public string Repeats { get; set; } = "Repeats";

    public string Day { get; set; } = "Day";

    public string DayOfWeek { get; set; } = "Day of week";

    public string DayOfMonth { get; set; } = "Day of month";

    public string TimeZone { get; set; } = "Time zone";

    public string Cron { get; set; } = "Cron";

    public string CronField { get; set; } = "Five-field cron expression";

    public string ExecutionMode { get; set; } = "Execution mode";

    public string Overlap { get; set; } = "Overlap";

    public string MisfireGrace { get; set; } = "Misfire grace (min)";

    public string MisfireGraceAccessibleName { get; set; } = "Misfire grace in minutes";

    public string NextRun { get; set; } = "Next run";

    public string CurrentState { get; set; } = "Current state";

    public string State { get; set; } = "State";

    public string Next { get; set; } = "Next";

    public string Enabled { get; set; } = "Enabled";

    public string Yes { get; set; } = "Yes";

    // ------------------------------------------------------------- recurrence
    public string BuilderAccessibleName { get; set; } = "Friendly recurring schedule builder";

    public string BuilderHint { get; set; } =
        "Choose a familiar recurrence and local time. StorageHub handles the underlying schedule and daylight-saving rules.";

    public string EveryDay { get; set; } = "Every day";

    public string EveryWeekday { get; set; } = "Every weekday (Monday–Friday)";

    public string EveryWeek { get; set; } = "Every week";

    public string EveryMonth { get; set; } = "Every month";

    public string CustomAdvanced { get; set; } = "Custom schedule (advanced)";

    public string AdvancedRecurrence { get; set; } = "Advanced recurring schedule.";

    public string CustomCronOnly { get; set; } =
        "Custom cron is available only under Custom schedule (advanced).";

    public string CronHint { get; set; } =
        "Select a profile and enter a bounded five-field cron schedule and time zone.";

    public string TimeZoneHint { get; set; } =
        "Offsets reflect today's daylight-saving rules; the named region controls future changes.";

    // ------------------------------------------------------------------ modes
    public string ReviewOnly { get; set; } = "Review only";

    public string SafeAutomatic { get; set; } = "Safe automatic";

    public string ReviewOnlyDescription { get; set; } =
        "Scans both locations and prepares a plan. Nothing is written until you approve it.";

    public string SafeAutomaticDescription { get; set; } =
        "Runs on its own when the plan only adds or replaces files. Anything that deletes, conflicts, or needs a permission that has changed waits for you instead.";

    public string SafeAutomaticHint { get; set; } =
        "A plan containing a deletion never runs unattended, so a mirror profile always waits for approval.";

    public string CoalesceOption { get; set; } =
        "Keep one coalesced occurrence while the profile is already running";

    public string CoalesceHint { get; set; } = "At most one coalesced occurrence is retained.";

    public string MisfireHint { get; set; } = "Occurrences that expire outside this window are skipped.";

    // ----------------------------------------------------------------- status
    public string LoadingSchedules { get; set; } = "Loading schedules and sync profiles…";

    public string RefreshingSchedules { get; set; } = "Refreshing schedules…";

    public string DeletingSchedule { get; set; } = "Deleting schedule…";

    public string EnablingSchedule { get; set; } = "Enabling schedule…";

    public string DisablingSchedule { get; set; } = "Disabling schedule…";

    public string NewScheduleDraft { get; set; } =
        "New schedule draft. Safe automatic is selected, and enabled is off until you turn it on.";

    public string NewSchedulesDisabled { get; set; } =
        "New schedules remain disabled until explicitly enabled.";

    public string NoSchedulesYet { get; set; } =
        "No schedules yet. New ones start on Safe automatic and stay off until you enable them.";

    public string OnlySavedProfiles { get; set; } = "Only saved profiles can be scheduled.";

    public string SelectScheduleFirst { get; set; } = "Select a saved schedule first.";

    public string ManagerConnectsWhenShown { get; set; } =
        "The manager connects to the background agent when shown.";

    public string RunActive { get; set; } = "Run active";

    public string RunActiveBlocks { get; set; } =
        "A scheduled run is active; update, disable, and delete are blocked.";

    public string ActiveRunsBlock { get; set; } = "Active runs block destructive management changes.";

    public string NoActiveRun { get; set; } = "No active scheduled run.";

    public string NotScheduledUntilSaved { get; set; } = "Not scheduled until saved and enabled.";

    public string NoFutureRun { get; set; } = "No future run is scheduled.";

    public string CalculatedAfterSave { get; set; } = "Calculated by the agent after save or enable.";

    public string Idle { get; set; } = "Idle";

    public string IdleNoOutcome { get; set; } = "Idle · no recorded outcome.";

    public string ScheduleChangeFailed { get; set; } = "The schedule could not be changed.";

    public string ScheduleDeleteFailed { get; set; } = "The schedule could not be deleted.";

    public string IncompleteSchedule { get; set; } = "The agent returned an incomplete schedule.";

    public string NoScheduleRevision { get; set; } =
        "The agent did not return the saved schedule revision.";

    /// <summary>{0} = how many schedules were loaded.</summary>
    public string SchedulesLoadedFormat { get; set; } = "Loaded {0} schedule(s).";

    /// <summary>{0} = the last outcome, {1} = an error code or empty.</summary>
    public string IdleOutcomeFormat { get; set; } = "Idle · last outcome: {0}{1}";

    /// <summary>{0} = the day of the week, {1} = the local time.</summary>
    public string RunsWeeklyFormat { get; set; } = "Runs every {0} at {1}.";

    /// <summary>{0} = the day of the month, {1} = the local time.</summary>
    public string RunsMonthlyFormat { get; set; } = "Runs on day {0} of every month at {1}.";

    /// <summary>{0} = the time zone identifier that could not be resolved.</summary>
    public string UnavailableRegionFormat { get; set; } = "Unavailable region · {0}";

    /// <summary>{0} = the profile's display name.</summary>
    public string DisabledProfileFormat { get; set; } = "{0} (disabled)";

    // ------------------------------------------------- accessible names and prose
    public string SavedSchedules { get; set; } = "Saved synchronization schedules";

    public string ListAndEditor { get; set; } = "Schedule list and editor";

    public string ManagementStatus { get; set; } = "Schedule management status";

    public string FrequencyAccessibleName { get; set; } = "Schedule frequency";

    public string TimeAccessibleName { get; set; } = "Schedule time";

    public string TimeZoneAccessibleName { get; set; } = "Schedule time zone";

    public string ExecutionModeAccessibleName { get; set; } = "Schedule execution mode";

    public string ExecutionModeNotice { get; set; } = "Schedule execution mode notice";

    public string SavingSchedule { get; set; } = "Saving schedule…";

    public string ScheduleDeleted { get; set; } = "Schedule deleted. Existing run history remains available.";

    public string ScheduleDisabled { get; set; } = "Schedule disabled. No future occurrence is queued.";

    public string ScheduleEnabled { get; set; } = "Schedule enabled for background synchronization.";

    public string SavedReviewOnly { get; set; } =
        "Schedule saved in Review only mode; it will prepare a plan without changing either location.";

    public string SavedSafeAutomatic { get; set; } =
        "Schedule saved. Plans that only add or replace files will run on their own.";

    /// <summary>{0} = the local time.</summary>
    public string RunsDailyFormat { get; set; } = "Runs every day at {0}.";

    /// <summary>{0} = the local time.</summary>
    public string RunsWeekdaysFormat { get; set; } = "Runs Monday through Friday at {0}.";
}
