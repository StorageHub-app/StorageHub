using System.Diagnostics;
using StorageHub.Agent;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>What the agent is doing right now, regardless of what was configured.</summary>
internal sealed record AgentHostModeStatus(
    AgentHostMode Configured,
    bool ServiceInstalled,
    bool ServiceRunning)
{
    /// <summary>The mode actually in force, which is not always the one that was chosen.</summary>
    internal AgentHostMode Effective => ServiceInstalled
        ? AgentHostMode.WindowsService
        : AgentHostMode.UserSession;

    /// <summary>
    /// True when the configured mode is not what is running -- the case the startup check exists
    /// to catch, such as a service removed outside the app.
    /// </summary>
    internal bool Mismatched => Configured != Effective;
}

internal sealed record AgentHostModeChangeResult(bool Succeeded, string Summary);

/// <summary>
/// Applies a host-mode change from the desktop.
///
/// The desktop cannot install a service itself, so the work is done by re-launching the agent
/// elevated with a management verb and waiting for it. That is also why the migration lives on the
/// far side of the prompt: the elevated process keeps this user's identity, so it can still read
/// the user-scoped vault while having the rights to write the machine location.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class AgentHostModeController(string agentExecutablePath)
{
    private static readonly TimeSpan ElevationTimeout = TimeSpan.FromMinutes(5);

    private readonly string _agentExecutablePath = string.IsNullOrWhiteSpace(agentExecutablePath)
        ? throw new ArgumentException("An agent executable path is required.", nameof(agentExecutablePath))
        : agentExecutablePath;

    /// <summary>
    /// A message when the installed service is running an older build than this application, or
    /// null when it is current or there is no service.
    ///
    /// The service runs from a machine-owned copy that an application update does not touch, and
    /// refreshing it needs elevation -- so the difference is reported rather than fixed quietly.
    /// Letting the service re-stage itself would mean SYSTEM copying a binary out of a directory
    /// the user can write, which is exactly what staging exists to prevent.
    /// </summary>
    internal static string? DescribeStaleService()
    {
        if (!AgentServiceInstaller.Describe().Installed)
        {
            return null;
        }

        var staged = AgentServiceStaging.ReadStagedVersion("StorageHub.Agent.Windows.exe");
        var current = DesktopApplicationVersion.Current;
        if (staged is null || VersionsMatch(staged, current))
        {
            return null;
        }

        return Ui.Format(Ui.Settings.AgentModeStaleServiceFormat, Shorten(staged), Shorten(current));
    }

    /// <summary>
    /// Compares only the release portion. Both sides carry a <c>+commit</c> suffix that differs
    /// between builds of the same version, and reporting that as an update would send the operator
    /// through an elevation prompt that changes nothing.
    /// </summary>
    private static bool VersionsMatch(string staged, string current) =>
        string.Equals(Shorten(staged), Shorten(current), StringComparison.OrdinalIgnoreCase);

    private static string Shorten(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }

    internal static AgentHostModeStatus Describe(AgentHostMode configured)
    {
        var service = AgentServiceInstaller.Describe();
        return new AgentHostModeStatus(configured, service.Installed, service.Running);
    }

    /// <summary>
    /// Switches to <paramref name="desired"/>, prompting for elevation. Returns a result rather
    /// than throwing so the caller can report a refused consent prompt as the ordinary outcome it
    /// is, rather than as a crash.
    /// </summary>
    internal async Task<AgentHostModeChangeResult> ApplyAsync(
        AgentHostMode desired,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(desired))
        {
            throw new ArgumentOutOfRangeException(nameof(desired));
        }

        // Moving between the two session modes only adds or removes a per-user logon entry, so it
        // must not drag the operator through an elevation prompt for a registry value they own.
        if (desired != AgentHostMode.WindowsService &&
            !AgentServiceInstaller.Describe().Installed)
        {
            return ApplySessionMode(desired);
        }

        var arguments = desired == AgentHostMode.WindowsService
            ? new[]
            {
                "--install-service",
                $"--user-data-root={AgentHostLayout.ResolveDataRoot(AgentHostMode.UserSession)}"
            }
            : ["--uninstall-service"];

        try
        {
            var exitCode = await RunElevatedAsync(arguments, cancellationToken).ConfigureAwait(false);

            // The cached mode was read at startup and is now wrong either way, including on a
            // failure that got far enough to register the service.
            DesktopAgentHost.Invalidate();
            if (exitCode is 0 or 6 && desired == AgentHostMode.WindowsService)
            {
                RetireSessionAgent();
            }

            return exitCode switch
            {
                0 => new AgentHostModeChangeResult(true, Ui.Settings.AgentModeApplied),
                // The agent reports a partly migrated vault separately: the service is installed
                // and running, but some secrets could not be read and must be entered again.
                6 => new AgentHostModeChangeResult(true, Ui.Settings.AgentModeAppliedWithSecretLoss),
                5 => new AgentHostModeChangeResult(false, Ui.Settings.AgentModeNeedsAdministrator),
                _ => new AgentHostModeChangeResult(false, Ui.Settings.AgentModeChangeFailed)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Declining the consent prompt lands here, and is not an error worth a stack trace.
            return new AgentHostModeChangeResult(false, Ui.Settings.AgentModeChangeCancelled);
        }
    }

    /// <summary>
    /// Adds or removes the logon entry, which is the whole difference between an agent that starts
    /// with Windows and one that lives only as long as the app.
    /// </summary>
    private static AgentHostModeChangeResult ApplySessionMode(AgentHostMode desired)
    {
        try
        {
            var lifecycle = PackagedDesktopLifecycle.CreateDefault();
            DesktopAgentHost.Invalidate();
            _ = desired == AgentHostMode.UserSession
                ? lifecycle.ConfigureAutostart()
                : lifecycle.RemoveAutostart();

            // Re-read rather than assume: autostart can be suppressed by policy or by the
            // environment switch, and claiming a mode Windows will not honour would be worse than
            // reporting the failure.
            DesktopAgentHost.Invalidate();
            return DesktopAgentHost.Mode == desired
                ? new AgentHostModeChangeResult(true, Ui.Settings.AgentModeApplied)
                : new AgentHostModeChangeResult(false, Ui.Settings.AgentModeChangeFailed);
        }
        catch (Exception)
        {
            return new AgentHostModeChangeResult(false, Ui.Settings.AgentModeChangeFailed);
        }
    }

    /// <summary>
    /// Stops the session agent left over from before the switch.
    ///
    /// It is a sibling process, not a child, so it survives on its own and would otherwise keep
    /// running beside the service -- two agents holding the same database open, and a desktop that
    /// connects to whichever pipe it happens to resolve. Removing the autostart entry as well
    /// stops it coming back at the next sign-in.
    /// </summary>
    private static void RetireSessionAgent()
    {
        try
        {
            var lifecycle = PackagedDesktopLifecycle.CreateDefault();
            _ = lifecycle.RemoveAutostart();
            _ = lifecycle.TryStopAgentAsync(AgentShutdownReason.Restart).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // The service is installed and serving either way; a stubborn session agent is worth
            // reporting only if it actually interferes, and it exits with the session regardless.
        }
    }

    private async Task<int> RunElevatedAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _agentExecutablePath,
            // Required for the runas verb; it also means output cannot be captured, which is why
            // the agent communicates through its exit code rather than its console.
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Windows did not start the elevated helper.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ElevationTimeout);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        return process.ExitCode;
    }
}
