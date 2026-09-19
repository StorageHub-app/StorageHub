using System.Diagnostics;
using StorageHub.Agent;

namespace StorageHub.Desktop;

/// <summary>
/// Applies an installation repair, asking Windows for consent when the repair needs it.
///
/// The desktop runs unelevated and always will: it is a file browser that happens to own a
/// service. So a repair that touches the service control manager is handed to the agent, launched
/// once with the <c>runas</c> verb, exactly as switching host modes already does. That turns
/// "restart StorageHub as an administrator and try again" -- which is a instruction to go away and
/// come back -- into the consent prompt the operator was going to see anyway.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class ElevatedInstallationRepair
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    internal static InstallationRepairResult Apply(InstallationRepair repair, AgentHostMode mode)
    {
        // Nothing to elevate for, or the token is already elevated: do it here and skip the prompt.
        if (!AgentInstallationRepair.RequiresElevation(repair) || AgentHostLayout.IsElevated())
        {
            // Already elevated: the bundled agent is the copy the service should be running.
            return AgentInstallationRepair.Apply(repair, mode, ResolveBundledAgent());
        }

        string agent;
        try
        {
            agent = PackagedDesktopLifecycle.CreateDefault().AgentExecutablePath;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException)
        {
            return new InstallationRepairResult(false, $"The agent could not be located: {error.Message}");
        }

        if (!File.Exists(agent))
        {
            return new InstallationRepairResult(false, $"The agent is not where it should be: {agent}");
        }

        return Run(agent, repair);
    }


    private static string? ResolveBundledAgent()
    {
        try
        {
            var agent = PackagedDesktopLifecycle.CreateDefault().AgentExecutablePath;
            return File.Exists(agent) ? agent : null;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static InstallationRepairResult Run(string agent, InstallationRepair repair)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = agent,
            // Required for the runas verb, and the reason the helper reports through its exit
            // code: with UseShellExecute its console cannot be read.
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add(AgentHostLayout.RepairArgument);
        startInfo.ArgumentList.Add(repair.ToString());

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new InstallationRepairResult(false, "Windows did not start the elevated helper.");
            }

            if (!process.WaitForExit(Timeout))
            {
                return new InstallationRepairResult(false, "The repair did not finish in time.");
            }

            return process.ExitCode == 0
                ? new InstallationRepairResult(true, Describe(repair))
                : new InstallationRepairResult(false, "The repair ran but reported a failure. Check installation again to see what is still wrong.");
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the consent prompt was declined. Not a failure worth alarming
            // anybody about -- they chose it, and nothing changed.
            return new InstallationRepairResult(false, "Cancelled. Nothing was changed.");
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new InstallationRepairResult(false, $"The elevated helper could not run: {error.Message}");
        }
    }

    private static string Describe(InstallationRepair repair) => repair switch
    {
        InstallationRepair.StartService => "The agent service is running.",
        InstallationRepair.ConfigureServiceRecovery =>
            "Windows will now restart the agent if it stops unexpectedly.",
        InstallationRepair.CreateAgentDirectory => "The data directory was created.",
        _ => "Done.",
    };
}
