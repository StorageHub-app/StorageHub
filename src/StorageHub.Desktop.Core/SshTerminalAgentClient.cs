using System.Text.Json;
using StorageHub.Ipc;
using StorageHub.Contracts.Ipc;
using StorageHub.Ipc.Windows;

namespace StorageHub.Desktop;

public interface ISshTerminalAgentClient : IAsyncDisposable
{
    Task<SshTerminalOpenResponse> OpenAsync(SshTerminalOpenRequest request, CancellationToken cancellationToken = default);
    Task<SshTerminalWriteResponse> WriteAsync(SshTerminalWriteRequest request, CancellationToken cancellationToken = default);
    Task<SshTerminalReadResponse> ReadAsync(SshTerminalReadRequest request, CancellationToken cancellationToken = default);
    Task<SshTerminalResizeResponse> ResizeAsync(SshTerminalResizeRequest request, CancellationToken cancellationToken = default);
    Task<SshTerminalCloseResponse> CloseAsync(SshTerminalCloseRequest request, CancellationToken cancellationToken = default);
}

/// <summary>One named-pipe connection to the agent, kept open across requests.</summary>
public interface ISshTerminalIpcTransport : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    ValueTask SendAsync(IpcEnvelope envelope, CancellationToken cancellationToken = default);

    ValueTask<IpcEnvelope> ReceiveAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Talks to the agent over two persistent pipe connections rather than one per call.
