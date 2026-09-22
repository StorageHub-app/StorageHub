using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Drives the background agent using the lifecycle primitives the desktop already owns: the
/// current-user shutdown command the agent exposes, and the guarded launch the desktop performs at
/// startup.
/// </summary>
/// <remarks>
/// <para>
/// Nothing new is asked of the agent -- this only gives the existing operations a caller. It is
/// the Windows half of the screen; <see cref="SystemdAgentLifecycleController"/> is the Linux one,
/// where systemd owns the process rather than the desktop.
/// </para>
/// <para>
/// Ported from the WinForms shell, where its outcome messages were English literals in an
/// application that ships in three languages. They are strings now.
/// </para>
/// </remarks>
internal sealed class PackagedAgentLifecycleController(PackagedDesktopLifecycle lifecycle)
    : IAgentLifecycleController
{
    private readonly PackagedDesktopLifecycle _lifecycle =
        lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

    public async Task<AgentLifecycleResult> ExecuteAsync(
        AgentLifecycleAction action,
        CancellationToken cancellationToken = default) => action switch
    {
        AgentLifecycleAction.Start => await StartAsync(cancellationToken).ConfigureAwait(false),
        AgentLifecycleAction.Stop => await StopAsync(cancellationToken).ConfigureAwait(false),
        AgentLifecycleAction.Restart => await RestartAsync(cancellationToken).ConfigureAwait(false),
        _ => new AgentLifecycleResult(false, Ui.Updates.AgentActionNotSupported)
    };

    private async Task<AgentLifecycleResult> StartAsync(CancellationToken cancellationToken)
    {
        var ensured = await _lifecycle.EnsureAgentAsync(cancellationToken).ConfigureAwait(false);
        return ensured.Status switch
        {
            AgentEnsureStatus.AlreadyRunning => new(true, Ui.Updates.AgentWasAlreadyRunning),
            AgentEnsureStatus.Started => new(true, Ui.Updates.AgentStarted),
            AgentEnsureStatus.MissingExecutable => new(false, Ui.Updates.AgentExecutableMissing),
            AgentEnsureStatus.StartupTimedOut => new(false, Ui.Updates.AgentDidNotBecomeAvailable),
            _ => new(false, Ui.Updates.AgentCouldNotBeLaunched)
        };
    }

    private async Task<AgentLifecycleResult> StopAsync(CancellationToken cancellationToken)
    {
        // A graceful shutdown lets in-flight work checkpoint; the durable queue recovers an
        // interrupted owner on the next start either way.
        var stopped = await _lifecycle
            .TryStopAgentAsync(AgentShutdownReason.Restart, cancellationToken)
            .ConfigureAwait(false);
        return stopped
            ? new(true, Ui.Updates.AgentStopped)
            : new(false, Ui.Updates.AgentDidNotConfirmShutdown);
    }

    private async Task<AgentLifecycleResult> RestartAsync(CancellationToken cancellationToken)
    {
        var stopped = await _lifecycle
            .TryStopAgentAsync(AgentShutdownReason.Restart, cancellationToken)
            .ConfigureAwait(false);

        // Start regardless: a stop that timed out may still have left no agent running, and an
        // agent that was already down is exactly what a restart should fix.
        var started = await StartAsync(cancellationToken).ConfigureAwait(false);
        if (!started.Succeeded)
        {
            return started;
        }

        return stopped
            ? new(true, Ui.Updates.AgentRestarted)
            : new(true, Ui.Updates.AgentRestartedWithoutShutdown);
    }
}

/// <summary>
/// Picks the way this machine controls its agent, or says that it cannot.
/// </summary>
/// <remarks>
/// <para>
/// The two implementations are not interchangeable and not a matter of taste: on Windows the
/// desktop launched the agent and can ask it to stop over its pipe, and on Linux systemd owns the
/// unit and would restart anything the desktop killed behind its back.
/// </para>
/// <para>
/// Null is a real answer. A development build run from the source tree has no packaged agent
/// beside it and no unit installed, and the screen says so plainly rather than offering buttons
/// that cannot work.
/// </para>
/// </remarks>
internal static class AgentLifecycleControllers
{
    internal static IAgentLifecycleController? ForThisMachine()
    {
        if (OperatingSystem.IsLinux())
        {
            return new SystemdAgentLifecycleController();
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return new PackagedAgentLifecycleController(WindowsDesktopLifecycle.Create());
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            // No executable path to work from, which is what a single-file host without one looks
            // like. The screen reports that it cannot control the agent rather than failing to open.
            return null;
        }
    }
}
