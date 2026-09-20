using StorageHub.Agent;
using StorageHub.Infrastructure.Unix;

namespace StorageHub.Agent.Linux;

/// <summary>
/// One agent per data root, held for as long as the process runs.
/// </summary>
/// <remarks>
/// A lock file opened without sharing, which maps to flock on Linux and is released when the
/// process ends however it ends. There is no DACL to harden the way the Windows lease does: the
/// data root is already inside one user's home, and a second user cannot reach it to begin with.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class LinuxAgentDataDirectory : IAgentDataDirectory
{
    private readonly FileStream _instanceLock;

    private LinuxAgentDataDirectory(string root, string agent, string framework, FileStream instanceLock)
    {
        RootDirectory = root;
        AgentDirectory = agent;
        FrameworkDirectory = framework;
        _instanceLock = instanceLock;
    }

    public string RootDirectory { get; }

    public string AgentDirectory { get; }

    public string FrameworkDirectory { get; }

    internal static LinuxAgentDataDirectory Acquire(string dataRoot)
    {
        var agent = Path.Combine(dataRoot, AgentHostLayout.AgentDirectoryName);
        var framework = Path.Combine(dataRoot, "CodeLogic");
        UnixFileSystem.EnsurePrivateDirectory(dataRoot);
        UnixFileSystem.EnsurePrivateDirectory(agent);
        UnixFileSystem.EnsurePrivateDirectory(framework);

        var lockPath = Path.Combine(agent, ".instance.lock");
        try
        {
            var handle = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
            return new LinuxAgentDataDirectory(dataRoot, agent, framework, handle);
        }
        catch (IOException error)
        {
            throw new AgentDataDirectoryException(
                $"Another StorageHub agent already owns '{dataRoot}'.",
                error);
        }
    }

    public void Dispose() => _instanceLock.Dispose();
}

/// <summary>What the agent host does differently on Linux.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class LinuxAgentHostPlatform : IAgentHostPlatform
{
    /// <summary>
    /// Always a session agent.
    /// </summary>
    /// <remarks>
    /// There is one kind of agent, so the arguments no longer decide which it is. This used to
    /// refuse --service, because Windows had a service mode and silently starting a session agent
    /// for somebody who asked for one would appear to work and then stop at sign-out. Neither
    /// platform has that mode now.
    /// </remarks>
    public AgentHostMode ResolveHostMode(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return AgentHostMode.UserSession;
    }

    /// <summary>Nothing competes for the data root but another agent, which the lock catches.</summary>
    public string? DescribeStartupRefusal(AgentHostMode mode) => null;

    /// <summary>systemd Type=simple treats the process as started as soon as it is forked.</summary>
    public void AttachToServiceManager(AgentHostMode mode, TaskCompletionSource shutdown)
    {
    }

    public IAgentDataDirectory AcquireDataDirectory(AgentHostMode mode, string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        return LinuxAgentDataDirectory.Acquire(dataRoot);
    }

    /// <summary>Empty: a Unix socket admits one user, and its directory is the whole boundary.</summary>
}
