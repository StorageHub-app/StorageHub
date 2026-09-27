namespace StorageHub.Desktop;

/// <summary>
/// Makes sure an agent is answering before the shell opens, on whichever platform this is.
/// </summary>
/// <remarks>
/// <para>
/// 1.x did this on its splash, and 2.0 had stopped doing it at all: a desktop opened without the
/// service running sat at "Agent: not connected" with nothing to start it but Tools > Background
/// agent. On Windows this is <see cref="PackagedDesktopLifecycle.EnsureAgentAsync"/>, which already
/// knows the service case, the session case and the timeouts. On Linux it is the user's systemd
/// unit, started if it is not already answering.
/// </para>
/// <para>
/// A build run from source has no packaged agent beside it. There, an agent started by hand is
/// used as it is -- the packaged path would stop it to launch a copy that does not exist -- and
/// its absence is reported as a missing executable, which is what it is.
/// </para>
/// </remarks>
internal static class DesktopAgentStartup
{
    /// <summary>How long a started unit, or an agent from source, is given to begin answering.</summary>
    internal static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(12);

    internal static async Task<AgentEnsureResult> EnsureAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            using var lifecycle = WindowsDesktopLifecycle.Create();
            if (!RunsAnAgentStartedByHand(lifecycle))
            {
                return await lifecycle.EnsureAgentAsync(cancellationToken).ConfigureAwait(false);
            }

            return new AgentEnsureResult(
                await AnswersAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false)
                    ? AgentEnsureStatus.AlreadyRunning
                    : AgentEnsureStatus.MissingExecutable);
        }

        if (await AnswersAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return new AgentEnsureResult(AgentEnsureStatus.AlreadyRunning);
        }

        if (OperatingSystem.IsLinux())
        {
            var started = await new SystemdAgentLifecycleController()
                .ExecuteAsync(AgentLifecycleAction.Start, cancellationToken)
                .ConfigureAwait(false);
            if (!started.Succeeded)
            {
                return new AgentEnsureResult(AgentEnsureStatus.LaunchFailed);
            }

            return new AgentEnsureResult(
                await AnswersAsync(ReadyTimeout, cancellationToken).ConfigureAwait(false)
                    ? AgentEnsureStatus.Started
                    : AgentEnsureStatus.StartupTimedOut);
        }

        return new AgentEnsureResult(AgentEnsureStatus.LaunchFailed);
    }

    /// <summary>
    /// Whether this is a build run from source, whose agent was started by hand: the desktop would
    /// own its agent, but has no packaged copy beside it to start.
    /// </summary>
    /// <remarks>
    /// That agent is used as it is and left as it is. Nothing here could start it again, so it is
    /// not restarted, for saved settings or for a terminal, and not stopped as the window closes,
    /// even in "only while StorageHub is open": stopping it there took the agent a developer had
    /// started down with every close of a dev desktop.
    /// </remarks>
    internal static bool RunsAnAgentStartedByHand(PackagedDesktopLifecycle lifecycle) =>
        lifecycle.DesktopOwnsAgent && !File.Exists(lifecycle.AgentExecutablePath);

    /// <summary>Whether the agent answers a status request within the time given.</summary>
    private static async Task<bool> AnswersAsync(TimeSpan within, CancellationToken cancellationToken)
    {
        await using var monitor = new AgentStatusMonitor();
        var deadline = DateTimeOffset.UtcNow + within;
        while (true)
        {
            if (await monitor.ProbeAsync(cancellationToken).ConfigureAwait(false)) return true;
            if (DateTimeOffset.UtcNow >= deadline) return false;
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken).ConfigureAwait(false);
        }
    }
}
