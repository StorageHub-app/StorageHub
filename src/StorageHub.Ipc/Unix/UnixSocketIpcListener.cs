using System.Net.Sockets;

namespace StorageHub.Ipc.Unix;

/// <summary>
/// A bound Unix socket that accepts one connection at a time.
/// </summary>
/// <remarks>
/// Simpler than its named-pipe counterpart: a listening socket and an accepted connection are
/// different objects here, so there is no pending instance to pre-create and no instance limit to
/// respect. Accept returns a new socket and the listener stays bound.
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class UnixSocketIpcListener : IIpcListener
{
    private readonly UnixSocketEndpoint _endpoint;
    private readonly Socket _socket;
    private bool _disposed;

    internal UnixSocketIpcListener(UnixSocketEndpoint endpoint, Socket socket)
    {
        _endpoint = endpoint;
        _socket = socket;
    }

    public IpcEndpoint Endpoint => _endpoint;

    public async ValueTask<IIpcConnection> AcceptAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var accepted = await _socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return new UnixSocketIpcConnection(accepted);
        }
        catch
        {
            accepted.Dispose();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _socket.Dispose();

        // The socket file outlives the process that bound it, so an agent that does not clean up
        // leaves one for the next start to reason about. Removing it here keeps that rare.
        try
        {
            File.Delete(_endpoint.SocketPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A path that cannot be removed is the next start's problem, and it knows how to
            // tell a stale socket from a live one.
        }

        return ValueTask.CompletedTask;
    }
}
