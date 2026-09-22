using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>What the update window's single action button does next.</summary>
/// <remarks>
/// Declared inside UpdateCheckerForm until the port, which meant the state machine it names could
/// only be reached from a Form. Keeping check, download and install behind one button is what
/// spares the flow a wizard per step, and that decision belongs beside the updater rather than
/// inside whichever window happens to be drawing it.
/// </remarks>
internal enum UpdateAction
{
    Check = 1,
    Download = 2,
    Restart = 3,
    None = 4
}

/// <summary>
/// The update window's text and next action, as pure functions of the updater's state.
/// </summary>
/// <remarks>
/// These were static methods on the form, which is why they were already testable and why moving
/// them costs nothing. The Avalonia guided update view needs exactly the same three answers, and
/// having them in one place is what stops the two shells drifting into saying different things
/// about the same state.
/// </remarks>
internal static class DesktopUpdatePresentation
{
    /// <summary>
    /// What the one action button should do next. Keeping this a pure function of state is what
    /// lets the window drive check, download, and install without a separate wizard for each.
    /// </summary>
    internal static UpdateAction NextAction(DesktopUpdateState state) => state switch
    {
        DesktopUpdateState.UpdateAvailable => UpdateAction.Download,
        DesktopUpdateState.ReadyToRestart => UpdateAction.Restart,
        DesktopUpdateState.Checking or DesktopUpdateState.Downloading or
            DesktopUpdateState.Installing => UpdateAction.None,
        DesktopUpdateState.Disabled or DesktopUpdateState.Unavailable => UpdateAction.None,
        _ => UpdateAction.Check
    };

    internal static string DescribeHeadline(DesktopUpdateSnapshot snapshot) => snapshot.State switch
    {
        DesktopUpdateState.Checking => Ui.Updates.CheckingForUpdates,
        DesktopUpdateState.UpdateAvailable =>
            Ui.Format(Ui.Updates.UpdateAvailableFormat, snapshot.Version),
        DesktopUpdateState.Downloading =>
            Ui.Format(Ui.Updates.UpdateDownloadingFormat, snapshot.Version),
        DesktopUpdateState.ReadyToRestart =>
            Ui.Format(Ui.Updates.UpdateReadyToInstallFormat, snapshot.Version),
        DesktopUpdateState.Installing => Ui.Updates.Installing,
        DesktopUpdateState.UpToDate => Ui.Updates.StorageHubIsUpToDate,
        DesktopUpdateState.Unavailable => Ui.Updates.UpdatesAreNotAvailableForThisBuild,
        DesktopUpdateState.Disabled => Ui.Updates.AutomaticUpdatesAreTurnedOff,
        DesktopUpdateState.Failed => Ui.Updates.TheUpdateCouldNotBeCompleted,
        _ => Ui.Updates.Updates
    };

    internal static string DescribeDetail(DesktopUpdateSnapshot snapshot) => snapshot.State switch
    {
        DesktopUpdateState.UpdateAvailable =>
            Ui.Updates.TheReleaseHasNotBeenDownloadedYet,
        DesktopUpdateState.ReadyToRestart =>
            Ui.Updates.TheDownloadIsIntegrityCheckedRestartingInstalls,
        DesktopUpdateState.UpToDate =>
            Ui.Updates.NoNewerReleaseWasFoundOnThe,
        DesktopUpdateState.Unavailable =>
            Ui.Updates.PortableAndDeveloperBuildsAreNeverModified,
        DesktopUpdateState.Disabled =>
            Ui.Updates.AutomaticChecksAreDisabledInSettingsYou,
        DesktopUpdateState.Failed => snapshot.Message,
        DesktopUpdateState.Idle => Ui.Updates.CheckWhetherANewerStorageHubReleaseIs,
        _ => snapshot.Message
    };
}
