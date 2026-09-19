using StorageHub.Agent;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// An update moves the application and leaves a service-hosted agent on the version it was staged
/// with, because the updater is unelevated and the staged copy is machine-owned. The dialog has to
/// say so while there is still a decision to make.
///
/// Written as separate facts rather than a theory over the state: the state enum is internal, and
/// xUnit requires a public test class, which cannot take an internal type as a parameter.
/// </summary>
public sealed class UpdateCheckerNoticeTests
{
    [Fact]
    public void AnAvailableUpdateSaysTheServiceIsNotIncluded() =>
        AssertNoticed(DesktopUpdateState.UpdateAvailable);

    [Fact]
    public void ADownloadingUpdateSaysTheServiceIsNotIncluded() =>
        AssertNoticed(DesktopUpdateState.Downloading);

    [Fact]
    public void AnUpdateReadyToInstallSaysTheServiceIsNotIncluded() =>
        AssertNoticed(DesktopUpdateState.ReadyToRestart);

    [Fact]
    public void AUserSessionAgentNeedsNoNoticeBecauseItUpdatesWithTheApplication() =>
        Assert.Null(UpdateCheckerForm.DescribeServiceAgentNotice(
            Snapshot(DesktopUpdateState.UpdateAvailable),
            AgentHostMode.UserSession));

    [Fact]
    public void AnAppSessionAgentNeedsNoNoticeEither() =>
        Assert.Null(UpdateCheckerForm.DescribeServiceAgentNotice(
            Snapshot(DesktopUpdateState.UpdateAvailable),
            AgentHostMode.AppSession));

    [Fact]
    public void NothingIsSaidWhenThereIsNoUpdateToInstall() =>
        Assert.Null(UpdateCheckerForm.DescribeServiceAgentNotice(
            Snapshot(DesktopUpdateState.UpToDate),
            AgentHostMode.WindowsService));

    [Fact]
    public void NothingIsSaidBeforeAnythingHasBeenChecked() =>
        Assert.Null(UpdateCheckerForm.DescribeServiceAgentNotice(
            Snapshot(DesktopUpdateState.Idle),
            AgentHostMode.WindowsService));

    private static void AssertNoticed(DesktopUpdateState state)
    {
        var notice = UpdateCheckerForm.DescribeServiceAgentNotice(
            Snapshot(state),
            AgentHostMode.WindowsService);

        Assert.NotNull(notice);
        Assert.Contains("administrator", notice, StringComparison.OrdinalIgnoreCase);
    }

    private static DesktopUpdateSnapshot Snapshot(DesktopUpdateState state) =>
        DesktopUpdateSnapshot.Initial with { State = state, Version = "1.4.6" };
}
