using System.Text.Json;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Ipc;
using StorageHub.Ipc.Windows;

namespace StorageHub.Desktop;

/// <summary>The agent's FTP command console: one command sent, one reply back.</summary>
public interface IFtpConsoleAgentClient : IAsyncDisposable
{
    Task<FtpConsoleExecuteResponse> ExecuteAsync(
        FtpConsoleExecuteRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Strict, correlated client for <see cref="FtpConsoleIpcContract"/>. The agent gives a command
/// <see cref="FtpConsoleIpcLimits.CommandTimeout"/> once the connection is open, and opening it can
/// take a login and a TLS handshake first, so the wait here is a minute and a half.
/// </summary>
public sealed class NamedPipeFtpConsoleAgentClient : IFtpConsoleAgentClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);
    private readonly IStorageIpcTransport _transport;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private long _sendSequence;
    private bool _disposed;

    public NamedPipeFtpConsoleAgentClient()
        : this(new Transport(DesktopAgentIpcOptions.CreateClient(DesktopAgentIpcOptions.Create(
            AgentStatusMonitor.DefaultEndpoint,
            "StorageHub.Desktop.FtpConsole",
            TimeSpan.FromSeconds(2)))))
    {
    }

    public NamedPipeFtpConsoleAgentClient(IStorageIpcTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<FtpConsoleExecuteResponse> ExecuteAsync(
        FtpConsoleExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || !request.HasValidBounds)
        {
            throw new ArgumentException(Ui.Validation.TheFtpConsoleRequestIsInvalid, nameof(request));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestTimeout);
        try
        {
            await _requestGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(Ui.Validation.TheFtpConsoleRequestTimedOut, error);
        }

        try
        {
            if (!_transport.IsConnected)
            {
                await _transport.ConnectAsync(deadline.Token).ConfigureAwait(false);
            }

            var requestId = Guid.NewGuid();
            var sequence = checked(Interlocked.Increment(ref _sendSequence));
            await _transport.SendAsync(
                IpcEnvelope.Create(FtpConsoleIpcMessageTypes.ExecuteRequest, requestId, sequence, request),
                deadline.Token).ConfigureAwait(false);
            var envelope = await _transport.ReceiveAsync(deadline.Token).ConfigureAwait(false);
            if (envelope is null || envelope.RequestId != requestId ||
                !string.Equals(envelope.MessageType, FtpConsoleIpcMessageTypes.ExecuteResponse, StringComparison.Ordinal))
            {
                throw new InvalidDataException(Ui.Validation.TheAgentReturnedAnInvalidFtpReply);
            }

            FtpConsoleExecuteResponse response;
            try
            {
                response = envelope.DeserializePayload<FtpConsoleExecuteResponse>();
            }
            catch (JsonException error)
            {
                throw new InvalidDataException(Ui.Validation.TheAgentReturnedAnInvalidFtpReply, error);
            }

            if (response is null ||
                response.ContractVersion != request.ContractVersion ||
                response.ConnectionId != request.ConnectionId ||
                !response.HasValidBounds)
            {
                throw new InvalidDataException(Ui.Validation.TheAgentReturnedAnInvalidFtpReply);
            }

            return response;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            await DisconnectAsync().ConfigureAwait(false);
            throw new TimeoutException(Ui.Validation.TheFtpConsoleRequestTimedOut, error);
        }
        catch (Exception)
        {
            // A failed exchange leaves the pipe in an unknown state; a late reply must never be
            // read as the answer to the next command.
            await DisconnectAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _requestGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
            _requestGate.Dispose();
        }
    }

    private async Task DisconnectAsync()
    {
        try
        {
            await _transport.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The failure that made the pipe unusable is the one worth reporting.
        }
    }

    private sealed class Transport(IpcClient client) : IStorageIpcTransport
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
