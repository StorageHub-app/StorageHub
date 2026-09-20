namespace StorageHub.Ipc.Windows;

/// <summary>
/// Builds StorageHub's IPC over named pipes with the pieces that belong together.
/// </summary>
/// <remarks>
/// The server and the client take a transport and an authenticator rather than choosing one, which
/// is what lets a second transport exist. That leaves a caller free to pair a machine-service
/// endpoint with the same-user authenticator, which would compile, connect, and quietly skip the
/// owner check that stops a squatter collecting secrets. Choosing the pair from the trust model in
/// one place removes that possibility from every call site.
/// </remarks>
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
    /// How a client decides the thing that answered is the agent: a same-user pipe is already
    /// restricted to this account, while a machine-service pipe is machine-wide and must have its
    /// owner checked.
    /// </summary>
    public static IIpcServerAuthenticator AuthenticatorFor(IpcTrustModel trustModel) =>
        trustModel == IpcTrustModel.MachineService
            ? new WindowsPipeOwnerAuthenticator()
            : new WindowsSameUserPipeAuthenticator();

    public static IpcServerSubsystem CreateServer(
        IpcServerOptions options,
        Func<IpcSession, CancellationToken, Task>? sessionHandler = null) =>
        new(Transport, options, PeerAuthorizer, sessionHandler);

    public static IpcClient CreateClient(IpcClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new IpcClient(Transport, options, AuthenticatorFor(options.TrustModel));
    }
}
