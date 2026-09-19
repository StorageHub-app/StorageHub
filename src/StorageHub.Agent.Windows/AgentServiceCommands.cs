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
internal static class AgentServiceCommands
{
    internal const string InstallArgument = "--install-service";
    internal const string UninstallArgument = "--uninstall-service";

    /// <summary>
    /// Applies one repair named by <see cref="InstallationRepair"/>, followed by the name.
    ///
    /// Elevated for the same reason the two above are: starting a service and setting its
    /// failure actions are administrative acts. Running it as its own short-lived process is
    /// what lets the desktop offer the repair as a button rather than as instructions to go and
    /// restart the application as an administrator.
    /// </summary>
    internal const string RepairArgument = AgentHostLayout.RepairArgument;

    /// <summary>Applied, but some secrets could not be read and must be entered again.</summary>
    private const int PartialMigrationExitCode = 6;

    internal static async Task<int> ExecuteAsync(string[] args)
    {
        try
        {
            if (!AgentHostLayout.IsElevated())
            {
                Console.Error.WriteLine("Managing the StorageHub service requires an elevated process.");
                return 5;
            }

            var machine = AgentDataLocation.For(AgentHostMode.WindowsService);
            var user = AgentDataLocation.For(
                AgentHostMode.UserSession,
                ResolveInvokingUserRoot(args) ?? AgentHostLayout.ResolveDataRoot(AgentHostMode.UserSession));

            if (args.Contains(RepairArgument, StringComparer.OrdinalIgnoreCase))
            {
                return Repair(args);
            }

            return args.Contains(UninstallArgument, StringComparer.OrdinalIgnoreCase)
                ? await RemoveAsync(machine, user).ConfigureAwait(false)
                : await InstallAsync(user, machine).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"StorageHub service management failed: {error.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Applies the named repair and reports through the exit code, because the runas verb needs
    /// UseShellExecute and that means the caller cannot read this process.s console.
    /// </summary>
    private static int Repair(string[] args)
    {
        var index = Array.FindIndex(args, argument =>
            string.Equals(argument, RepairArgument, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length ||
            !Enum.TryParse<InstallationRepair>(args[index + 1], ignoreCase: true, out var repair))
        {
            Console.Error.WriteLine("A repair name is required.");
            return 2;
        }

        // Environment.ProcessPath is this agent, launched from the install directory, which is
        // exactly the copy the service should be running.
        var result = AgentInstallationRepair.Apply(
            repair, AgentHostMode.WindowsService, Environment.ProcessPath);
        Console.WriteLine(result.Message);
        return result.Succeeded ? 0 : 1;
    }

    private static async Task<int> InstallAsync(AgentDataLocation user, AgentDataLocation machine)
    {
        var report = await AgentModeMigration.MigrateAsync(user, machine).ConfigureAwait(false);
        Console.WriteLine(report.Summary);

        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ??
            throw new InvalidOperationException("The current Windows account SID is unavailable.");
        AgentServiceInstaller.Install(Environment.ProcessPath!, machine.DataRoot, owner);
        Console.WriteLine("The StorageHub agent service is installed and running.");

        // A partial migration still leaves a working service, so it is reported rather than failed:
        // the connections whose secrets did not survive can be re-entered, and saying so beats
        // rolling back a service the operator asked for.
        return report.Succeeded ? 0 : PartialMigrationExitCode;
    }

    /// <summary>
    /// Stops the service, brings the installation back to the user location, and only then removes
    /// the registration.
    ///
    /// Stopping first is what makes the copy trustworthy -- a database read out from under a
    /// running agent is a database missing whatever it wrote in the meantime -- and the service is
    /// being deleted anyway, so there is nothing to preserve by leaving it up. Deleting last means
    /// a failed migration leaves the machine in the mode it was already in, rather than in neither.
    /// </summary>
    private static async Task<int> RemoveAsync(AgentDataLocation machine, AgentDataLocation user)
    {
        if (!AgentServiceInstaller.Describe().Installed)
        {
            Console.WriteLine("There was no StorageHub agent service to remove.");
            return 0;
        }

        AgentServiceInstaller.Stop();
        var report = await AgentModeMigration.MigrateAsync(machine, user).ConfigureAwait(false);
        Console.WriteLine(report.Summary);

        AgentServiceInstaller.Uninstall();
        Console.WriteLine("The StorageHub agent service was removed.");
        return report.Succeeded ? 0 : PartialMigrationExitCode;
    }

    /// <summary>
    /// The user data root to migrate to and from. Passed explicitly because an elevated launch can
    /// arrive with a different profile than the desktop that requested it, and guessing would
    /// silently migrate the wrong account's installation -- or nothing at all.
    /// </summary>
    private static string? ResolveInvokingUserRoot(string[] args)
    {
        const string prefix = "--user-data-root=";
        foreach (var argument in args)
        {
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = argument[prefix.Length..].Trim().Trim('"');
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }
}
