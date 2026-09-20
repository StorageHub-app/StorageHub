namespace StorageHub.Ipc.Unix;

/// <summary>
/// Builds StorageHub's IPC over Unix sockets with the pieces that belong together.
/// </summary>
/// <remarks>
/// The counterpart of WindowsNamedPipeIpc, and for the same reason: the server and client take a
/// transport and an authenticator rather than choosing one, so pairing them in a single place is
/// what stops a call site from assembling a combination that compiles and does not check anything.
/// </remarks>
public static class UnixDomainSocketIpc
{
    /// <summary>The Unix socket transport. Stateless, so one instance serves everything.</summary>
    public static IIpcTransport Transport { get; } = new UnixSocketIpcTransport();

    /// <summary>The uid check that is the whole boundary on this platform.</summary>
    public static IIpcPeerAuthorizer PeerAuthorizer { get; } = new UnixPeerCredentialAuthorizer();

    /// <summary>The same check, the other way round.</summary>
    public static IIpcServerAuthenticator ServerAuthenticator { get; } = new UnixSocketOwnerAuthenticator();

    public static IpcServerSubsystem CreateServer(
        IpcServerOptions options,
        Func<IpcSession, CancellationToken, Task>? sessionHandler = null) =>
        new(Transport, options, PeerAuthorizer, sessionHandler);

    public static IpcClient CreateClient(IpcClientOptions options) =>
        new(Transport, options, ServerAuthenticator);

    /// <summary>The agent's two endpoints for this user.</summary>
    public static (UnixSocketEndpoint Normal, UnixSocketEndpoint Secret) EndpointsForCurrentUser()
    {
        var directory = UnixIpcSocketDirectory.Resolve();
        return (new UnixSocketEndpoint(Path.Combine(directory, "agent.sock")),
                new UnixSocketEndpoint(Path.Combine(directory, "agent-secret.sock")));
    }
}
