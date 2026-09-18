using StorageHub.Desktop.Framework;

namespace StorageHub.Desktop.Tests;

public sealed class SplashFormTests
{
    [Fact]
    public void EveryBootStageHasWordingOfItsOwn()
    {
        var described = Enum.GetValues<BootStage>()
            .Select(StorageHubSplashForm.Describe)
            .ToArray();

        Assert.All(described, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.Equal(described.Length, described.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ConstructsAndDisposesOnStaWithoutBeingShown() =>
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var splash = new StorageHubSplashForm();
            Assert.Equal(FormBorderStyle.None, splash.FormBorderStyle);

            // A borderless window that cannot be reached with Alt+Tab is how a slow start gets
            // reported as a hang.
            Assert.True(splash.ShowInTaskbar);
        });

    [Fact]
    public void ReportingAStageMovesThroughEveryStepWithoutThrowing() =>
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var splash = new StorageHubSplashForm();
            foreach (var stage in Enum.GetValues<BootStage>())
            {
                splash.Report(new BootStatus(stage));
            }
        });

    /// <summary>
    /// A quarantined settings file and a validator warning are both worth seeing, so a second
    /// warning must not overwrite the first.
    /// </summary>
    [Fact]
    public void WarningsAccumulateRatherThanReplacingOneAnother() =>
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var splash = new StorageHubSplashForm();
            splash.Report(new BootStatus(BootStage.CheckingEnvironment, "First warning."));
            splash.Report(new BootStatus(BootStage.LoadingSettings, "Second warning."));

            var detail = FindLabel(splash, "Startup details");
            Assert.Contains("First warning.", detail.Text, StringComparison.Ordinal);
            Assert.Contains("Second warning.", detail.Text, StringComparison.Ordinal);
        });

    [Fact]
    public void AFailureOffersRetryOnlyWhenTheStepCanBeRetried() =>
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var retryable = new StorageHubSplashForm();
            retryable.ShowFailure("The agent is not ready.", "Agent status: LaunchFailed", canRetry: true);
            Assert.Contains(Buttons(retryable), button => button.Text == "Retry");

            using var fatal = new StorageHubSplashForm();
            fatal.ShowFailure("The framework did not start.", "Something broke.", canRetry: false);
            Assert.DoesNotContain(Buttons(fatal), button => button.Text == "Retry");

            // Whatever the failure, the user can always read it and leave.
            Assert.Contains(Buttons(fatal), button => button.Text == "Quit");
            Assert.Contains(Buttons(fatal), button => button.Text == "Copy details");
        });

    [Fact]
    public void ResumingAfterAFailureClearsTheErrorButKeepsEarlierWarnings() =>
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var splash = new StorageHubSplashForm();
            splash.Report(new BootStatus(BootStage.CheckingEnvironment, "Disk is nearly full."));
            splash.ShowFailure("The agent is not ready.", "Agent status: StartupTimedOut", canRetry: true);

            splash.ResumeProgress(BootStatus.StartingAgent);

            Assert.Empty(Buttons(splash));
            var status = FindLabel(splash, "Startup status");
            Assert.Equal(StorageHubSplashForm.Describe(BootStage.StartingAgent), status.Text);

            var detail = FindLabel(splash, "Startup details");
            Assert.Contains("Disk is nearly full.", detail.Text, StringComparison.Ordinal);
        });

    private static Label FindLabel(Control root, string accessibleName) =>
        Descendants(root)
            .OfType<Label>()
            .Single(label => string.Equals(label.AccessibleName, accessibleName, StringComparison.Ordinal));

    private static Button[] Buttons(Control root) =>
        Descendants(root).OfType<Button>().ToArray();

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
