using System.Net.Sockets;

namespace StorageHub.Ipc.Unix;

/// <summary>A Unix domain socket on the local machine.</summary>
/// <remarks>
/// Unlike a named pipe, the path grants nothing by itself. What keeps another user out is that the
/// directory holding it is theirs to enter or not, which is why the endpoint is always validated
/// together with that directory.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed record UnixSocketEndpoint(string SocketPath) : IpcEndpoint
{
    public override string Moniker => SocketPath;
}

/// <summary>StorageHub's IPC over Unix domain sockets.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
public sealed class UnixSocketIpcTransport : IIpcTransport
{
    public string Name => "unix-socket";

    /// <summary>
    /// A socket in a private directory carries secrets; a machine-service endpoint does not exist
    /// here at all.
    /// </summary>
    /// <remarks>
    /// The Windows check this replaces asked whether the host was Windows, and the answer here would
    /// have been no. The actual requirement is that the channel reaches one account and cannot be
    /// observed or impersonated by another, and a 0700 directory on tmpfs meets it at least as well
    /// as a named pipe does - arguably better, since the Windows pipe namespace is global and its
    /// names are enumerable, while this path is not even listable by another user.
    /// </remarks>
    public bool SupportsConfidentialChannel(IpcEndpoint endpoint, IpcTrustModel trustModel)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint is not UnixSocketEndpoint socket || trustModel != IpcTrustModel.SameUser)
        {
            return false;
        }

        var directory = Path.GetDirectoryName(socket.SocketPath)!;
        if (!Directory.Exists(directory))
        {
            // Asked before the endpoint is published, which is the normal case: the answer is about
            // what this transport will create, and Listen creates it private or fails.
            return true;
        }

        try
        {
            UnixIpcSocketDirectory.Verify(directory);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A directory that already exists and cannot be proven private is not a channel secrets
            // may go down.
            return false;
        }
    }

    public void ValidateEndpoint(IpcEndpoint endpoint, IpcTrustModel trustModel)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint is not UnixSocketEndpoint socket)
        {
            throw new ArgumentException(
                $"The Unix socket transport cannot address a {endpoint.GetType().Name}.",
                nameof(endpoint));
        }

        if (!Enum.IsDefined(trustModel))
        {
            throw new ArgumentOutOfRangeException(nameof(trustModel), "The IPC trust model is invalid.");
        }

        // A machine-wide service is a Windows shape. The Linux agent is a per-user process by
        // design - that is what makes the vault, the socket and the data root all one account's -
        // so there is nothing here for an access list to describe, and pretending otherwise would
        // publish an endpoint with no boundary at all.
        if (trustModel == IpcTrustModel.MachineService)
        {
            throw new ArgumentException(
                "The Unix socket transport has no machine-service mode; the Linux agent runs per user.",
                nameof(trustModel));
        }

        UnixIpcSocketDirectory.ValidateSocketPath(socket.SocketPath);
    }

    public void ValidateListenOptions(IpcListenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateEndpoint(options.Endpoint, options.TrustModel);

        if (options.MaxConcurrentClients < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxConcurrentClients,
                "The maximum concurrent client count must be at least 1.");
        }

        if (options.PermittedPrincipals.Count != 0)
        {
            throw new ArgumentException(
                "The Unix socket transport admits only the owning user, so an access list has no meaning.",
                nameof(options));
        }
    }

    public IIpcListener Listen(IpcListenOptions options)
    {
        ValidateListenOptions(options);
        var endpoint = (UnixSocketEndpoint)options.Endpoint;
        var directory = Path.GetDirectoryName(endpoint.SocketPath)!;
        UnixIpcSocketDirectory.EnsurePrivate(directory);
        ClearStaleSocket(endpoint.SocketPath);

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(endpoint.SocketPath));
            socket.Listen(options.MaxConcurrentClients);
            return new UnixSocketIpcListener(endpoint, socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async ValueTask<IIpcConnection> ConnectAsync(
        IpcConnectOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateEndpoint(options.Endpoint, options.TrustModel);
        var endpoint = (UnixSocketEndpoint)options.Endpoint;

        // Verified before connecting, not after: if the directory is not private then whatever is
        // listening on that path is not necessarily the agent.
        UnixIpcSocketDirectory.Verify(Path.GetDirectoryName(endpoint.SocketPath)!);

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(
                new UnixDomainSocketEndPoint(endpoint.SocketPath),
                cancellationToken).ConfigureAwait(false);
            return new UnixSocketIpcConnection(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Removes a socket file left behind by an agent that is gone, and refuses one that is not.
    /// </summary>
    /// <remarks>
    /// bind() fails with EADDRINUSE whenever the file exists, whether or not anything is listening,
    /// and a Unix socket file is not cleaned up when its process dies. Deleting it unconditionally
    /// would let a second agent steal the endpoint from a healthy first one, so the question is
    /// answered by asking: a refused connection means nobody is home.
    /// </remarks>
    private static void ClearStaleSocket(string socketPath)
    {
        if (!File.Exists(socketPath))
        {
            return;
        }

        using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            probe.Connect(new UnixDomainSocketEndPoint(socketPath));
        }
        catch (SocketException error) when (error.SocketErrorCode is SocketError.ConnectionRefused)
        {
            File.Delete(socketPath);
            return;
        }

        throw new IOException(
            $"Another StorageHub agent is already listening on '{socketPath}'.");
    }
}
