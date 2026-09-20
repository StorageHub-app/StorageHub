using StorageHub.Ipc;

namespace StorageHub.Agent;

/// <summary>
/// The agent's claim on its data root for as long as it runs.
/// </summary>
/// <remarks>
/// Two agents on one data root is the failure this prevents, and it is a quiet one: the files
/// differ per mode, so nothing is corrupted, but the app and the scheduler come to disagree about
/// what is stored. Windows also hardens the tree's DACL while it holds the claim; Linux relies on
/// the directory already belonging to one user.
/// </remarks>
public interface IAgentDataDirectory : IDisposable
{
    /// <summary>The StorageHub root, which the desktop shares.</summary>
    string RootDirectory { get; }

    /// <summary>The agent's own subtree of it.</summary>
    string AgentDirectory { get; }

    /// <summary>Where the CodeLogic framework keeps its own state.</summary>
    string FrameworkDirectory { get; }
}

/// <summary>
/// The host-side decisions that differ by operating system.
/// </summary>
/// <remarks>
/// Separate from <see cref="IAgentPlatform"/>, which answers questions about a mode. These are
/// things the host process does on the way up: read its own arguments, refuse to start, attach to a
/// service manager, claim its data root. Together they are what lets the composition root contain a
/// single platform branch - the one that picks the platform - rather than ten.
/// </remarks>
public interface IAgentHostPlatform
{
    /// <summary>
    /// Which mode these arguments ask for.
    /// </summary>
    /// <remarks>
    /// Windows recognises --service. Linux has no service mode, so every invocation is a session
    /// agent and an unsupported switch is refused rather than quietly ignored.
    /// </remarks>
    AgentHostMode ResolveHostMode(IReadOnlyList<string> arguments);

    /// <summary>
    /// Runs an administrative command and returns its exit code, or null if these arguments are not
    /// one.
    /// </summary>
    /// <remarks>
    /// Service install, uninstall and repair run elevated and do no agent work, so they are handled
    /// before any startup. On Linux there are none: registering a user unit needs no privilege and
    /// is done in-process.
    /// </remarks>
    Task<int?> TryHandleAdministrativeCommandAsync(IReadOnlyList<string> arguments);

    /// <summary>
    /// Why this agent must not start, or null if it may.
    /// </summary>
    /// <remarks>
    /// On Windows a session agent refuses to run beside an installed service. An older desktop, or
    /// an autostart entry one left behind, would otherwise start a second agent on a second
    /// database - no corruption, because the files differ, but the app and the scheduler quietly
    /// disagree about what is stored.
    /// </remarks>
    string? DescribeStartupRefusal(AgentHostMode mode);

    /// <summary>
    /// Hands the process to a service manager if this mode has one.
    /// </summary>
    /// <remarks>
    /// Windows must answer the SCM before the slow startup below it, or the process is retired as
    /// unresponsive long before the agent is ready. systemd Type=simple wants no such handshake.
    /// </remarks>
    void AttachToServiceManager(AgentHostMode mode, TaskCompletionSource shutdown);

    /// <summary>Claims the data root, or throws <see cref="AgentDataDirectoryException"/>.</summary>
    IAgentDataDirectory AcquireDataDirectory(AgentHostMode mode, string dataRoot);

    /// <summary>
    /// Accounts admitted to a machine-service endpoint, in the transport's own form.
    /// </summary>
    /// <remarks>
    /// Only meaningful where an endpoint is reachable by more than one account, which on Linux it
    /// is not - so this is empty there, and the socket's directory is the whole boundary.
    /// </remarks>
    IReadOnlyList<string> ResolvePermittedPrincipals(AgentHostMode mode, string dataRoot);

}

/// <summary>A data root the agent will not accept.</summary>
public class AgentDataDirectoryException : Exception
{
    public AgentDataDirectoryException(string message)
        : base(message)
    {
    }

    public AgentDataDirectoryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AgentDataDirectoryException()
    {
    }
}
