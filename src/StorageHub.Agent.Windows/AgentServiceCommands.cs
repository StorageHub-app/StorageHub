using System.Security.Principal;

namespace StorageHub.Agent.Windows;

/// <summary>
/// The elevated half of switching host modes: migrate, then register or remove the service.
///
/// This runs as its own short-lived process because installing a service needs an elevated token
/// the desktop does not have. It stays a single pass on purpose -- migration and installation in
/// the same elevated run -- so the operator sees one consent prompt rather than one per step, and
/// so the service is never registered against a machine location the secrets have not reached.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class AgentServiceCommands
{
    internal const string InstallArgument = "--install-service";
    internal const string UninstallArgument = "--uninstall-service";

    internal static async Task<int> ExecuteAsync(string[] args)
    {
        try
        {
            if (!AgentHostLayout.IsElevated())
            {
                Console.Error.WriteLine("Managing the StorageHub service requires an elevated process.");
                return 5;
            }

            if (args.Contains(UninstallArgument, StringComparer.OrdinalIgnoreCase))
            {
                AgentServiceInstaller.Uninstall();
                Console.WriteLine("The StorageHub agent service was removed.");
                return 0;
            }

            var machineRoot = AgentHostLayout.ResolveDataRoot(AgentHostMode.WindowsService);
            var userRoot = ResolveInvokingUserRoot(args)
                ?? AgentHostLayout.ResolveDataRoot(AgentHostMode.UserSession);
            var report = await AgentModeMigration
                .CopyUserInstallationToMachineAsync(userRoot, machineRoot)
                .ConfigureAwait(false);
            Console.WriteLine(report.Summary);

            using var identity = WindowsIdentity.GetCurrent();
            var owner = identity.User ??
                throw new InvalidOperationException("The current Windows account SID is unavailable.");
            AgentServiceInstaller.Install(Environment.ProcessPath!, machineRoot, owner);
            Console.WriteLine("The StorageHub agent service is installed and running.");

            // A partial migration still leaves a working service, so it is reported rather than
            // failed: the connections whose secrets did not survive can be re-entered, and saying
            // so beats rolling back a service the operator asked for.
            return report.Succeeded ? 0 : 6;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"StorageHub service management failed: {error.Message}");
            return 1;
        }
    }

    /// <summary>
    /// The user data root to migrate from. Passed explicitly because an elevated launch can arrive
    /// with a different profile than the desktop that requested it, and guessing would silently
    /// migrate the wrong account's installation -- or nothing at all.
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
