using StorageHub.Desktop.Localization;
namespace StorageHub.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Velopack stays first: its install and update hooks can end the process outright, and they
        // must not do that with a window on screen.
        VelopackDesktopBootstrap.Build(args).Run();

        // Before CodeLogic is initialized, because its parser reads the process command line
        // unconditionally and would answer these into a console a WinExe does not own.
        if (DesktopCommandLine.TryHandleFrameworkArguments(args, out var frameworkExitCode))
        {
            return frameworkExitCode;
        }

        using var lifecycle = WindowsDesktopLifecycle.Create();
        if (DesktopCommandLine.IsAgentOnly(args))
        {
            return RunAgentOnly(lifecycle);
        }

        // Both of these have to precede the first Form. Initialize sets DPI awareness and the
        // default font, and the framework refuses a colour-mode switch once a message loop is
        // running. The splash follows the operating system here; the configured appearance is
        // applied later, once settings have been read and before the main window is built.
        ApplicationConfiguration.Initialize();
        DesktopAppearanceService.SetAppearance(DesktopAppearance.System);

        var explorerDropBrokerAvailable = ExplorerDropBrokerInstaller.EnsureRegistered(AppContext.BaseDirectory);
        using var boot = new DesktopBootContext(lifecycle, explorerDropBrokerAvailable);
        System.Windows.Forms.Application.Run(boot);

        // After the loop, so the replacement shell never overlaps this one. See DesktopRestart.
        _ = DesktopRestart.TryStart();
        return boot.ExitCode;
    }

    /// <summary>The autostart path: start the agent and exit without ever showing a window.</summary>
    private static int RunAgentOnly(PackagedDesktopLifecycle lifecycle)
    {
        if (lifecycle.IsAutostartDisabled)
        {
            _ = lifecycle.RemoveAutostart();
            return 0;
        }

        var agent = lifecycle.EnsureAgentAsync().AsTask().GetAwaiter().GetResult();
        return agent.IsReady ? 0 : 1;
    }
}
