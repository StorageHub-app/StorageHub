using System.Diagnostics;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>What running a command produced.</summary>
/// <param name="Ran">
/// Whether the command could be run at all. False on a machine with no systemd, which is a
/// different thing from a command that ran and refused.
/// </param>
/// <param name="ExitCode">The command's exit code, or -1 when it never ran.</param>
/// <param name="Error">
/// Whatever it wrote to standard error, trimmed. systemd explains its refusals there, and the
/// explanation is worth more to somebody than "the agent did not start".
/// </param>
internal sealed record ProcessRunResult(bool Ran, int ExitCode, string Error)
{
    internal bool Succeeded => Ran && ExitCode == 0;

    internal static ProcessRunResult NotRun { get; } = new(false, -1, string.Empty);
}

/// <summary>
/// Runs a command and waits for it.
/// </summary>
/// <remarks>
/// An interface so the systemd controller can be tested on any machine, including the Windows one
/// this is largely developed on and the CI runner, neither of which has <c>systemctl</c>.
/// </remarks>
internal interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

/// <summary>Runs a command as a child process.</summary>
internal sealed class ProcessRunner(TimeSpan? timeout = null) : IProcessRunner
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(20);

    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null) return ProcessRunResult.NotRun;

            // Read before waiting. A command that fills the error pipe blocks writing to it, and
            // waiting first would then deadlock against the read that never started.
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_timeout);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Took longer than the timeout. Killed rather than left behind, because the caller
                // is about to report a failure and a stray systemctl would outlive the window.
                TryKill(process);
                return new ProcessRunResult(true, -1, Ui.Updates.AgentCommandTimedOut);
            }

            return new ProcessRunResult(
                true,
                process.ExitCode,
                (await error.ConfigureAwait(false)).Trim());
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or
            InvalidOperationException or PlatformNotSupportedException)
        {
            // The command is not on this machine at all, which the caller reports as "this build
            // cannot control the agent" rather than crashing over.
            return ProcessRunResult.NotRun;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException or
            System.ComponentModel.Win32Exception)
        {
            // It exited between the timeout and the kill, which is the outcome we wanted anyway.
        }
    }
}

/// <summary>
/// Starts, stops and restarts the agent as a systemd user unit.
/// </summary>
/// <remarks>
/// <para>
/// The Linux counterpart of <see cref="PackagedAgentLifecycleController"/>, and the reason this
/// screen is not simply a port: on Windows the desktop launches the agent process itself and asks
/// it to shut down over a pipe, whereas on Linux systemd owns it. Asking systemd is not merely a
/// different way to do the same thing -- it is the only one that agrees with the unit's
/// <c>Restart=on-failure</c>, which would otherwise bring back an agent the desktop had just
/// stopped.
/// </para>
/// <para>
/// The unit is the one the .deb installs and the one <c>SystemdUserAutostart</c> writes; either
/// may have registered it, so the name is fixed here to match both. It is a user unit, so none of
/// this needs privilege.
/// </para>
/// </remarks>
internal sealed class SystemdAgentLifecycleController(IProcessRunner? runner = null)
    : IAgentLifecycleController
{
    /// <summary>
    /// The unit both the package and the agent's own autostart register.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than shared with <c>SystemdUserAutostart.UnitName</c>, which is internal
    /// to the agent assembly. If one of them ever changes, the other has to change with it, and
    /// <c>ThePackagedUnitNameIsTheOneThisControls</c> is the test that says so.
    /// </remarks>
    internal const string UnitName = "storagehub-agent.service";

    private readonly IProcessRunner _runner = runner ?? new ProcessRunner();

    public async Task<AgentLifecycleResult> ExecuteAsync(
        AgentLifecycleAction action,
        CancellationToken cancellationToken = default)
    {
        var verb = action switch
        {
            AgentLifecycleAction.Start => "start",
            AgentLifecycleAction.Stop => "stop",
            AgentLifecycleAction.Restart => "restart",
            _ => null
        };

        if (verb is null)
        {
            return new AgentLifecycleResult(false, Ui.Updates.AgentActionNotSupported);
        }

        var result = await _runner
            .RunAsync("systemctl", ["--user", verb, UnitName], cancellationToken)
            .ConfigureAwait(false);

        if (!result.Ran)
        {
            return new AgentLifecycleResult(false, Ui.Updates.ThisBuildCannotControlTheAgentProcess);
        }

        if (result.Succeeded)
        {
            return new AgentLifecycleResult(true, Describe(action));
        }

        // systemd says why in its own words -- "Unit storagehub-agent.service not found", most
        // often, when the package is installed but the unit has never been enabled. Carrying that
        // through is the difference between a fixable message and a shrug.
        return new AgentLifecycleResult(
            false,
            string.IsNullOrWhiteSpace(result.Error)
                ? Ui.Updates.AgentCouldNotBeControlled
                : result.Error);
    }

    private static string Describe(AgentLifecycleAction action) => action switch
    {
        AgentLifecycleAction.Start => Ui.Updates.AgentStarted,
        AgentLifecycleAction.Stop => Ui.Updates.AgentStopped,
        _ => Ui.Updates.AgentRestarted
    };
}
