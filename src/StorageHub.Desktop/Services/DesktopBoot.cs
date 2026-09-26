using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Framework;
using StorageHub.Desktop.Views;

namespace StorageHub.Desktop.Services;

/// <summary>
/// Runs the startup sequence behind the splash, then hands over to the main window.
/// </summary>
/// <remarks>
/// <para>
/// 1.x's DesktopBootContext, in the same order: settings (so a damaged file is found before
/// anything reads it), the framework and the language, the agent, then the shell. Each step is
/// reported on the splash; anything that stops startup lands there too, with Retry where retrying
/// can help. Nothing escapes to the unhandled-exception path, because this runs from an event.
/// </para>
/// <para>
/// The splash is the application's window until the shell exists, so closing it ends the process
/// -- which is what makes Quit and the close box work without special cases.
/// </para>
/// </remarks>
internal sealed class DesktopBoot : IDisposable
{
    /// <summary>
    /// How long the finished splash is held. On a warm start everything answers at once and the
    /// splash would appear and vanish inside a frame, which reads as a flicker rather than as a
    /// product opening. 1.x held it for two seconds.
    /// </summary>
    private static readonly TimeSpan BrandingHold = TimeSpan.FromSeconds(2);

    /// <summary>After this long on the agent, the splash says that it can take a while.</summary>
    private static readonly TimeSpan PatienceDelay = TimeSpan.FromSeconds(3);

    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly Func<IClassicDesktopStyleApplicationLifetime, MainWindow> _openShell;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SplashModel _model = new();
    private readonly SplashWindow _splash;
    private DesktopFrameworkHost? _framework;
    private bool _settingsReady;
    private bool _frameworkStarted;
    private bool _handedOff;

    private DesktopBoot(
        IClassicDesktopStyleApplicationLifetime desktop,
        Func<IClassicDesktopStyleApplicationLifetime, MainWindow> openShell)
    {
        _desktop = desktop;
        _openShell = openShell;
        _splash = new SplashWindow { DataContext = _model };
        _model.QuitRequested += (_, _) => Quit();
        _model.RetryRequested += async (_, _) =>
        {
            _model.Resume(BootStatus.StartingAgent);
            await RunAsync().ConfigureAwait(true);
        };
        _splash.Closing += (_, _) =>
        {
            // Closing the splash mid-start means it: stop waiting rather than opening a window the
            // person has already dismissed.
            if (!_handedOff) _lifetime.Cancel();
        };
        _splash.Opened += async (_, _) => await RunAsync().ConfigureAwait(true);

        // The framework is stopped with the process, as 1.x did; it is only running if it started.
        desktop.Exit += (_, _) =>
        {
            _framework?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Dispose();
        };
    }

    public void Dispose() => _lifetime.Dispose();

    /// <summary>Puts the splash up as the application's window; the rest follows when it opens.</summary>
    internal static void Start(
        IClassicDesktopStyleApplicationLifetime desktop,
        Func<IClassicDesktopStyleApplicationLifetime, MainWindow> openShell)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(openShell);
        var boot = new DesktopBoot(desktop, openShell);
        desktop.MainWindow = boot._splash;
    }

    private async Task RunAsync()
    {
        try
        {
            var progress = new Progress<BootStatus>(_model.Report);

            // Settings before the framework: CodeLogic reads these files while it configures and
            // throws on one it cannot parse, so they are screened and repaired first. The scheme
            // is applied here too, so the shell opens in it rather than changing after it shows.
            DesktopUpdatePreferences preferences;
            if (!_settingsReady)
            {
                _model.Report(BootStatus.LoadingSettings);
                var report = SettingsWindow.ApplySavedScheme();
                _settingsReady = true;
                foreach (var finding in report.Describe())
                {
                    _model.Report(new BootStatus(BootStage.LoadingSettings, finding));
                }
            }

            preferences = LoadPreferences();

            if (!_frameworkStarted)
            {
                _framework ??= new DesktopFrameworkHost(DesktopFrameworkPaths.Resolve());
                await _framework.StartAsync(progress, preferences.Language, _lifetime.Token).ConfigureAwait(true);
                _frameworkStarted = true;
                foreach (var warning in _framework.Warnings)
                {
                    _model.Report(new BootStatus(BootStage.CheckingEnvironment, warning));
                }
            }

            // Every launch, as 1.x did: it is what lets a file dragged out of a connection land in
            // Explorer, and a registration another install replaced is put back rather than left
            // pointing at a DLL that has moved.
            if (OperatingSystem.IsWindows())
            {
                _ = ExplorerDropBrokerInstaller.EnsureRegistered(AppContext.BaseDirectory);
            }

            _model.Report(BootStatus.StartingAgent);
            var patience = new DispatcherTimer(PatienceDelay, DispatcherPriority.Background, (sender, _) =>
            {
                ((DispatcherTimer)sender!).Stop();
                _model.StillWaiting();
            });
            patience.Start();
            var agent = await DesktopAgentStartup.EnsureAsync(_lifetime.Token).ConfigureAwait(true);
            patience.Stop();
            if (!agent.IsReady)
            {
                _model.ShowFailure(
                    DesktopStartupPreflight.DescribeFailure(agent.Status),
                    $"Agent status: {agent.Status}",
                    canRetry: true);
                return;
            }

            _model.Report(BootStatus.Opening);
            await Task.Delay(BrandingHold, _lifetime.Token).ConfigureAwait(true);
            HandOff();
        }
        catch (OperationCanceledException)
        {
            // The splash was closed. The window is already going away.
        }
        catch (DesktopBootException error)
        {
            DesktopErrorLog.Write("boot", error);
            _model.ShowFailure(BootText.DescribeFailure(error.Stage), error.Message, canRetry: false);
        }
        catch (Exception error)
        {
            DesktopErrorLog.Write("boot", error);
            _model.ShowFailure(BootText.CouldNotStart, $"{error.GetType().Name}: {error.Message}", canRetry: false);
        }
    }

    /// <summary>
    /// Builds the shell while the splash is still up, makes it the main window, and only then
    /// closes the splash, so the application never has no window.
    /// </summary>
    private void HandOff()
    {
        var main = _openShell(_desktop);
        _handedOff = true;
        _desktop.MainWindow = main;
        main.Show();
        _splash.Close();
    }

    private void Quit()
    {
        _lifetime.Cancel();
        _desktop.Shutdown(1);
    }

    private static DesktopUpdatePreferences LoadPreferences()
    {
        try
        {
            return new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot).Load();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new DesktopBootException(BootStage.LoadingSettings, error.Message, error);
        }
    }
}
