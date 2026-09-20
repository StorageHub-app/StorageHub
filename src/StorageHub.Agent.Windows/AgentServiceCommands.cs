using System.Security.Principal;

namespace StorageHub.Agent.Windows;

/// <summary>
/// The elevated half of switching host modes: bring the installation across, then register or
/// remove the service.
///
/// This runs as its own short-lived process because managing a service needs an elevated token the
/// desktop does not have. It stays a single pass on purpose -- migration and registration in the
/// same elevated run -- so the operator sees one consent prompt rather than one per step, and so
/// the service is never registered against a machine location the installation has not reached.
///
/// Both directions migrate. Only installing used to, which left anything done under the service
/// stranded in ProgramData the moment somebody switched back.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class AgentServiceCommands
{
    internal const string InstallArgument = "--install-service";
    internal const string UninstallArgument = "--uninstall-service";

    public static int Execute(string[] args)
    {
        try
        {
            if (!AgentHostLayout.IsElevated())
            {
                Console.Error.WriteLine("Managing the StorageHub service requires an elevated process.");
                return 5;
            }

            return args.Contains(UninstallArgument, StringComparer.OrdinalIgnoreCase)
                ? Remove()
                : Install();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"StorageHub service management failed: {error.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Registers the service against the one data root this platform has.
    /// </summary>
    /// <remarks>
    /// There is nothing to move. Installing used to copy the database and re-protect every vault
    /// entry from the user location into the machine one, because the two modes read different
    /// roots; uninstalling copied it back. Both roots are %PROGRAMDATA%\StorageHub now, so the
    /// copy has no source and no destination - it would be a directory onto itself.
    /// </remarks>
    private static int Install()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ??
            throw new InvalidOperationException("The current Windows account SID is unavailable.");

        AgentServiceInstaller.Install(
            Environment.ProcessPath!,
            AgentHostLayout.ResolveDataRoot(AgentHostMode.WindowsService),
            owner);
        Console.WriteLine("The StorageHub agent service is installed and running.");
        return 0;
    }

    /// <summary>
    /// Stops the service and removes the registration, leaving the data where it is.
    /// </summary>
    /// <remarks>
    /// The data root does not belong to the service - a session agent reads the same one - so
    /// removing the service is a change of how the agent starts, not of where anything lives.
    /// </remarks>
    private static int Remove()
    {
        if (!AgentServiceInstaller.Describe().Installed)
        {
            Console.WriteLine("There was no StorageHub agent service to remove.");
            return 0;
        }

        AgentServiceInstaller.Stop();
        AgentServiceInstaller.Uninstall();
        Console.WriteLine("The StorageHub agent service was removed.");
        return 0;
    }
}
