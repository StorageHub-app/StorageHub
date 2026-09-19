using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

namespace StorageHub.Agent;

/// <summary>Whether the agent service exists, and if so what state it is in.</summary>
public sealed record AgentServiceState(bool Installed, bool Running);

/// <summary>
/// Registers, starts and removes the agent's Windows service.
///
/// Every method here needs an elevated token, which the desktop does not have, so the desktop
/// re-launches the agent elevated to run these. Elevation keeps the same user identity, which is
/// what lets the same elevated pass both write to ProgramData and still open the user's own
/// secrets during migration.
///
/// The service is created with <c>sc.exe</c> rather than P/Invoking the service control manager:
/// the arguments are fixed strings built here, there is nothing user-supplied to escape, and a
/// failure produces a message an operator can act on instead of a bare Win32 error code.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class AgentServiceInstaller
{
    private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Reports whether the service exists and whether it is running.</summary>
    public static AgentServiceState Describe()
    {
        try
        {
            using var controller = new ServiceController(AgentHostLayout.ServiceName);
            return new AgentServiceState(true, controller.Status == ServiceControllerStatus.Running);
        }
        catch (InvalidOperationException)
        {
            // The service control manager reports a missing service this way.
            return new AgentServiceState(false, false);
        }
    }

    /// <summary>
    /// Creates the service, records who may connect to it, and starts it. Safe to repeat: an
    /// existing service is reconfigured rather than duplicated.
    /// </summary>
    public static void Install(string agentExecutablePath, string machineDataRoot, SecurityIdentifier permittedUser)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineDataRoot);
        ArgumentNullException.ThrowIfNull(permittedUser);
        if (!File.Exists(agentExecutablePath))
        {
            throw new FileNotFoundException("The agent executable was not found.", agentExecutablePath);
        }

        // Written before the service starts: it refuses to publish its pipe with nobody permitted,
        // so ordering this after the start would guarantee a failed first launch.
        AgentServiceClients.WritePermittedSids(machineDataRoot, [permittedUser.Value]);

        // Never register the install directory itself. StorageHub installs per user and a portable
        // build runs from anywhere, so the agent is copied somewhere only administrators can write
        // before anything points a LocalSystem service at it.
        var stagedExecutable = AgentServiceStaging.Stage(agentExecutablePath);
        AgentServiceStaging.EnsureSafeForService(stagedExecutable);
        var binPath = $"\"{stagedExecutable}\" {AgentHostLayout.ServiceArgument}";
        var alreadyInstalled = Describe().Installed;
        if (alreadyInstalled)
        {
            RunServiceControl("config", AgentHostLayout.ServiceName, "binPath=", binPath, "start=", "auto");
        }
        else
        {
            RunServiceControl(
                "create",
                AgentHostLayout.ServiceName,
                "binPath=",
                binPath,
                "start=",
                "auto",
                "DisplayName=",
                AgentHostLayout.ServiceDisplayName);
        }

        RunServiceControl(
            "description",
            AgentHostLayout.ServiceName,
            "Runs StorageHub transfers and scheduled synchronization without a signed-in user.");

        // Windows does nothing for a service with no failure actions, so a single crash left the
        // machine with no agent until somebody noticed and started it by hand -- and the only
        // symptom is the desktop reporting that the agent did not become ready. Five seconds,
        // then ten, then thirty, forgetting a bad day after 24 hours.
        RunServiceControl(
            "failure",
            AgentHostLayout.ServiceName,
            "reset=",
            "86400",
            "actions=",
            "restart/5000/restart/10000/restart/30000");

        try
        {
            Start();
        }
        catch (Exception) when (!alreadyInstalled)
        {
            // A registered service that will not start is worse than no service: the session agent
            // refuses to run beside an installed one, so the machine would be left with no agent at
            // all, and the only way out is a setting the operator has no reason to suspect. Putting
            // the registration back means a refused opt-in simply leaves them where they started.
            //
            // Only for a service this call created. Reconfiguring one that already existed is a
            // different situation -- the operator already has a service, and deleting it because a
            // restart failed would discard something this call did not create.
            RollBackFailedRegistration();
            throw;
        }
    }

    /// <summary>
    /// Removes a service that was just created but could not be started. Best effort by design: the
    /// caller is already reporting a failed install, and a registration that also refuses to be
    /// deleted needs an operator either way.
    /// </summary>
    private static void RollBackFailedRegistration()
    {
        try
        {
            Uninstall();
        }
        catch (Exception)
        {
            // Nothing further to try without turning a failed install into a second failure.
        }
    }

    /// <summary>Stops and deletes the service. Data is left where it is.</summary>
    public static void Uninstall()
    {
        if (!Describe().Installed)
        {
            return;
        }

        Stop();
        RunServiceControl("delete", AgentHostLayout.ServiceName);
    }

    public static void Start()
    {
        using var controller = new ServiceController(AgentHostLayout.ServiceName);
        if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
        {
            return;
        }

        controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, ControlTimeout);
    }

    public static void Stop()
    {
        using var controller = new ServiceController(AgentHostLayout.ServiceName);
        if (controller.Status == ServiceControllerStatus.Stopped || !controller.CanStop)
        {
            return;
        }

        controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, ControlTimeout);
    }

    private static void RunServiceControl(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Windows could not start the service control utility.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(ControlTimeout))
        {
            throw new System.TimeoutException("The service control utility did not finish in time.");
        }

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(error) ? output : error;
            throw new InvalidOperationException(
                $"Service control '{arguments[0]}' failed with exit code {process.ExitCode}. {detail.Trim()}");
        }
    }
}
