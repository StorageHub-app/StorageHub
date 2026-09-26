using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StorageHub.Desktop.Framework;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The splash: a step at a time, warnings kept, and a failure that says what can be done.
/// </summary>
/// <remarks>
/// 2.0 opened without one, before the agent, the settings or the language were ready. These pin the
/// behaviour 1.x's StorageHubSplashForm had, which is what the port restores.
/// </remarks>
public class SplashTests
{
    [AvaloniaFact]
    public void EachStepIsSaidAndWarningsAccumulate()
    {
        var model = new SplashModel();

        model.Report(new BootStatus(BootStage.LoadingSettings, "A settings file was set aside."));
        model.Report(new BootStatus(BootStage.CheckingEnvironment, "The log folder is on a network drive."));
        model.Report(BootStatus.StartingAgent);

        Assert.Equal(BootText.Describe(BootStage.StartingAgent), model.Status);
        Assert.Contains("set aside", model.Details, StringComparison.Ordinal);
        Assert.Contains("network drive", model.Details, StringComparison.Ordinal);
        Assert.True(model.IsWorking);
    }

    /// <summary>Only the agent wait is long enough to need explaining.</summary>
    [AvaloniaFact]
    public void PatienceIsOnlyForTheAgent()
    {
        var model = new SplashModel();
        model.Report(BootStatus.LoadingLanguage);
        model.StillWaiting();
        Assert.DoesNotContain(BootText.Patience, model.Status, StringComparison.Ordinal);

        model.Report(BootStatus.StartingAgent);
        model.StillWaiting();
        Assert.Contains(BootText.Patience, model.Status, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void AFailureOffersRetryOnlyWhenRetryingCanHelp()
    {
        var model = new SplashModel();

        model.ShowFailure("The agent did not start.", "Agent status: StartupTimedOut", canRetry: true);
        Assert.True(model.IsFailed);
        Assert.True(model.RetryCommand.CanExecute(null));
        Assert.Contains("StartupTimedOut", model.CopyText, StringComparison.Ordinal);
        Assert.StartsWith($"StorageHub {DesktopApplicationVersion.Current}", model.CopyText, StringComparison.Ordinal);

        model.Resume(BootStatus.StartingAgent);
        Assert.False(model.IsFailed);
        Assert.False(model.RetryCommand.CanExecute(null));

        model.ShowFailure(BootText.DescribeFailure(BootStage.LoadingLanguage), "bad file", canRetry: false);
        Assert.False(model.RetryCommand.CanExecute(null));
    }

    /// <summary>The splash working and failed, for a human to look at. STORAGEHUB_SHOT_DIR keeps them.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSplashCanBePhotographed(bool failed)
    {
        var model = new SplashModel();
        model.Report(new BootStatus(BootStage.LoadingSettings, "Your settings were carried over. The old file was kept as settings.json.bak."));
        model.Report(BootStatus.StartingAgent);
        model.StillWaiting();
        if (failed)
        {
            model.ShowFailure(
                DesktopStartupPreflight.DescribeFailure(AgentEnsureStatus.StartupTimedOut),
                "Agent status: StartupTimedOut",
                canRetry: true);
        }

        var window = new SplashWindow { DataContext = model };
        window.Show();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, failed ? "splash-failed.png" : "splash.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }
}
