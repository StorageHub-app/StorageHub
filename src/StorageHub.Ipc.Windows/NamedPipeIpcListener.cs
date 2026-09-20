using System.IO.Pipes;

namespace StorageHub.Ipc.Windows;

/// <summary>
/// A published named pipe that hands out one connection per accepted client.
/// </summary>
/// <remarks>
/// Named pipes conflate the listener and the connection: each instance is created unconnected,
/// waits, and then <em>is</em> the connection. So the first instance is created in the constructor
/// rather than on the first accept, which keeps the guarantee the agent relied on - the pipe exists
/// by the time the subsystem reports itself started, and a desktop that connects immediately
/// afterwards finds it there instead of racing it.
/// </remarks>
internal sealed class NamedPipeIpcListener : IIpcListener
{
    private readonly NamedPipeEndpoint _endpoint;
    private readonly int _maxConcurrentClients;
    private readonly PipeSecurity? _security;
    private readonly object _pendingGate = new();
    private NamedPipeServerStream? _pending;
    private bool _disposed;

    internal NamedPipeIpcListener(
        NamedPipeEndpoint endpoint,
        int maxConcurrentClients,
        PipeSecurity? security)
    {
        _endpoint = endpoint;
        _maxConcurrentClients = maxConcurrentClients;
        _security = security;
        _pending = CreateInstance();
    }

    public IpcEndpoint Endpoint => _endpoint;

    public async ValueTask<IIpcConnection> AcceptAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        NamedPipeServerStream instance;
        lock (_pendingGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            instance = _pending ??= CreateInstance();
        }

        try
        {
            await instance.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleasePending(instance);
            await instance.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        ReleasePending(instance);
        return new NamedPipeIpcConnection(instance);
    }

    public ValueTask DisposeAsync()
    {
        NamedPipeServerStream? pending;
        lock (_pendingGate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            pending = _pending;
            _pending = null;
        }

        // Disposing the instance blocked in WaitForConnectionAsync is what unblocks it, and is how
        // the agent stops accepting.
        pending?.Dispose();
        return ValueTask.CompletedTask;
    }

    private void ReleasePending(NamedPipeServerStream instance)
    {
        lock (_pendingGate)
        {
            if (ReferenceEquals(_pending, instance))
            {
                _pending = null;
            }
        }
    }

    private NamedPipeServerStream CreateInstance()
    {
        const PipeOptions BaseOptions = PipeOptions.Asynchronous;
        if (_security is not null)
        {
            // A service and its clients are different accounts, so CurrentUserOnly would make the
            // pipe unreachable. The ACL takes over as the boundary: LocalSystem and Administrators
            // to administer it, the accounts that opted in to use it, and no one else. Notably no
            // Everyone or Authenticated Users ACE -- the pipe name is machine-wide and therefore
            // guessable, so the name grants nothing on its own.
            return NamedPipeServerStreamAcl.Create(
                _endpoint.PipeName,
                PipeDirection.InOut,
                _maxConcurrentClients,
                PipeTransmissionMode.Byte,
                BaseOptions,
                inBufferSize: 0,
                outBufferSize: 0,
                _security);
        }

        return new NamedPipeServerStream(
            _endpoint.PipeName,
            PipeDirection.InOut,
            _maxConcurrentClients,
            PipeTransmissionMode.Byte,
            BaseOptions | PipeOptions.CurrentUserOnly);
    }
}