///
/// The old client built a fresh <see cref="IpcClient"/> for every request -- a full
/// connect, handshake and teardown for each keystroke and each poll. It uses two connections and
/// not one because the per-connection protocol is strictly serial: an in-flight long-poll read
/// would otherwise hold the gate and delay a keystroke by up to the whole wait budget. The read
/// pump gets one connection; write, resize and close share the other.
/// </summary>
public sealed class NamedPipeSshTerminalAgentClient : ISshTerminalAgentClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(35);
    private readonly Channel _readChannel;
    private readonly Channel _controlChannel;
    private bool _disposed;

    public NamedPipeSshTerminalAgentClient()
        : this(CreateTransport("Read"), CreateTransport("Control"))
    {
    }

    internal NamedPipeSshTerminalAgentClient(
        ISshTerminalIpcTransport readTransport,
        ISshTerminalIpcTransport controlTransport)
    {
        _readChannel = new Channel(readTransport ?? throw new ArgumentNullException(nameof(readTransport)));
        _controlChannel = new Channel(controlTransport ?? throw new ArgumentNullException(nameof(controlTransport)));
    }

    public Task<SshTerminalOpenResponse> OpenAsync(
        SshTerminalOpenRequest request,
        CancellationToken cancellationToken = default) => _controlChannel.ExecuteAsync<SshTerminalOpenRequest, SshTerminalOpenResponse>(
        SshTerminalIpcMessageTypes.OpenRequest,
        SshTerminalIpcMessageTypes.OpenResponse,
        request,
        cancellationToken);

    public Task<SshTerminalWriteResponse> WriteAsync(
        SshTerminalWriteRequest request,
        CancellationToken cancellationToken = default) => _controlChannel.ExecuteAsync<SshTerminalWriteRequest, SshTerminalWriteResponse>(
        SshTerminalIpcMessageTypes.WriteRequest,
        SshTerminalIpcMessageTypes.WriteResponse,
        request,
        cancellationToken);

    public Task<SshTerminalReadResponse> ReadAsync(
        SshTerminalReadRequest request,
        CancellationToken cancellationToken = default) => _readChannel.ExecuteAsync<SshTerminalReadRequest, SshTerminalReadResponse>(
        SshTerminalIpcMessageTypes.ReadRequest,
        SshTerminalIpcMessageTypes.ReadResponse,
        request,
        cancellationToken);

    public Task<SshTerminalResizeResponse> ResizeAsync(
        SshTerminalResizeRequest request,
        CancellationToken cancellationToken = default) => _controlChannel.ExecuteAsync<SshTerminalResizeRequest, SshTerminalResizeResponse>(
        SshTerminalIpcMessageTypes.ResizeRequest,
        SshTerminalIpcMessageTypes.ResizeResponse,
        request,
        cancellationToken);

    public Task<SshTerminalCloseResponse> CloseAsync(
        SshTerminalCloseRequest request,
        CancellationToken cancellationToken = default) => _controlChannel.ExecuteAsync<SshTerminalCloseRequest, SshTerminalCloseResponse>(
        SshTerminalIpcMessageTypes.CloseRequest,
        SshTerminalIpcMessageTypes.CloseResponse,
        request,
        cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _readChannel.DisposeAsync().ConfigureAwait(false);
        await _controlChannel.DisposeAsync().ConfigureAwait(false);
    }

    private static SshTerminalNamedPipeTransport CreateTransport(string role) =>
        new(DesktopAgentIpcOptions.CreateClient(new IpcClientOptions
        {
            Endpoint = AgentStatusMonitor.DefaultEndpoint,
            // As everywhere else: the access mode has to match the pipe the name resolved to, or a
            // service-hosted agent is unreachable and every terminal fails to open.
            TrustModel = DesktopAgentHost.TrustModel,
            ClientName = $"StorageHub.Desktop.SshTerminal.{role}",
            ClientVersion = DesktopApplicationVersion.Current,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            MaxConnectAttempts = 2,
            InitialReconnectDelay = TimeSpan.FromMilliseconds(100),
            MaximumReconnectDelay = TimeSpan.FromMilliseconds(250)
        }));

    /// <summary>One connection plus the gate that keeps its request/response pairs in step.</summary>
    private sealed class Channel(ISshTerminalIpcTransport transport) : IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private long _sendSequence;
        private bool _disposed;

        internal async Task<TResponse> ExecuteAsync<TRequest, TResponse>(
            string requestType,
            string responseType,
            TRequest request,
            CancellationToken cancellationToken)
            where TRequest : class
            where TResponse : class
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(request);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(RequestTimeout);
            await _gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                if (!transport.IsConnected)
                {
                    await transport.ConnectAsync(deadline.Token).ConfigureAwait(false);

                    // A reconnect resets the pipe's own sent-sequence, so the caller-owned counter
                    // has to start again with it or the two drift apart.
                    _sendSequence = 0;
                }

                var requestId = Guid.NewGuid();
                var sequence = checked(Interlocked.Increment(ref _sendSequence));
                await transport.SendAsync(
                    IpcEnvelope.Create(requestType, requestId, sequence, request),
                    deadline.Token).ConfigureAwait(false);
                var envelope = await transport.ReceiveAsync(deadline.Token).ConfigureAwait(false);
                if (envelope.RequestId != requestId ||
                    !string.Equals(envelope.MessageType, responseType, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The agent returned an unexpected SSH terminal response.");
                }

                return envelope.DeserializePayload<TResponse>();
            }
            catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException
                or InvalidDataException or InvalidOperationException or JsonException or OperationCanceledException)
            {
                // The connection is now of unknown state: drop it so the next call reconnects
                // cleanly rather than reading someone else's response off a half-consumed stream.
                await DisconnectAfterFailureAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                _ = _gate.Release();
            }
        }

        private async Task DisconnectAfterFailureAsync()
        {
            try
            {
                await transport.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await transport.DisposeAsync().ConfigureAwait(false);
            _gate.Dispose();
        }
    }

    private sealed class SshTerminalNamedPipeTransport(IpcClient client) : ISshTerminalIpcTransport
    {
        public bool IsConnected => client.IsConnected;

        public async Task ConnectAsync(CancellationToken cancellationToken = default) =>
            _ = await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

        public ValueTask SendAsync(IpcEnvelope envelope, CancellationToken cancellationToken = default) =>
            client.SendAsync(envelope, cancellationToken);

        public ValueTask<IpcEnvelope> ReceiveAsync(CancellationToken cancellationToken = default) =>
            client.ReceiveAsync(cancellationToken);

        public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
            client.DisconnectAsync(cancellationToken);

        public ValueTask DisposeAsync() => client.DisposeAsync();
    }
}
