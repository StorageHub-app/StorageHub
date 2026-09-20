namespace StorageHub.Ipc.Windows;

/// <summary>
/// Builds StorageHub's IPC over named pipes with the pieces that belong together.
/// </summary>
/// <remarks>
/// The server and the client take a transport and an authenticator rather than choosing one, which
/// is what lets a second transport exist. Assembling the matching set once, here, is what keeps a
/// call site from having to know which pieces belong together.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class WindowsNamedPipeIpc
{
    /// <summary>The named-pipe transport. Stateless, so one instance serves everything.</summary>
    public static IIpcTransport Transport { get; } = new NamedPipeIpcTransport();

    /// <summary>
    /// Windows enforces pipe access in the kernel, so by the time a connection is accepted the
    /// decision has already been made.
    /// </summary>
    public static IIpcPeerAuthorizer PeerAuthorizer { get; } = new WindowsPipeAccessControlAuthorizer();

    /// <summary>
    /// How a client decides the thing that answered is the agent. Nothing to do: the pipe is
    /// restricted to the creating account, so Windows has already refused anyone who could have
    /// squatted on the name.
    /// </summary>
    public static IIpcServerAuthenticator ServerAuthenticator { get; } = new WindowsSameUserPipeAuthenticator();

    public static IpcServerSubsystem CreateServer(
        IpcServerOptions options,
        Func<IpcSession, CancellationToken, Task>? sessionHandler = null) =>
        new(Transport, options, PeerAuthorizer, sessionHandler);

    public static IpcClient CreateClient(IpcClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new IpcClient(Transport, options, ServerAuthenticator);
    }
}
