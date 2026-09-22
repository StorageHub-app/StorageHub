using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The update window.
/// </summary>
/// <remarks>
/// What each state says and what the button does next are pure functions, pinned in
/// DesktopUpdatePresentationTests. What is checked here is the seam: that one button carries the
/// whole flow, that the progress bar and the Cancel label appear only while something is
/// downloading, and that a failure is reported rather than escaping.
/// </remarks>
public sealed class UpdateCheckerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"storagehub-updates-{Guid.NewGuid():N}");

    private DesktopUpdater? _updater;

    public void Dispose()
    {
        _updater?.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>An untouched window offers a check, and says what a check is for.</summary>
    [AvaloniaFact]
    public void AFreshWindowOffersACheck()
    {
        using var model = Model();

        Assert.Equal(Ui.Updates.CheckForUpdates, model.PrimaryLabel);
        Assert.True(model.CanPrimary);
        Assert.False(model.IsDownloading);
        Assert.Equal(Ui.Updates.CheckWhetherANewerStorageHubReleaseIs, model.Detail);
        Assert.Equal(Ui.Dialogs.ButtonClose, model.CloseLabel);
    }

    /// <summary>
    /// The installed version and the channel are shown whatever the state.
    /// </summary>
    /// <remarks>
    /// The channel is a setting rather than a state, and it decides which releases this copy is
    /// ever offered, so the window says which one is in force rather than leaving it to Settings.
    /// </remarks>
    [AvaloniaFact]
    public void TheInstalledVersionAndChannelAreShown()
    {
        var updater = Updater();
        using var model = new UpdateCheckerModel(updater, snapshot => { });

        Assert.StartsWith(Ui.Updates.InstalledVersion, UpdateCheckerModel.Installed, StringComparison.Ordinal);
        Assert.Contains(DesktopApplicationVersion.Current, UpdateCheckerModel.Installed, StringComparison.Ordinal);

        updater.SavePreferences(updater.Preferences with { IncludePrereleases = true });
        model.Show(model.Snapshot);
        Assert.Equal(Ui.Updates.ChannelStableReleasesAndReleaseCandidates, model.Channel);

        updater.SavePreferences(updater.Preferences with { IncludePrereleases = false });
        model.Show(model.Snapshot);
        Assert.Equal(Ui.Updates.ChannelStableReleasesOnly, model.Channel);
    }

    /// <summary>
    /// One button carries the flow: check, then download, then install.
    /// </summary>
    /// <remarks>
    /// The states are pushed rather than produced by a real updater, because what is under test is
    /// the window's reading of them. Reaching them for real needs a feed, an installer and a
    /// restart, which is what the engine tests and the release checks cover.
    /// </remarks>
    [AvaloniaFact]
    public void TheOneButtonFollowsTheFlow()
    {
        using var model = Model();

        model.Show(Snapshot(DesktopUpdateState.UpdateAvailable, version: "2.0.1"));
        Assert.Equal(Ui.Updates.DownloadUpdate, model.PrimaryLabel);
        Assert.True(model.CanPrimary);
        Assert.True(model.IsWarning);

        model.Show(Snapshot(DesktopUpdateState.ReadyToRestart, version: "2.0.1"));
        Assert.Equal(Ui.Updates.RestartAndInstall, model.PrimaryLabel);
        Assert.True(model.CanPrimary);
        Assert.True(model.IsSuccess);
    }

    /// <summary>
    /// While something is running the button says so and cannot be pressed again.
    /// </summary>
    /// <remarks>
    /// A loop rather than a theory because the states are internal, and a public test method
    /// cannot take one as a parameter.
    /// </remarks>
    [AvaloniaFact]
    public void WhileWorkingTheButtonIsDim()
    {
        using var model = Model();

        foreach (var state in new[]
        {
            DesktopUpdateState.Checking,
            DesktopUpdateState.Downloading,
            DesktopUpdateState.Installing
        })
        {
            model.Show(Snapshot(state, version: "2.0.1"));

            Assert.Equal(Ui.Updates.Working, model.PrimaryLabel);
            Assert.False(model.CanPrimary);
            Assert.False(model.PrimaryCommand.CanExecute(null));
        }
    }

    /// <summary>
    /// The progress bar and the Cancel label belong to the download and to nothing else.
    /// </summary>
    /// <remarks>
    /// The label mattered: the WinForms window set it to the literal "Close", so in Danish and
    /// German the button changed language halfway through a download.
    /// </remarks>
    [AvaloniaFact]
    public void ProgressAndCancelBelongToTheDownload()
    {
        using var model = Model();
        Assert.False(model.IsDownloading);

        model.Show(Snapshot(DesktopUpdateState.Downloading, version: "2.0.1", percent: 42));

        Assert.True(model.IsDownloading);
        Assert.Equal(42, model.ProgressPercent);
        Assert.Equal(Ui.Updates.Cancel, model.CloseLabel);

        model.Show(Snapshot(DesktopUpdateState.ReadyToRestart, version: "2.0.1"));

        Assert.False(model.IsDownloading);
        Assert.Equal(Ui.Dialogs.ButtonClose, model.CloseLabel);
    }

    /// <summary>Progress reaches the bar directly, so nonsense is clamped rather than drawn.</summary>
    [AvaloniaTheory]
    [InlineData(null, 0)]
    [InlineData(-5, 0)]
    [InlineData(250, 100)]
    [InlineData(37, 37)]
    public void ProgressIsClamped(int? reported, int expected)
    {
        using var model = Model();

        model.Show(Snapshot(DesktopUpdateState.Downloading, version: "2.0.1", percent: reported));

        Assert.Equal(expected, model.ProgressPercent);
    }

    /// <summary>A state with nothing to offer says so rather than dimming with no reason.</summary>
    [AvaloniaFact]
    public void AStateWithNothingToOfferStillExplainsItself()
    {
        using var model = Model();

        foreach (var state in new[] { DesktopUpdateState.Unavailable, DesktopUpdateState.Disabled })
        {
            model.Show(Snapshot(state));

            Assert.False(model.CanPrimary);
            Assert.False(string.IsNullOrWhiteSpace(model.Headline));
            Assert.False(string.IsNullOrWhiteSpace(model.Detail));
        }
    }

    /// <summary>A failure reads as one, and carries whatever the updater said.</summary>
    [AvaloniaFact]
    public void AFailureIsReportedWithItsReason()
    {
        using var model = Model();

        model.Show(Snapshot(DesktopUpdateState.Failed) with { Message = "The feed could not be reached." });

        Assert.True(model.IsDanger);
        Assert.Equal(Ui.Updates.TheUpdateCouldNotBeCompleted, model.Headline);
        Assert.Equal("The feed could not be reached.", model.Detail);

        // And a check is offered again, rather than leaving the window with nothing to do.
        Assert.True(model.CanPrimary);
        Assert.Equal(Ui.Updates.CheckForUpdates, model.PrimaryLabel);
    }

    /// <summary>
    /// The updater outlives the window, so a closed one stops listening.
    /// </summary>
    /// <remarks>
    /// Otherwise the updater's own automatic check would keep a disposed window's model alive and
    /// go on raising property changes at it.
    /// </remarks>
    [AvaloniaFact]
    public void AClosedWindowStopsListening()
    {
        var model = new UpdateCheckerModel(Updater(), snapshot => { });
        var changes = 0;
        model.PropertyChanged += (_, _) => changes++;

        model.Show(Snapshot(DesktopUpdateState.UpdateAvailable, version: "2.0.1"));
        Assert.True(changes > 0);

        model.Dispose();
        var afterClose = changes;
        model.Show(Snapshot(DesktopUpdateState.ReadyToRestart, version: "2.0.1"));

        Assert.Equal(afterClose, changes);
    }

    /// <summary>Disposing twice is what a window that closes after a cancel does.</summary>
    [AvaloniaFact]
    public void DisposingTwiceIsHarmless()
    {
        var model = Model();
        model.Dispose();
        model.Dispose();
    }

    /// <summary>
    /// Photographs the window in both appearances, for a human to look at.
    /// </summary>
    /// <remarks>
    /// Mid-download, because that is the state with the most on screen: a headline, a detail, a
    /// progress bar, and a button that has changed both its label and its meaning. Set
    /// STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheWindowCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        using var model = Model();
        // With the message the updater actually reports, so the photograph shows the real layout
        // rather than one with the detail line empty.
        model.Show(Snapshot(DesktopUpdateState.Downloading, version: "2.0.1", percent: 64) with
        {
            Message = "Downloading storagehub-2.0.1-x64.msi — 64 MB of 100 MB."
        });

        var window = new UpdateCheckerWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(520, 320));
        window.Arrange(new Rect(0, 0, 520, 320));
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(
            Path.Combine(directory, $"update-checker-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    private static DesktopUpdateSnapshot Snapshot(
        DesktopUpdateState state, string? version = null, int? percent = null) =>
        new(state, string.Empty, version, percent);

    /// <summary>
    /// A model over a real updater pointed at a temporary settings file.
    /// </summary>
    /// <remarks>
    /// The status callback is replaced so nothing depends on a dispatcher loop being drained;
    /// these tests push snapshots directly, which is the same thing the dispatcher would deliver.
    /// </remarks>
    private UpdateCheckerModel Model() => new(Updater(), snapshot => { });

    /// <summary>One updater per test, disposed with it, over a temporary settings file.</summary>
    private DesktopUpdater Updater()
    {
        if (_updater is not null) return _updater;
        Directory.CreateDirectory(_directory);
        _updater = new DesktopUpdater(new DesktopConfigStore(_directory));
        return _updater;
    }
}
