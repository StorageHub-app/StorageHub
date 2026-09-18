using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Framework;
using WinFormsApplicationContext = System.Windows.Forms.ApplicationContext;

namespace StorageHub.Desktop;

/// <summary>
/// Runs the startup sequence behind the splash, then hands the message loop to the main window.
/// </summary>
/// <remarks>
/// Startup used to block the STA thread on <c>EnsureAgentAsync().GetAwaiter().GetResult()</c> before
/// any window existed. Here the splash goes up first and the same work is awaited from its
/// <see cref="Form.Shown"/> handler, so the message loop pumps throughout: the window paints, the
/// progress bar animates, and Alt+F4 during the agent wait actually cancels rather than being
/// queued behind a blocked thread.
/// </remarks>
internal sealed class DesktopBootContext : WinFormsApplicationContext
{
    /// <summary>How long the finished splash is held before the main window takes over.</summary>
    private static readonly TimeSpan BrandingHold = TimeSpan.FromSeconds(2);

    private readonly PackagedDesktopLifecycle _lifecycle;
    private readonly bool _explorerDropBrokerAvailable;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StorageHubSplashForm _splash;
    private readonly DesktopFrameworkHost _framework;

    private DesktopConfigStore? _preferencesStore;
    private bool _settingsReady;
    private bool _frameworkStarted;
    private bool _handedOff;

    internal DesktopBootContext(PackagedDesktopLifecycle lifecycle, bool explorerDropBrokerAvailable)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _explorerDropBrokerAvailable = explorerDropBrokerAvailable;
        _framework = new DesktopFrameworkHost(DesktopFrameworkPaths.Resolve());

        _splash = new StorageHubSplashForm();
        _splash.QuitRequested += QuitRequested;
        _splash.RetryRequested += RetryRequested;
        _splash.FormClosing += SplashClosing;
        _splash.Shown += SplashShown;

