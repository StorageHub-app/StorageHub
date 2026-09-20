using StorageHub.Agent;
using StorageHub.Ipc;
using StorageHub.Infrastructure.Unix;
using StorageHub.Ipc.Unix;
using StorageHub.Security;

namespace StorageHub.Agent.Linux;

/// <summary>Running the StorageHub agent on Linux.</summary>
public sealed class LinuxAgentPlatform : IAgentPlatform
{
    private static readonly HashSet<AgentHostMode> Supported =
        [AgentHostMode.UserSession, AgentHostMode.AppSession];

    public string Name => "linux";

    /// <summary>
    /// The two per-user modes, and deliberately not WindowsService.
    /// </summary>
    /// <remarks>
    /// The obvious move - adding a Linux member to AgentHostMode for a systemd unit - would be
    /// wrong. UserSession and AppSession already express the only distinction that matters: whether
    /// an autostart registration outlives the app. Same uid, same data root, same vault key, same
    /// socket either way. The mode is persisted in the desktop's settings, so leaving the enum alone
    /// also avoids a config migration for a difference that does not exist.
    /// </remarks>
    public IReadOnlySet<AgentHostMode> SupportedHostModes => Supported;

    public IIpcTransport Transport => UnixDomainSocketIpc.Transport;

    public IIpcPeerAuthorizer PeerAuthorizer => UnixDomainSocketIpc.PeerAuthorizer;

    public IIpcServerAuthenticator ServerAuthenticator => UnixDomainSocketIpc.ServerAuthenticator;

    public IAutostartRegistration Autostart { get; } = new SystemdUserAutostart();

    /// <summary>
    /// Nothing here asks for privilege it was not started with.
    /// </summary>
    /// <remarks>
    /// The Windows design elevates to register a service, and carries the awkward consequence that
    /// an elevated step still has to reach the calling user's own vault. A per-user systemd unit
    /// needs no root at all, so the question does not arise.
    /// </remarks>
    public bool CanElevate => false;

    public AgentPaths ResolvePaths(AgentHostMode mode)
    {
        EnsureSupported(mode);
        return new AgentPaths(LinuxAgentPaths.ResolveDataRoot(), LinuxAgentPaths.ResolveRuntimeRoot());
    }

    public IpcEndpoint ResolveEndpoint(AgentHostMode mode, AgentIpcChannel channel)
    {
        EnsureSupported(mode);
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        var endpoints = UnixDomainSocketIpc.EndpointsForCurrentUser();
        return channel == AgentIpcChannel.Secret ? endpoints.Secret : endpoints.Normal;
    }

    /// <summary>
    /// A master key file only this user can read, rather than the session keyring.
    /// </summary>
    /// <remarks>
    /// libsecret is the obvious choice and the wrong one: a lingering agent is designed to run with
    /// nobody signed in, where there is no session bus and no unlocked keyring, so every vault read
    /// would fail exactly when unattended work depends on it.
    /// </remarks>
    public ISecretProtector CreateSecretProtector(AgentHostMode mode, AgentPaths paths)
    {
        EnsureSupported(mode);
        ArgumentNullException.ThrowIfNull(paths);
        return new UnixKeyFileSecretProtector(Path.Combine(paths.AgentDirectory, "secrets"));
    }

    public IRuntimeSecretFileMaterializer CreateRuntimeSecretFileMaterializer(AgentPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new UnixRuntimeSecretFileMaterializer(paths.RuntimeSecretsDirectory);
    }

    public IpcTrustModel ResolveTrustModel(AgentHostMode mode)
    {
        EnsureSupported(mode);
        return IpcTrustModel.SameUser;
    }

    private static void EnsureSupported(AgentHostMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!Supported.Contains(mode))
        {
            throw new PlatformNotSupportedException(
                $"The Linux agent has no {mode} mode; it runs as a per-user process.");
        }
    }
}
