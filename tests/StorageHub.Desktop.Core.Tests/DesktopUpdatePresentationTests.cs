namespace StorageHub.Desktop.Tests;

/// <summary>
/// The updater types are internal by design, and xUnit requires public test classes, so the states
/// under test are enumerated inside each method rather than passed as theory parameters.
///
/// These were UpdateCheckerForm's, and were static there for the same reason they are pure
/// here: what a state should say and what the button should do next is not a drawing question.
/// </summary>
public sealed class DesktopUpdatePresentationTests
{
    [Fact]
    public void TheOneActionButtonFollowsTheUpdateState()
    {
        Assert.Equal(UpdateAction.Check, DesktopUpdatePresentation.NextAction(DesktopUpdateState.Idle));
        Assert.Equal(UpdateAction.Check, DesktopUpdatePresentation.NextAction(DesktopUpdateState.UpToDate));
        Assert.Equal(UpdateAction.Check, DesktopUpdatePresentation.NextAction(DesktopUpdateState.Failed));
        Assert.Equal(UpdateAction.Download, DesktopUpdatePresentation.NextAction(DesktopUpdateState.UpdateAvailable));
        Assert.Equal(UpdateAction.Restart, DesktopUpdatePresentation.NextAction(DesktopUpdateState.ReadyToRestart));
    }

    [Fact]
    public void WorkInFlightOffersNoAction()
    {
        // Re-entering a check or download mid-flight is what the old message-box chain allowed.
        Assert.Equal(UpdateAction.None, DesktopUpdatePresentation.NextAction(DesktopUpdateState.Checking));
        Assert.Equal(UpdateAction.None, DesktopUpdatePresentation.NextAction(DesktopUpdateState.Downloading));
        Assert.Equal(UpdateAction.None, DesktopUpdatePresentation.NextAction(DesktopUpdateState.Installing));
    }

    [Fact]
    public void BuildsThatCannotUpdateOfferNoAction()
    {
        Assert.Equal(UpdateAction.None, DesktopUpdatePresentation.NextAction(DesktopUpdateState.Unavailable));
        Assert.Equal(UpdateAction.None, DesktopUpdatePresentation.NextAction(DesktopUpdateState.Disabled));
    }

    [Fact]
    public void EveryStateHasAHeadlineAndAnExplanation()
    {
        foreach (var state in Enum.GetValues<DesktopUpdateState>())
        {
            var snapshot = new DesktopUpdateSnapshot(state, "engine message", "1.2.3", 42);

            Assert.False(
                string.IsNullOrWhiteSpace(DesktopUpdatePresentation.DescribeHeadline(snapshot)),
                $"No headline for {state}.");
            Assert.False(
                string.IsNullOrWhiteSpace(DesktopUpdatePresentation.DescribeDetail(snapshot)),
                $"No detail for {state}.");
        }
    }

    [Fact]
    public void EveryStateResolvesToAKnownAction()
    {
        foreach (var state in Enum.GetValues<DesktopUpdateState>())
        {
            Assert.True(Enum.IsDefined(DesktopUpdatePresentation.NextAction(state)), $"No action for {state}.");
        }
    }

    [Fact]
    public void AnAvailableUpdateNamesItsVersion()
    {
        var snapshot = new DesktopUpdateSnapshot(
            DesktopUpdateState.UpdateAvailable, "ignored", "1.0.0-rc.59", null);

        Assert.Contains("1.0.0-rc.59", DesktopUpdatePresentation.DescribeHeadline(snapshot), StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadingDoesNotClaimTheVersionIsInstalled()
    {
        // Downloading changes nothing until the restart, and the wording has to say so.
        var available = DesktopUpdatePresentation.DescribeDetail(new DesktopUpdateSnapshot(
            DesktopUpdateState.UpdateAvailable, "ignored", "1.0.0-rc.59"));

        Assert.Contains("does not change the installed version", available, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFailureSurfacesTheEngineReason()
    {
        var snapshot = new DesktopUpdateSnapshot(
            DesktopUpdateState.Failed, "The release feed was unreachable.", null);

        Assert.Equal("The release feed was unreachable.", DesktopUpdatePresentation.DescribeDetail(snapshot));
    }

    [Fact]
    public void APortableBuildExplainsWhyItCannotUpdate()
    {
        var detail = DesktopUpdatePresentation.DescribeDetail(
            new DesktopUpdateSnapshot(DesktopUpdateState.Unavailable, "ignored"));

        Assert.Contains("Portable", detail, StringComparison.OrdinalIgnoreCase);
    }
}
