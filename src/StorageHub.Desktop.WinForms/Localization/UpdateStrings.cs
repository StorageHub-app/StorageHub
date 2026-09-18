using CodeLogic.Core.Localization;

namespace StorageHub.Desktop.Localization;

/// <summary>
/// The update checker and the background agent controls.
/// </summary>
/// <remarks>
/// Flat string properties only, and no property may be named <c>Culture</c>. See
/// <see cref="CommandStrings"/> for why.
/// </remarks>
[LocalizationSection("updates")]
internal sealed class UpdateStrings : LocalizationModelBase
{
    public string AgentDetail { get; set; } = "Agent detail";

    public string AutomaticChecksAreDisabledInSettingsYou { get; set; } = "Automatic checks are disabled in Settings. You can still check manually here.";

    public string AutomaticUpdatesAreTurnedOff { get; set; } = "Automatic updates are turned off";

    public string BackgroundAgent { get; set; } = "Background Agent";

    public string Cancel { get; set; } = "Cancel";

    public string ChannelStableReleasesAndReleaseCandidates { get; set; } = "Channel: stable releases and release candidates";

    public string ChannelStableReleasesOnly { get; set; } = "Channel: stable releases only";

    public string CheckForUpdates { get; set; } = "Check for updates";

    public string CheckWhetherANewerStorageHubReleaseIs { get; set; } = "Check whether a newer StorageHub release is available.";

    public string CheckingForUpdates { get; set; } = "Checking for updates…";

    public string DownloadUpdate { get; set; } = "Download update";

    public string InstalledVersion { get; set; } = "Installed version: ";

    public string Installing { get; set; } = "Installing…";

    public string LastAgentActionResult { get; set; } = "Last agent action result";

    public string LastReported { get; set; } = "Last reported: ";

    public string NoNewerReleaseWasFoundOnThe { get; set; } = "No newer release was found on the selected channel.";

    public string NoStatusHasBeenReportedYet { get; set; } = "No status has been reported yet.";

    public string PortableAndDeveloperBuildsAreNeverModified { get; set; } = "Portable and developer builds are never modified. Only an installed StorageHub can update itself.";

    public string Restart { get; set; } = "Restart";

    public string RestartAndInstall { get; set; } = "Restart and install";

    public string RestartingTheAgent { get; set; } = "Restarting the agent…";

    public string StartingTheAgent { get; set; } = "Starting the agent…";

    public string StoppingTheAgent { get; set; } = "Stopping the agent…";

    public string StorageHubUpdates { get; set; } = "StorageHub Updates";

    public string StorageHubCannotReachTheBackgroundAgentTransfers { get; set; } = "StorageHub cannot reach the background agent. Transfers, sync, and schedules are unavailable.";

    public string StorageHubCouldNotStartTheUpdaterReopen { get; set; } = "StorageHub could not start the updater. Reopen the application and try again.";

    public string StorageHubIsUpToDate { get; set; } = "StorageHub is up to date";

    public string TheAgentIsStarting { get; set; } = "The agent is starting.";

    public string TheAgentStartedButItsDurableState { get; set; } = "The agent started but its durable state is unavailable, so saved connections, the transfer queue, and sync are not usable.";

    public string TheDownloadIsIntegrityCheckedRestartingInstalls { get; set; } = "The download is integrity-checked. Restarting installs it silently; durable queued work is preserved.";

    public string TheReleaseHasNotBeenDownloadedYet { get; set; } = "The release has not been downloaded yet. Downloading does not change the installed version.";

    public string TheUpdateCouldNotBeCompleted { get; set; } = "The update could not be completed";

    public string ThisBuildCannotControlTheAgentProcess { get; set; } = "This build cannot control the agent process.";

    public string TransfersSyncAndSchedulesAreAvailable { get; set; } = "Transfers, sync, and schedules are available.";

    public string UpdateDetail { get; set; } = "Update detail";

    public string UpdateDownloadProgress { get; set; } = "Update download progress";

    public string Updates { get; set; } = "Updates";

    public string UpdatesAreNotAvailableForThisBuild { get; set; } = "Updates are not available for this build";

    public string Working { get; set; } = "Working…";

    // ------------------------------------------------------- agent control form
    public string AgentStart { get; set; } = "Start";

    public string AgentStop { get; set; } = "Stop";

    public string AgentStateUnknown { get; set; } = "\u25cf Agent state unknown";

    public string AgentStateRunning { get; set; } = "\u25cf Running";

    public string AgentStateRecovery { get; set; } = "\u25cf Running in recovery mode";

    public string AgentStateNotRunning { get; set; } = "\u25cf Not running";

    public string AgentStateStarting { get; set; } = "\u25cf Starting";

    /// <summary>{0} = the underlying error message.</summary>
    public string AgentNoResponseFormat { get; set; } = "The agent did not respond: {0}";

    /// <summary>{0} = active transfers, {1} = active sync runs.</summary>
    public string AgentActiveWorkFormat { get; set; } =
        "Active transfers: {0:N0}    Active sync runs: {1:N0}";
}
