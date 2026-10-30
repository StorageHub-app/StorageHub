using System.Collections.Concurrent;
using System.Text.Json;
using StorageHub.Contracts.Agent;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Ipc;

public sealed class IpcServerSubsystem : IAgentSubsystem, IAsyncDisposable
{
    private readonly IIpcTransport _transport;
    private readonly IpcServerOptions _options;
    private readonly IIpcPeerAuthorizer _peerAuthorizer;
    private readonly Func<IpcSession, CancellationToken, Task>? _sessionHandler;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _clientSlots;
    private readonly ConcurrentDictionary<int, IIpcConnection> _activeConnections = new();
    private readonly ConcurrentDictionary<int, Task> _sessionTasks = new();
    private CancellationTokenSource? _lifetime;
    private IIpcListener? _listener;
    private Task? _acceptLoop;
    private Exception? _lastFailure;
    private int _nextSessionId;
    private int _activeClientCount;
    private int _peakClientCount;
    private int _isRunning;
    private bool _initialized;
    private bool _disposed;

    public IpcServerSubsystem(
        IIpcTransport transport,
        IpcServerOptions options,
        IIpcPeerAuthorizer peerAuthorizer,
        Func<IpcSession, CancellationToken, Task>? sessionHandler = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(peerAuthorizer);
        ValidateOptions(transport, options);
        _transport = transport;
        _options = options;
        _peerAuthorizer = peerAuthorizer;
        _sessionHandler = sessionHandler;
        _clientSlots = new SemaphoreSlim(options.MaxConcurrentClients, options.MaxConcurrentClients);
    }

    public string Name => _options.FrameKind == IpcFrameKind.Secret
        ? "Secret IPC"
        : "Local IPC";

    public bool CanRunInRecoveryMode => true;

    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;

    public int ActiveClientCount => Volatile.Read(ref _activeClientCount);

    public int PeakClientCount => Volatile.Read(ref _peakClientCount);

