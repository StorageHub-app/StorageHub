using Avalonia;

namespace StorageHub.Desktop;

public static class Program
{
    [System.STAThread]
    public static int Main(string[] args)
    {
        // Before anything initializes the framework: CodeLogic's parser reads the process command
        // line unconditionally and would answer these into a console a windowed app does not own.
        if (DesktopCommandLine.TryHandleFrameworkArguments(args, out var frameworkExitCode))
        {
            return frameworkExitCode;
        }

        // The sign-in autostart. It starts the agent and exits without a window; ignoring it, as
        // 2.0 did, opened the whole shell at every sign-in.
        if (DesktopCommandLine.IsAgentOnly(args))
        {
            return RunAgentOnly();
        }

        var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        // After the lifetime, so the replacement shell never overlaps this one. See DesktopRestart.
        // The close left the agent running for that shell, so when none could be started, "Only
        // while StorageHub is open" still takes the agent with this one, as any other close does,
        // unless it is one started by hand for a build run from source, which no close stops.
        var restarting = DesktopRestart.Requested;
        if (!DesktopRestart.TryStart() && restarting && OperatingSystem.IsWindows() && DesktopAgentHost.DesktopStopsAgent)
        {
            using var lifecycle = WindowsDesktopLifecycle.Create();
            if (!DesktopAgentStartup.RunsAnAgentStartedByHand(lifecycle))
            {
                _ = lifecycle.TryStopAgentAsync(AgentShutdownReason.Restart).AsTask().GetAwaiter().GetResult();
            }
        }

        return exitCode;
    }

    /// <summary>
    /// Public and static so the headless test host builds the same application the executable does.
    /// </summary>
    /// <remarks>
    /// UsePlatformDetect chooses X11 on Linux, which is Avalonia's supported default; the native
    /// Wayland backend graduated from preview in 12.1 but is opt-in and still marked experimental,
    /// so it is a follow-up rather than the shipping choice.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// The autostart path: start the agent and exit, as 1.x did.
    /// </summary>
    /// <remarks>
    /// Only Windows registers one. On Linux the agent's autostart is the user's systemd unit, which
    /// never runs the desktop at all, so the argument has nothing to do there.
    /// </remarks>
    private static int RunAgentOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        using var lifecycle = WindowsDesktopLifecycle.Create();
        if (lifecycle.IsAutostartDisabled)
        {
            _ = lifecycle.RemoveAutostart();
            return 0;
        }

        return lifecycle.EnsureAgentAsync().AsTask().GetAwaiter().GetResult().IsReady ? 0 : 1;
    }
}
