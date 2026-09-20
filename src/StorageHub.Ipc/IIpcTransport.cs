namespace StorageHub.Ipc;

/// <summary>One accepted or established connection, as a stream plus who is on it.</summary>
public interface IIpcConnection : IAsyncDisposable
{
    /// <summary>The bytes. Framing is the protocol's business, not the transport's.</summary>
    Stream Stream { get; }

    bool IsConnected { get; }

    /// <summary>The peer as the operating system reports it.</summary>
    IpcPeerIdentity Peer { get; }
}

/// <summary>
/// A published endpoint that hands out connections.
/// </summary>
/// <remarks>
/// The endpoint exists once <see cref="IIpcTransport.Listen"/> returns rather than when the first
/// accept runs, so a client that connects immediately afterwards finds it there. Disposing must
/// unblock a pending <see cref="AcceptAsync"/>; that is how the agent stops.
/// </remarks>
public interface IIpcListener : IAsyncDisposable
{
    IpcEndpoint Endpoint { get; }

    ValueTask<IIpcConnection> AcceptAsync(CancellationToken cancellationToken);
}

public sealed record IpcListenOptions
{
    public required IpcEndpoint Endpoint { get; init; }

    public required int MaxConcurrentClients { get; init; }
}

public sealed record IpcConnectOptions
{
    public required IpcEndpoint Endpoint { get; init; }
}

/// <summary>
/// How StorageHub reaches its agent on this operating system.
/// </summary>
/// <remarks>
/// Implementations own address creation and the operating system's own access control, and nothing
/// above them. The framing, the handshake and the session lifetime are the same everywhere and
/// stay out of here.
/// </remarks>
public interface IIpcTransport
{
    /// <summary>A name for this transport, for diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// Whether a channel carrying secrets can be trusted on this endpoint.
    /// </summary>
    /// <remarks>
    /// This replaces a check that asked whether the host was Windows. The requirement was never
    /// Windows; it is that the channel reaches one account and cannot be observed or impersonated
    /// by another. A named pipe restricted to the creating account satisfies that, and so does a
    /// socket in a directory only its owner may enter. Both are what these transports publish, so
    /// the answer turns on the endpoint alone.
    /// </remarks>
    bool SupportsConfidentialChannel(IpcEndpoint endpoint);

    /// <summary>
    /// Rejects an endpoint this transport cannot address, at configuration time rather than at the
    /// first connection.
    /// </summary>
    void ValidateEndpoint(IpcEndpoint endpoint);

    /// <summary>
    /// Rejects a listen configuration this transport cannot honour, at construction time.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ValidateEndpoint"/> because the endpoint can be perfectly valid
    /// while the rest of the configuration is not - a client limit past what the transport can
    /// instantiate being the case that matters. Publishing that would leave an agent that looks
    /// healthy and fails at accept, so it has to fail where it is configured rather than where it
    /// is started.
    /// </remarks>
    void ValidateListenOptions(IpcListenOptions options);

    IIpcListener Listen(IpcListenOptions options);

    ValueTask<IIpcConnection> ConnectAsync(IpcConnectOptions options, CancellationToken cancellationToken);
}

/// <summary>Decides, server side, whether an accepted peer may proceed to the handshake.</summary>
public interface IIpcPeerAuthorizer
{
    bool IsAuthorized(IpcPeerIdentity peer, out string? denialReason);
}

/// <summary>
/// Decides, client side, whether the thing that answered is really the agent.
/// </summary>
/// <remarks>
/// Runs after connect and before a single byte is sent, because the point is to not hand
/// credentials to a squatter that claimed the address first.
/// </remarks>
public interface IIpcServerAuthenticator
{
    void EnsureTrusted(IIpcConnection connection, IpcEndpoint endpoint);
}
