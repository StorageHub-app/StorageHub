using StorageHub.Ipc;
using StorageHub.Security;

namespace StorageHub.Agent;

/// <summary>Which of the agent's two channels an endpoint is for.</summary>
public enum AgentIpcChannel
{
    /// <summary>Status, browsing, transfers, sync, schedules.</summary>
    Normal = 0,

    /// <summary>Secret enrollment and rotation, on a channel of its own.</summary>
    Secret = 1,
}

/// <summary>
/// The two roots an agent writes under.
/// </summary>
/// <remarks>
/// One root was enough while Windows was the only host, because %LOCALAPPDATA% is a reasonable home
/// for everything. It is not enough on Linux: a socket has to live on tmpfs to be cleared when the
/// session ends, and plaintext key material materialised for a provider should never touch a disk
/// at all. Splitting runtime state from durable state is what makes both true.
///
/// Deliberately two rather than five. Strict XDG would also separate config, state and cache, which
/// would touch every Environment.SpecialFolder call site plus CodeLogic's own root contract. Two
/// buys the property that matters at a fraction of the cost; the rest is a later refinement.
/// </remarks>
public sealed record AgentPaths(string DataRoot, string RuntimeRoot)
{
    /// <summary>The agent's own subtree of the data root.</summary>
    public string AgentDirectory => Path.Combine(DataRoot, AgentHostLayout.AgentDirectoryName);

    public string DatabasePath => Path.Combine(AgentDirectory, AgentHostLayout.DatabaseFileName);

    public string VaultDirectory => Path.Combine(AgentDirectory, "vault");

    /// <summary>
    /// Where a provider's key material is written for the life of one connection. On the runtime
    /// root, so on Linux it is tmpfs and never reaches a disk.
    /// </summary>
    public string RuntimeSecretsDirectory => Path.Combine(RuntimeRoot, "runtime-secrets");
}

/// <summary>
/// How the agent is made to start again without the desktop asking.
/// </summary>
/// <remarks>
/// Windows spells this as a value under HKCU\...\Run; Linux spells it as a systemd user unit. The
/// two are the same decision, which is why AgentHostMode does not grow a member for either: a mode
/// says whether there is a registration, and the platform says what one looks like.
/// </remarks>
public interface IAutostartRegistration
{
    bool IsRegistered { get; }

    bool TryRegister(string commandLine);

    bool TryRemove();

    /// <summary>
    /// Whether the registration outlives signing out. A Windows service does; an HKCU Run entry
    /// does not; a systemd user unit does only once lingering is enabled, which is a separate and
    /// separately consented step.
    /// </summary>
    bool SurvivesSignOut { get; }
}

/// <summary>
/// Everything about running the agent that differs between operating systems.
/// </summary>
/// <remarks>
/// The point is that the host composes one of these and then contains no further platform branch.
/// CA1416 enforces the rest: an implementation lives in an assembly marked for its platform, so
/// reaching one without a guard is a build error rather than a runtime surprise.
///
/// </remarks>
public interface IAgentPlatform
{
    /// <summary>A name for this platform, for diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// The modes this platform can host. Both platforms host the same two now - they differ only by
    /// whether an autostart registration outlives the application - so this no longer separates
    /// them, and is kept because a platform is still the right thing to ask.
    /// </summary>
    IReadOnlySet<AgentHostMode> SupportedHostModes { get; }

    /// <summary>
    /// Which mode an agent on this machine is actually in.
    /// </summary>
    /// <remarks>
    /// Discovered rather than remembered, and asked by the desktop rather than by the agent: the
    /// agent knows its own mode because it was started in it, while the desktop has to find out
    /// which agent is there before it can pick an endpoint and a trust model. A stored preference
    /// would be the wrong answer whenever it disagreed with the machine - a service installed since
    /// it was written, or an autostart entry removed - and the failure looks like "the agent is
    /// offline" rather than like a stale setting.
    /// </remarks>
    AgentHostMode DiscoverHostMode();

    /// <summary>
    /// How a client checks that the thing that answered is the agent, for a given mode.
    /// </summary>
    /// <remarks>
    /// This took a host mode, because the property being verified used to depend on it: a service
    /// published a machine-wide endpoint whose owner had to be checked. Only same-user endpoints
    /// exist now, so <see cref="ServerAuthenticator"/> below is the whole answer.
    /// </remarks>
    AgentPaths ResolvePaths(AgentHostMode mode);

    IpcEndpoint ResolveEndpoint(AgentHostMode mode, AgentIpcChannel channel);

    IIpcTransport Transport { get; }

    IIpcPeerAuthorizer PeerAuthorizer { get; }

    IIpcServerAuthenticator ServerAuthenticator { get; }

    IAutostartRegistration Autostart { get; }

    /// <summary>
    /// Whether this platform has a way to run an administrative step with more privilege than the
    /// caller has. Windows does, through UAC. The Linux agent is designed never to need one.
    /// </summary>
    bool CanElevate { get; }

    /// <summary>
    /// What protects the vault's entries at rest.
    /// </summary>
    /// <remarks>
    /// Takes the mode because the two move together: a Windows service protects with a machine key
    /// its administrators can read, while a session agent protects with the user's own. Getting
    /// that pairing wrong does not fail - it writes a vault the other mode cannot open, which is
    /// why the scheme is stamped into every envelope and a mismatch is refused rather than decoded.
    /// </remarks>
    ISecretProtector CreateSecretProtector(AgentHostMode mode, AgentPaths paths);

    /// <summary>
    /// Where a provider's key material is written for the life of one connection.
    /// </summary>
    /// <remarks>
    /// Plaintext, necessarily: SSH.NET and the TLS stack want a path, not bytes. What differs by
    /// platform is how briefly it exists and how private it is while it does.
    /// </remarks>
    IRuntimeSecretFileMaterializer CreateRuntimeSecretFileMaterializer(AgentPaths paths);
}