    public async Task<SubsystemInitializationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _initialized = true;
            return SubsystemInitializationResult.Ready();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? lifetime = null;
        IIpcListener? listener = null;
        try
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("The local IPC subsystem must be initialized before it starts.");
            }

            if (IsRunning)
            {
                return;
            }

            if (_lifetime is not null || _acceptLoop is not null)
            {
                throw new InvalidOperationException(
                    "The local IPC subsystem must be stopped after a failed run before it can restart.");
            }

            Volatile.Write(ref _lastFailure, null);
            lifetime = new CancellationTokenSource();

            // The endpoint is published here rather than inside the accept loop, so that by the
            // time this returns a client which connects immediately finds it already there.
            listener = _transport.Listen(ToListenOptions(_options));

            _lifetime = lifetime;
            lifetime = null;
            _listener = listener;
            listener = null;
            Volatile.Write(ref _isRunning, 1);
            _acceptLoop = AcceptConnectionsAsync(_listener, _lifetime.Token);
        }
        catch (Exception error)
        {
            Volatile.Write(ref _isRunning, 0);
            if (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                Volatile.Write(ref _lastFailure, error);
            }

            if (listener is not null)
            {
                await listener.DisposeAsync().ConfigureAwait(false);
            }

            lifetime?.Dispose();
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsRunning && _lifetime is null && _acceptLoop is null)
            {
                return;
            }

            Volatile.Write(ref _isRunning, 0);
            _lifetime?.Cancel();
            if (_listener is not null)
            {
                await _listener.DisposeAsync().ConfigureAwait(false);
                _listener = null;
            }

            foreach (var connection in _activeConnections.Values)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            if (_acceptLoop is not null)
            {
                await IgnoreExpectedShutdownExceptionAsync(_acceptLoop).ConfigureAwait(false);
            }

            var sessions = _sessionTasks.Values.ToArray();
            if (sessions.Length != 0)
            {
                await Task.WhenAll(sessions).ConfigureAwait(false);
            }

            _acceptLoop = null;
            _lifetime?.Dispose();
            _lifetime = null;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task<SubsystemHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lastFailure = Volatile.Read(ref _lastFailure);
        if (lastFailure is not null)
        {
            return Task.FromResult(SubsystemHealth.Unhealthy(
                $"Local IPC failed: {lastFailure.GetType().Name}."));
        }

        if (IsRunning)
        {
            return Task.FromResult(SubsystemHealth.Healthy(
                $"Local IPC is accepting connections ({ActiveClientCount}/{_options.MaxConcurrentClients} active)."));
        }

        return Task.FromResult(SubsystemHealth.Degraded("Local IPC is stopped."));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
        _lifetime?.Dispose();
        _clientSlots.Dispose();
        _lifecycleGate.Dispose();
    }

    private static IpcListenOptions ToListenOptions(IpcServerOptions options) => new()
    {
        Endpoint = options.Endpoint,
        MaxConcurrentClients = options.MaxConcurrentClients,
    };

    private static void ValidateOptions(IIpcTransport transport, IpcServerOptions options)
    {
        // The whole listen configuration, not just the address: a client limit the transport
        // cannot instantiate has to be refused here rather than at StartAsync, or the agent starts,
        // reports itself healthy, and fails at accept.
        transport.ValidateListenOptions(ToListenOptions(options));
        if (!Enum.IsDefined(options.FrameKind))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The IPC frame kind is invalid.");
        }

        // This used to ask whether the host was Windows. The requirement was never Windows: it is
        // that the channel reaches one account and cannot be observed or impersonated by another,
        // which a transport can answer for its own endpoints.
        if (options.FrameKind == IpcFrameKind.Secret &&
            !transport.SupportsConfidentialChannel(options.Endpoint))
        {
            throw new ArgumentException(
                $"The {transport.Name} transport cannot carry a secret channel on {options.Endpoint}.",
                nameof(options));
        }

        IpcProtocolValidation.ValidateIdentity(options.AgentVersion, nameof(options.AgentVersion));
        if (options.AgentInstanceId == Guid.Empty)
        {
            throw new ArgumentException("The agent instance ID must not be empty.", nameof(options));
        }

        IpcProtocolValidation.ValidatePositiveTimeout(options.HandshakeTimeout, nameof(options.HandshakeTimeout));
        IpcProtocolValidation.ValidatePositiveOperationTimeout(
            options.SessionIdleTimeout,
            nameof(options.SessionIdleTimeout));
        IpcProtocolValidation.ValidatePositiveOperationTimeout(
            options.RequestTimeout,
            nameof(options.RequestTimeout));
    }

    private async Task AcceptConnectionsAsync(
        IIpcListener listener,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var ownsSlot = false;
            var handedOff = false;
            IIpcConnection? connection = null;
            try
            {
                await _clientSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                ownsSlot = true;

                connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);

                // Where the operating system does not enforce the address itself, this is the
                // check that keeps another account out. Where it does, it has already run.
                if (!_peerAuthorizer.IsAuthorized(connection.Peer, out _))
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    connection = null;
                    continue;
                }

                var sessionId = Interlocked.Increment(ref _nextSessionId);
                _activeConnections[sessionId] = connection;
                var sessionTask = RunSessionAsync(sessionId, connection, cancellationToken);
                _sessionTasks[sessionId] = sessionTask;
                _ = ObserveSessionAsync(sessionId, sessionTask);
                handedOff = true;
                connection = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                await RecordFatalAcceptLoopFailureAsync(error).ConfigureAwait(false);
                return;
            }
            finally
            {
                if (connection is not null)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }

                if (ownsSlot && !handedOff)
                {
                    _clientSlots.Release();
                }
            }
        }
    }

    private async Task RecordFatalAcceptLoopFailureAsync(Exception error)
    {
        Volatile.Write(ref _lastFailure, error);
        Volatile.Write(ref _isRunning, 0);
        try
        {
            _lifetime?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        foreach (var connection in _activeConnections.Values)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RunSessionAsync(
        int sessionId,
        IIpcConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var session = await NegotiateSessionAsync(connection, sessionCancellation).ConfigureAwait(false);
            if (session is null)
            {
                return;
            }

            await using (session.ConfigureAwait(false))
            {
                RecordClientStarted();
                try
                {
                    if (_sessionHandler is not null)
                    {
                        await _sessionHandler(session, sessionCancellation.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _activeClientCount);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Lifetime cancellation and unauthenticated handshake timeouts are both session-local.
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (JsonException)
        {
            // Malformed or incompatible client payloads are rejected by closing only that session.
        }
        catch (InvalidDataException)
        {
            // Invalid lengths, envelopes, and sequence values are untrusted client input.
        }
        catch (IpcProtocolNegotiationException)
        {
            // Protocol failures are isolated to the untrusted client connection.
        }
        catch (IOException)
        {
            // A local client can disconnect at any point. This is session-local, not subsystem failure.
        }
        catch (Exception error)
        {
            _lastFailure = error;
        }
        finally
        {
            _activeConnections.TryRemove(sessionId, out _);
            await connection.DisposeAsync().ConfigureAwait(false);
            _clientSlots.Release();
        }
    }

    private async Task<IpcSession?> NegotiateSessionAsync(
        IIpcConnection connection,
        CancellationTokenSource sessionCancellation)
    {
        using var handshakeCancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation.Token);
        handshakeCancellation.CancelAfter(_options.HandshakeTimeout);
        var envelope = await LengthPrefixedJsonChannel.ReadAsync<IpcEnvelope>(
            connection.Stream,
            IpcFrameLimits.NormalMaxBytes,
            cancellationToken: handshakeCancellation.Token).ConfigureAwait(false);
        IpcProtocolValidation.ValidateHandshakeEnvelope(envelope, IpcProtocol.HelloRequestMessageType);
        HelloRequest hello;
        try
        {
            hello = envelope.DeserializePayload<HelloRequest>();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or NotSupportedException)
        {
            throw new IpcProtocolNegotiationException("The client returned an invalid handshake payload.", error);
        }

        string? rejectionReason = null;
        if (string.IsNullOrWhiteSpace(hello.ClientName) || string.IsNullOrWhiteSpace(hello.ClientVersion))
        {
            rejectionReason = "Client name and version are required.";
        }
        else if (hello.ClientName.Length > 256 || hello.ClientVersion.Length > 256)
        {
            rejectionReason = "Client name and version cannot exceed 256 characters.";
        }
        else if (hello.ClientInstanceId == Guid.Empty)
        {
            rejectionReason = "Client instance ID must not be empty.";
        }
        else if (!_options.ProtocolVersion.IsCompatibleWith(hello.ProtocolVersion))
        {
            rejectionReason =
                $"Client protocol {hello.ProtocolVersion} is incompatible with agent protocol {_options.ProtocolVersion}.";
        }

        var accepted = rejectionReason is null;
        var negotiatedVersion = accepted
            ? new ProtocolVersion(
                _options.ProtocolVersion.Major,
                Math.Min(_options.ProtocolVersion.Minor, hello.ProtocolVersion.Minor))
            : _options.ProtocolVersion;
        var response = new HelloResponse(
            negotiatedVersion,
            accepted,
            _options.AgentVersion,
            _options.AgentInstanceId,
            rejectionReason);
        var responseEnvelope = IpcEnvelope.Create(
            IpcProtocol.HelloResponseMessageType,
            envelope.RequestId,
            0,
            response);
        await LengthPrefixedJsonChannel.WriteAsync(
            connection.Stream,
            responseEnvelope,
            IpcFrameLimits.NormalMaxBytes,
            cancellationToken: handshakeCancellation.Token).ConfigureAwait(false);

        return accepted
            ? new IpcSession(
                connection,
                hello,
                negotiatedVersion,
                sessionCancellation,
                _options.SessionIdleTimeout,
                _options.RequestTimeout,
                _options.FrameKind)
            : null;
    }

    private void RecordClientStarted()
    {
        var current = Interlocked.Increment(ref _activeClientCount);
        var observedPeak = Volatile.Read(ref _peakClientCount);
        while (current > observedPeak)
        {
            var priorPeak = Interlocked.CompareExchange(ref _peakClientCount, current, observedPeak);
            if (priorPeak == observedPeak)
            {
                break;
            }

            observedPeak = priorPeak;
        }
    }

    private async Task ObserveSessionAsync(int sessionId, Task sessionTask)
    {
        await sessionTask.ConfigureAwait(false);
        _sessionTasks.TryRemove(sessionId, out _);
    }

    private static async Task IgnoreExpectedShutdownExceptionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
