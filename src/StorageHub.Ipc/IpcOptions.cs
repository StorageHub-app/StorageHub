using StorageHub.Contracts.Ipc;

namespace StorageHub.Ipc;

public sealed record IpcServerOptions
{
    /// <summary>Where to listen. The transport decides what kind of endpoint it can address.</summary>
    public required IpcEndpoint Endpoint { get; init; }

    /// <summary>Who may reach it. See <see cref="IpcTrustModel"/>.</summary>
    public IpcTrustModel TrustModel { get; init; } = IpcTrustModel.SameUser;

    /// <summary>
    /// Principals allowed to connect under <see cref="IpcTrustModel.MachineService"/>, in the form
    /// that transport's authority uses - SDDL SID strings for Windows. Required in that mode and
    /// ignored otherwise: an empty list would publish an endpoint only administrators could reach,
    /// which silently breaks the desktop rather than failing at configuration time.
    /// </summary>
    public IReadOnlyList<string> PermittedPrincipals { get; init; } = [];

    public int MaxConcurrentClients { get; init; } = 8;

    public ProtocolVersion ProtocolVersion { get; init; } = ProtocolVersion.Current;

    public required string AgentVersion { get; init; }

    public Guid AgentInstanceId { get; init; } = Guid.NewGuid();

    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan SessionIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Normal by default. Secret mode is valid only where the transport can say the channel reaches
    /// one account and cannot be observed or impersonated by another.
    /// </summary>
    public IpcFrameKind FrameKind { get; init; } = IpcFrameKind.Normal;
}

public sealed record IpcClientOptions
{
    public required IpcEndpoint Endpoint { get; init; }

    /// <summary>
    /// Must match the agent's mode. Under <see cref="IpcTrustModel.MachineService"/> the client
    /// cannot rely on the address being private - the server is a different account - so it
    /// authenticates the server instead. Without that check any local process could answer on the
    /// machine-wide address and collect whatever the desktop sends it.
    ///
    /// Required rather than defaulted. It used to default to same-user, which is correct for a
    /// session agent and silently wrong for a service: the connection is refused, the client reports
    /// the agent as absent, and nothing anywhere says the two disagreed about the mode. A caller
    /// that has to name it cannot forget to think about it.
    /// </summary>
    public required IpcTrustModel TrustModel { get; init; }

    public required string ClientName { get; init; }

    public required string ClientVersion { get; init; }

    public Guid ClientInstanceId { get; init; } = Guid.NewGuid();

    public ProtocolVersion ProtocolVersion { get; init; } = ProtocolVersion.Current;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public int MaxConnectAttempts { get; init; } = 5;

    public TimeSpan InitialReconnectDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    public TimeSpan MaximumReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Normal by default. Secret mode follows the same rule as on the server.</summary>
    public IpcFrameKind FrameKind { get; init; } = IpcFrameKind.Normal;
}
