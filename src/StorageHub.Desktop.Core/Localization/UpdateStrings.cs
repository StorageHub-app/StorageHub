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
    public string AgentActionNotSupported { get; set; } = "That agent action is not supported.";

    public string AgentCommandTimedOut { get; set; } = "The command did not finish in time.";

    public string AgentCouldNotBeControlled { get; set; } = "The agent could not be controlled.";

    public string AgentCouldNotBeLaunched { get; set; } = "The agent could not be launched.";

    public string AgentDidNotBecomeAvailable { get; set; } = "The agent did not become available in time.";

    public string AgentDidNotConfirmShutdown { get; set; } = "The agent did not confirm shutdown within the timeout.";

    public string AgentExecutableMissing { get; set; } = "The agent executable is missing. Repair or reinstall StorageHub.";

    public string AgentRestarted { get; set; } = "The agent restarted.";

    public string AgentRestartedWithoutShutdown { get; set; } = "The agent restarted, but the previous instance did not confirm shutdown.";

    public string AgentStarted { get; set; } = "The agent started.";

    public string AgentStopped { get; set; } = "The agent stopped.";

    public string AgentWasAlreadyRunning { get; set; } = "The agent was already running.";

    public string AgentStart { get; set; } = "Start";

    public string AgentStop { get; set; } = "Stop";

    public string AgentStateUnknown { get; set; } = "\u25cf Agent state unknown";

    public string AgentStateRunning { get; set; } = "\u25cf Running";

    public string AgentStateRecovery { get; set; } = "\u25cf Running in recovery mode";

    public string AgentStateNotRunning { get; set; } = "\u25cf Not running";

    public string AgentStateStarting { get; set; } = "\u25cf Starting";

    // ------------------------------------------------------- installation check
    public string CheckInstallation { get; set; } = "Check installation";

    public string InstallationCheckTitle { get; set; } = "Check installation \u2014 StorageHub";

    public string InstallationCheckAgain { get; set; } = "Check again";

    public string InstallationChecking { get; set; } = "Checking the installation…";

    public string InstallationCheckCouldNotRun { get; set; } = "The check could not run.";

    public string InstallationCheckFailed { get; set; } = "Check failed";

    public string InstallationModeUserSession { get; set; } = "signed-in session";

    public string InstallationModeAppSession { get; set; } = "only while StorageHub is open";

    public string InstallationModeSystemd { get; set; } = "started by systemd";

    public string InstallationRepairButton { get; set; } = "Repair";

    public string InstallationRepaired { get; set; } = "Repaired";

    public string InstallationCouldNotRepair { get; set; } = "Could not repair";

    public string InstallationNothingToRepair { get; set; } = "Nothing to repair.";

    public string InstallationPresent { get; set; } = "Present.";

    public string InstallationDataDirectory { get; set; } = "Data directory";

    public string InstallationDataDirectoryMissing { get; set; } =
        "Missing. The agent keeps its database, vault and logs here and cannot start without it.";

    public string InstallationDataDirectoryDenied { get; set; } =
        "This account cannot open it, and the agent runs as this account, so it cannot open it either.";

    public string InstallationDataDirectoryDeniedByService { get; set; } =
        "This account cannot open it, so the agent, which runs as this account, cannot either. The " +
        "Windows service of an earlier StorageHub keeps this folder to itself; remove that service " +
        "and its folder first.";

    public string InstallationDatabase { get; set; } = "Database";

    public string InstallationDatabaseMissing { get; set; } =
        "Not created yet. Expected for a new installation; unexpected if connections were saved before.";

    public string InstallationDatabaseEmpty { get; set; } =
        "Present but empty, which means saved connections, schedules and queue history are not readable.";

    public string InstallationDatabaseDenied { get; set; } =
        "This account cannot read it, and the agent runs as this account, so saved connections, " +
        "schedules and queue history are out of its reach.";

    public string InstallationLegacyDatabase { get; set; } = "Database from an earlier version";

    public string InstallationLegacyDatabaseDetail { get; set; } =
        "A populated database from an earlier version of StorageHub is still on disk. Nothing reads it " +
        "now, so anything saved into it is not missing; it is only out of reach where it is.";

    public string InstallationAgentProgram { get; set; } = "Agent program";

    public string InstallationAgentProgramMissing { get; set; } =
        "Missing, so StorageHub cannot start the agent. Repair or reinstall StorageHub.";

    public string InstallationAgentUnit { get; set; } = "Agent unit";

    public string InstallationAgentUnitPresent { get; set; } = "Installed for systemd.";

    public string InstallationAgentUnitMissing { get; set; } =
        "No systemd user unit for the agent was found, so StorageHub cannot start it. Reinstall the StorageHub package.";

    public string InstallationAgentReachable { get; set; } = "Agent reachable";

    public string InstallationAgentAnswers { get; set; } = "Answering.";

    public string InstallationAgentStarting { get; set; } = "Answering, but still starting.";

    public string InstallationAgentSilent { get; set; } =
        "Nothing is answering where the agent listens, which is what StorageHub reports as the agent " +
        "not becoming ready.";

    /// <summary>{0} = how the agent is run.</summary>
    public string InstallationSummaryOkFormat { get; set; } = "Everything checked out. Agent mode: {0}.";

    /// <summary>{0} = how the agent is run.</summary>
    public string InstallationSummaryWarningFormat { get; set; } =
        "Works, with things worth fixing. Agent mode: {0}.";

    /// <summary>{0} = how the agent is run.</summary>
    public string InstallationSummaryProblemFormat { get; set; } = "Something is wrong. Agent mode: {0}.";

    /// <summary>{0} = the size in megabytes.</summary>
    public string InstallationSizeMegabytesFormat { get; set; } = "{0:0.#} MB.";

    /// <summary>{0} = the size in kilobytes.</summary>
    public string InstallationSizeKilobytesFormat { get; set; } = "{0:N0} KB.";

    /// <summary>{0} = the directory.</summary>
    public string InstallationDirectoryCreatedFormat { get; set; } = "Created {0}.";

    /// <summary>{0} = the directory, {1} = why it could not be created.</summary>
    public string InstallationDirectoryNotCreatedFormat { get; set; } = "Could not create {0}: {1}";

    /// <summary>{0} = the release version.</summary>
    public string UpdateAvailableFormat { get; set; } = "StorageHub {0} is available";

    /// <summary>{0} = the release version.</summary>
    public string UpdateDownloadingFormat { get; set; } = "Downloading StorageHub {0}…";

    /// <summary>{0} = the release version.</summary>
    public string UpdateReadyToInstallFormat { get; set; } = "StorageHub {0} is ready to install";

    /// <summary>{0} = the underlying error message.</summary>
    public string UpdateCheckFailedFormat { get; set; } = "The update check failed: {0}";

    /// <summary>{0} = the underlying error message.</summary>
    public string AgentNoResponseFormat { get; set; } = "The agent did not respond: {0}";

    /// <summary>{0} = active transfers, {1} = active sync runs.</summary>
    public string AgentActiveWorkFormat { get; set; } =
        "Active transfers: {0:N0}    Active sync runs: {1:N0}";
}