        // Until the main window exists the splash is the application's window: closing it ends the
        // message loop, which is what makes Quit and the close box work without special casing.
        MainForm = _splash;
    }

    internal int ExitCode { get; private set; }

    private async void SplashShown(object? sender, EventArgs e) => await BootAsync().ConfigureAwait(true);

    private async void RetryRequested(object? sender, EventArgs e)
    {
        _splash.ResumeProgress(BootStatus.StartingAgent);
        await BootAsync().ConfigureAwait(true);
    }

    private void QuitRequested(object? sender, EventArgs e)
    {
        ExitCode = 1;
        _splash.Close();
    }

    private void SplashClosing(object? sender, FormClosingEventArgs e)
    {
        // A user who closes the splash mid-start means it: cancel the work rather than letting the
        // agent wait run on against a window that is going away.
        if (!_handedOff)
        {
            _lifetime.Cancel();
        }
    }

    /// <summary>
    /// The whole startup sequence. Every failure lands on the splash rather than escaping to the
    /// unhandled-exception handler, because this is invoked from an <c>async void</c> event.
    /// </summary>
    private async Task BootAsync()
    {
        try
        {
            var progress = new Progress<BootStatus>(_splash.Report);

            // Before the framework, not after: CodeLogic reads these files during ConfigureAsync
            // and throws on one it cannot parse, so they have to be screened and repaired first.
            // It is also where the configured language comes from, which the framework needs.
            var preferencesStore = _preferencesStore ??= DesktopConfigStore.CreateDefault();
            if (!_settingsReady)
            {
                _splash.Report(BootStatus.LoadingSettings);
                var preflight = await Task.Run(preferencesStore.Preflight, _lifetime.Token).ConfigureAwait(true);
                _settingsReady = true;

                foreach (var finding in DescribeSettings(preflight))
                {
                    _splash.Report(new BootStatus(BootStage.LoadingSettings, finding));
                }
            }

            var preferences = preferencesStore.Load();

            if (!_frameworkStarted)
            {
                await _framework
                    .StartAsync(progress, preferences.Language, _lifetime.Token)
                    .ConfigureAwait(true);
                _frameworkStarted = true;

                foreach (var warning in _framework.Warnings)
                {
                    _splash.Report(new BootStatus(BootStage.CheckingEnvironment, warning));
                }
            }

            // The last point at which the framework still accepts a colour-mode switch: it refuses
            // once a message loop owns the decision, and the main window is built next.
            DesktopAppearanceService.SetAppearance(preferences.Appearance);

            _splash.Report(BootStatus.StartingAgent);
            var agent = await _lifecycle.EnsureAgentAsync(_lifetime.Token).ConfigureAwait(true);
            if (!agent.IsReady)
            {
                _splash.ShowFailure(
                    DesktopStartupPreflight.DescribeFailure(agent.Status),
                    $"Agent status: {agent.Status}",
                    canRetry: true);
                return;
            }

            _splash.Report(BootStatus.Opening);

            // Deliberate, and the only delay here that is not work: on a warm start the agent
            // answers immediately and the splash appears and vanishes inside a single frame, which
            // reads as a flicker rather than as a product opening. Held long enough for the mark to
            // register. Cancellable, so closing the splash during the hold still exits at once
            // rather than opening a window the user has already dismissed.
            await Task.Delay(BrandingHold, _lifetime.Token).ConfigureAwait(true);

            await HandOffAsync(preferencesStore).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The user closed the splash. The window is already going away.
        }
        catch (DesktopBootException error)
        {
            _splash.ShowFailure(DescribeBootFailure(error), error.Message, canRetry: false);
        }
        catch (Exception error)
        {
            _splash.ShowFailure(
                "StorageHub could not start.",
                $"{error.GetType().Name}: {error.Message}",
                canRetry: false);
        }
    }

    /// <summary>
    /// Builds the main window while the splash is still up — its constructor is heavy enough to be
    /// visible — then swaps the two before closing the splash, so the message loop never runs
    /// without a main form.
    /// </summary>
    /// <summary>
    /// The lines the splash shows about the settings, in the order a user would want them: what was
    /// carried over, then what was thrown away, then what was corrected. All of them are warnings
    /// rather than failures -- none of them stops StorageHub opening.
    /// </summary>
    private static IEnumerable<string> DescribeSettings(ConfigPreflightReport report)
    {
        if (report.MigratedFrom is { } migrated)
        {
            yield return Ui.Format(Ui.Dialogs.SettingsMigratedFormat, migrated);
        }

        foreach (var rejected in report.Quarantined)
        {
            yield return Ui.Format(Ui.Dialogs.SettingsQuarantinedFormat, rejected);
        }

        if (report.Repaired)
        {
            yield return Ui.Dialogs.SettingsRepaired;
        }
    }

    private async Task HandOffAsync(DesktopConfigStore preferencesStore)
    {
        var main = new MainForm(
            preferencesStore,
            updateEngineFactory: null,
            _lifecycle,
            _explorerDropBrokerAvailable);

        _handedOff = true;
        MainForm = main;
        main.Show();

        await _splash.FadeOutAsync().ConfigureAwait(true);
        _splash.Shown -= SplashShown;
        _splash.FormClosing -= SplashClosing;
        _splash.Close();
        _splash.Dispose();
    }

    private static string DescribeBootFailure(DesktopBootException error) => error.Stage switch
    {
        BootStage.PreparingData => "StorageHub could not prepare its data folder.",
        BootStage.StartingFramework => "StorageHub could not start its application framework.",
        BootStage.CheckingEnvironment => "StorageHub found a problem with its folders.",
        BootStage.LoadingSettings => "StorageHub could not read its settings.",
        BootStage.LoadingLanguage => "StorageHub could not load its configuration and language files.",
        _ => "StorageHub could not start."
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _framework.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _lifetime.Dispose();
        }

        base.Dispose(disposing);
    }
}
