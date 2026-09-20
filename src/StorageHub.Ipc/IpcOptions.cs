using StorageHub.Contracts.Ipc;

namespace StorageHub.Ipc;

public sealed record IpcServerOptions
{
    /// <summary>Where to listen. The transport decides what kind of endpoint it can address.</summary>
    public required IpcEndpoint Endpoint { get; init; }

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
