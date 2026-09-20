using StorageHub.Contracts.Ipc;

namespace StorageHub.Ipc;

/// <summary>
/// Who may open the agent's pipe.
/// </summary>
public enum IpcPipeAccess
{
    /// <summary>
    /// Windows restricts the pipe to the account that created it, and the client checks the
    /// server's owner matches itself. Nothing else on the machine can connect or impersonate the
    /// agent, which is why it stays the default for a user-session agent.
    /// </summary>
    CurrentUserOnly = 0,

    /// <summary>
    /// An explicit ACL, for a service whose clients sign in as somebody else. The name is no
    /// longer the boundary, so the ACL has to be: LocalSystem and Administrators get full access,
    /// each permitted account gets read/write, and nobody else is granted anything.
    /// </summary>
    MachineService = 1,
}

public sealed record NamedPipeIpcServerOptions
{
    public required string PipeName { get; init; }

    /// <summary>How the pipe is secured. See <see cref="IpcPipeAccess"/>.</summary>
    public IpcPipeAccess Access { get; init; } = IpcPipeAccess.CurrentUserOnly;

    /// <summary>
    /// Accounts allowed to connect under <see cref="IpcPipeAccess.MachineService"/>, as SDDL SID
    /// strings. Required in that mode and ignored otherwise: an empty list would publish a pipe
    /// only administrators could reach, which silently breaks the desktop rather than failing at
    /// configuration time.
    /// </summary>
    public IReadOnlyList<string> PermittedUserSids { get; init; } = [];

    public int MaxConcurrentClients { get; init; } = 8;

    public ProtocolVersion ProtocolVersion { get; init; } = ProtocolVersion.Current;

    public required string AgentVersion { get; init; }

    public Guid AgentInstanceId { get; init; } = Guid.NewGuid();

    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan SessionIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Normal by default. Secret mode is valid only on a current-user-only pipe.</summary>
    public IpcFrameKind FrameKind { get; init; } = IpcFrameKind.Normal;
}

public sealed record NamedPipeIpcClientOptions
{
    public required string PipeName { get; init; }

    /// <summary>
    /// Must match the agent's mode. Under <see cref="IpcPipeAccess.MachineService"/> the client
    /// cannot use CurrentUserOnly -- the server is a different account -- so it verifies the pipe's
    /// owner instead. Without that check any local process could create another instance of the
    /// machine-wide pipe name and collect whatever the desktop sends it.
    ///
    /// Required rather than defaulted. It used to default to CurrentUserOnly, which is correct for
    /// a session agent and silently wrong for a service: Windows refuses the connection, the client
    /// reports the agent as absent, and nothing anywhere says the two disagreed about the mode.
    /// A caller that has to name it cannot forget to think about it.
    /// </summary>
    public required IpcPipeAccess Access { get; init; }

    public required string ClientName { get; init; }

    public required string ClientVersion { get; init; }

    public Guid ClientInstanceId { get; init; } = Guid.NewGuid();

    public ProtocolVersion ProtocolVersion { get; init; } = ProtocolVersion.Current;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public int MaxConnectAttempts { get; init; } = 5;

    public TimeSpan InitialReconnectDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan MaximumReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Normal by default. Secret mode is valid only on a current-user-only pipe.</summary>
    public IpcFrameKind FrameKind { get; init; } = IpcFrameKind.Normal;
}
