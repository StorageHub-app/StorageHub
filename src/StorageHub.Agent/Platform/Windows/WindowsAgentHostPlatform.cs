using StorageHub.Agent;
using StorageHub.Infrastructure.Windows;

namespace StorageHub.Agent.Windows;

/// <summary>The Windows data-directory lease, behind the host's own contract.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class WindowsAgentDataDirectory(WindowsAgentDataDirectoryLease lease) : IAgentDataDirectory
{
    public string RootDirectory => lease.RootDirectory;

    public string AgentDirectory => lease.AgentDirectory;

    public string FrameworkDirectory => lease.FrameworkDirectory;

    public void Dispose() => lease.Dispose();
}

/// <summary>What the agent host does differently on Windows.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsAgentHostPlatform : IAgentHostPlatform
{
    public AgentHostMode ResolveHostMode(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Contains(AgentHostLayout.ServiceArgument, StringComparer.OrdinalIgnoreCase)
            ? AgentHostMode.WindowsService
            : AgentHostMode.UserSession;
    }

    /// <summary>
    /// A session agent will not run beside an installed service.
    /// </summary>
    /// <remarks>
    /// An older desktop, or an autostart entry one left behind, would otherwise start a second agent
    /// on a second database: no corruption, because the files differ, but the app and the scheduler
    /// would quietly disagree about what is stored. Refusing is the only signal a stale launcher
    /// will understand.
    /// </remarks>
    public string? DescribeStartupRefusal(AgentHostMode mode) =>
        mode == AgentHostMode.UserSession && AgentServiceInstaller.Describe().Installed
            ? "The StorageHub agent service is installed; the session agent will not start alongside it."
            : null;

    /// <summary>
    /// Answers the service control manager before the slow startup below, or it retires the process
    /// as unresponsive long before the agent is ready.
    /// </summary>
    public void AttachToServiceManager(AgentHostMode mode, TaskCompletionSource shutdown)
    {
        ArgumentNullException.ThrowIfNull(shutdown);
        if (mode == AgentHostMode.WindowsService)
        {
            AgentServiceHost.Attach(shutdown);
        }
    }

    public IAgentDataDirectory AcquireDataDirectory(AgentHostMode mode, string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        try
        {
            var applicationOwnedTreeRoot =
                WindowsAgentDataDirectoryLease.ResolveApplicationOwnedTreeRoot(AppContext.BaseDirectory);
            WindowsAgentDataDirectoryLease.EnsureDataRootIsSeparateFromApplication(
                dataRoot,
                applicationOwnedTreeRoot);
            WindowsAgentDataDirectoryLease.EnsureApplicationTreeIsSeparateFromInstanceLock(
                applicationOwnedTreeRoot);

            return new WindowsAgentDataDirectory(WindowsAgentDataDirectoryLease.Acquire(
                dataRoot,
                scope: mode == AgentHostMode.WindowsService
                    // The machine tree stays reachable by administrators. Its secrets are sealed
                    // with the machine key, which any administrator can already use, and the
                    // elevated switch back to a session mode runs as the signed-in user - it has to
                    // be able to read what it is bringing home.
                    ? AgentDataTreeScope.Machine
                    : AgentDataTreeScope.CurrentUser));
        }
        catch (WindowsAgentDataDirectoryException error)
        {
            throw new AgentDataDirectoryException(error.Message, error);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new AgentDataDirectoryException("The configured path is invalid.", error);
        }
    }

    public IReadOnlyList<string> ResolvePermittedPrincipals(AgentHostMode mode, string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        return mode == AgentHostMode.WindowsService
            ? AgentServiceClients.ReadPermittedSids(dataRoot)
            : [];
    }
}
