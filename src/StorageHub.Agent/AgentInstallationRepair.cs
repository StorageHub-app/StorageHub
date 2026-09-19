namespace StorageHub.Agent;

/// <summary>What happened when a repair was applied.</summary>
public sealed record InstallationRepairResult(bool Succeeded, string Message);

/// <summary>
/// Applies the repairs <see cref="AgentInstallationCheck"/> names.
///
/// Kept apart from the check so that looking is always safe. The check touches nothing and can be
/// run on a timer or a button without consequence; only this changes the machine, and only for a
/// repair the caller asked for by name.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class AgentInstallationRepair
{
    /// <summary>
    /// The failure actions a crashed agent needs: restart after five seconds, then ten, then
    /// thirty, with the count forgetting a bad day after 24 hours. Without these a crash is
    /// permanent, because Windows does nothing for a service that has no actions configured.
    /// </summary>
    private const string FailureActions = "reset= 86400 actions= restart/5000/restart/10000/restart/30000";

    /// <summary>Whether this repair needs an administrative token to apply.</summary>
    public static bool RequiresElevation(InstallationRepair repair) => repair switch
    {
        InstallationRepair.StartService => true,
        InstallationRepair.ConfigureServiceRecovery => true,
        InstallationRepair.RestageAgent => true,
        InstallationRepair.CreateAgentDirectory => false,
        _ => false,
    };

    /// <summary>
    /// Applies one repair. Returns rather than throws, because every one of these can fail for a
    /// reason the operator needs to read -- most often that the token is not elevated.
    /// </summary>
    /// <param name="agentSourcePath">
    /// The agent to copy when re-staging. Passed in rather than taken from the running process,
    /// because this runs in the agent when elevation was needed and in the desktop when it was
    /// not -- and staging the desktop over the service would be a memorable way to break a
    /// machine.
    /// </param>
    public static InstallationRepairResult Apply(
        InstallationRepair repair,
        AgentHostMode mode,
        string? agentSourcePath = null)
    {
        if (repair == InstallationRepair.None)
        {
            return new InstallationRepairResult(false, "Nothing to repair.");
        }

        if (RequiresElevation(repair) && !AgentHostLayout.IsElevated())
        {
            return new InstallationRepairResult(
                false,
                "This repair changes a Windows service, which needs administrator rights. Restart StorageHub "
                    + "as an administrator and run it again.");
        }

        return repair switch
        {
            InstallationRepair.CreateAgentDirectory => CreateAgentDirectory(mode),
            InstallationRepair.StartService => StartService(),
            InstallationRepair.ConfigureServiceRecovery => ConfigureRecovery(),
            InstallationRepair.RestageAgent => Restage(agentSourcePath),
            _ => new InstallationRepairResult(false, "Unknown repair."),
        };
    }

    private static InstallationRepairResult CreateAgentDirectory(AgentHostMode mode)
    {
        var directory = AgentHostLayout.ResolveAgentDirectory(mode);
        try
        {
            Directory.CreateDirectory(directory);
            return new InstallationRepairResult(true, $"Created {directory}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new InstallationRepairResult(false, $"Could not create {directory}: {error.Message}");
        }
    }

    private static InstallationRepairResult StartService()
    {
        try
        {
            AgentServiceInstaller.Start();
            return new InstallationRepairResult(true, "The agent service is running.");
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            return new InstallationRepairResult(false, $"The service did not start: {error.Message}");
        }
    }

    /// <summary>
    /// Copies the current agent over the one the service runs from.
    ///
    /// Needed because an application update cannot do it: the updater is unelevated and the
    /// staged copy lives somewhere only administrators may write, which is the whole point --
    /// a service binary its own user can replace runs that user.s code as SYSTEM. The service
    /// must be stopped first, because it holds the file it is executing.
    /// </summary>
    private static InstallationRepairResult Restage(string? agentSourcePath)
    {
        if (string.IsNullOrWhiteSpace(agentSourcePath) || !File.Exists(agentSourcePath))
        {
            return new InstallationRepairResult(
                false,
                "The agent to copy from could not be found, so nothing was changed.");
        }

        try
        {
            var wasRunning = AgentServiceInstaller.Describe().Running;
            if (wasRunning)
            {
                AgentServiceInstaller.Stop();
            }

            var staged = AgentServiceStaging.Stage(agentSourcePath);
            AgentServiceStaging.EnsureSafeForService(staged);
            AgentServiceInstaller.Start();
            return new InstallationRepairResult(true, "The service is running the current agent.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            return new InstallationRepairResult(
                false,
                $"The agent could not be re-staged: {error.Message}. The service may be stopped; "
                    + "check the installation again.");
        }
    }

    private static InstallationRepairResult ConfigureRecovery()
    {
        var output = WindowsInstallationProbe.RunServiceControl(
            $"failure {AgentHostLayout.ServiceName} {FailureActions}");
        return output is null
            ? new InstallationRepairResult(false, "Windows did not accept the failure actions.")
            : new InstallationRepairResult(true, "Windows will now restart the agent if it stops unexpectedly.");
    }
}
