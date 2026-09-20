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
    /// <summary>
    /// Every invocation is a session agent.
    /// </summary>
    /// <remarks>
    /// Windows used to recognise --service here. There is one agent now, so the arguments no longer
    /// decide which kind it is.
    /// </remarks>
    public AgentHostMode ResolveHostMode(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return AgentHostMode.UserSession;
    }

    /// <summary>
    /// Nothing refuses a session agent any more.
    /// </summary>
    /// <remarks>
    /// This refused to start beside an installed service, which would otherwise have been a second
    /// agent on a second database. With one mode there is no second database to disagree with, and
    /// the single-instance lease already covers two agents in the same one.
    /// </remarks>
    public string? DescribeStartupRefusal(AgentHostMode mode) => null;

    /// <summary>There is no service control manager to answer.</summary>
    public void AttachToServiceManager(AgentHostMode mode, TaskCompletionSource shutdown)
    {
        ArgumentNullException.ThrowIfNull(shutdown);
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
                // The data root is machine-wide - %PROGRAMDATA% - but the agent that owns it is
                // this user's, so the lease is the user's.
                scope: AgentDataTreeScope.CurrentUser));
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

    /// <summary>
    /// None. A same-user pipe is already restricted to this account by the kernel.
    /// </summary>
    /// <remarks>
    /// This listed the SIDs allowed onto a machine-wide pipe, which only a service published.
    /// </remarks>
    public IReadOnlyList<string> ResolvePermittedPrincipals(AgentHostMode mode, string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        return [];
    }
}
