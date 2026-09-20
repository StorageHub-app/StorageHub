namespace StorageHub.Agent;

/// <summary>
/// Chooses the platform implementation for the operating system in use.
/// </summary>
/// <remarks>
/// The one place in StorageHub that branches on the operating system to pick an agent platform.
/// Everything downstream takes an <see cref="IAgentPlatform"/> and contains no further branch, and
/// CA1416 enforces the rest: a platform implementation is attributed for its platform, so reaching
/// one without a guard is a build error rather than a runtime surprise.
///
/// Both the agent host and the desktop ask here. They used to answer separately - the host by
/// constructing a platform, the desktop by reading the service control manager itself - which is how
/// the desktop came to look for a registry value under a different name than the agent wrote.
/// </remarks>
public static class AgentPlatforms
{
    /// <summary>
    /// The platforms for this operating system, or false if StorageHub does not run on it.
    /// </summary>
    /// <remarks>
    /// Written as if/else rather than a ternary because CA1416 narrows on a guard, not on the false
    /// branch of one: it has to see IsLinux() asserted before a Linux type is constructed.
    /// </remarks>
    public static bool TryCreate(out IAgentPlatform? agent, out IAgentHostPlatform? host)
    {
        if (OperatingSystem.IsWindows())
        {
            agent = new Windows.WindowsAgentPlatform();
            host = new Windows.WindowsAgentHostPlatform();
            return true;
        }

        if (OperatingSystem.IsLinux())
        {
            agent = new Linux.LinuxAgentPlatform();
            host = new Linux.LinuxAgentHostPlatform();
            return true;
        }

        agent = null;
        host = null;
        return false;
    }

    /// <summary>The agent platform for this operating system.</summary>
    /// <exception cref="PlatformNotSupportedException">StorageHub does not run on this system.</exception>
    public static IAgentPlatform ForCurrentOperatingSystem() =>
        TryCreate(out var agent, out _) && agent is not null
            ? agent
            : throw new PlatformNotSupportedException("StorageHub runs on Windows and Linux.");
}
