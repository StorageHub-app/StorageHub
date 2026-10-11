using System.Text.Json;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Ipc;
using StorageHub.Ipc.Windows;

namespace StorageHub.Desktop;

/// <summary>The agent's leftover cleanup: look for leftover working files, remove the chosen ones.</summary>
public interface IStagingCleanupAgentClient : IAsyncDisposable
{
    Task<StagingCleanupScanResponse> ScanAsync(
        StagingCleanupScanRequest request,
        CancellationToken cancellationToken = default);

    Task<StagingCleanupRemoveResponse> RemoveAsync(
        StagingCleanupRemoveRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Strict, correlated client for <see cref="StagingCleanupIpcContract"/>. A scan opens saved
/// connections one after another, which the agent bounds at ninety seconds, so the wait here is
/// longer than that rather than the usual half minute.
/// </summary>
public sealed class NamedPipeStagingCleanupAgentClient : IStagingCleanupAgentClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(110);
    private readonly IStorageIpcTransport _transport;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private long _sendSequence;
    private bool _disposed;

    public NamedPipeStagingCleanupAgentClient()
        : this(new Transport(DesktopAgentIpcOptions.CreateClient(DesktopAgentIpcOptions.Create(
            AgentStatusMonitor.DefaultEndpoint,
            "StorageHub.Desktop.StagingCleanup",
            TimeSpan.FromSeconds(2)))))
    {
    }

    public NamedPipeStagingCleanupAgentClient(IStorageIpcTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public Task<StagingCleanupScanResponse> ScanAsync(
        StagingCleanupScanRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(request, request?.HasValidBounds == true);
        return ExecuteAsync<StagingCleanupScanRequest, StagingCleanupScanResponse>(
            StagingCleanupIpcMessageTypes.ScanRequest,
            StagingCleanupIpcMessageTypes.ScanResponse,
            request!,
            response => Ensure(response.ContractVersion == request!.ContractVersion && response.HasValidBounds),
            cancellationToken);
    }

    public Task<StagingCleanupRemoveResponse> RemoveAsync(
        StagingCleanupRemoveRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(request, request?.HasValidBounds == true);
        return ExecuteAsync<StagingCleanupRemoveRequest, StagingCleanupRemoveResponse>(
            StagingCleanupIpcMessageTypes.RemoveRequest,
            StagingCleanupIpcMessageTypes.RemoveResponse,
            request!,
            response => Ensure(
                response.ContractVersion == request!.ContractVersion &&
                response.HasValidBounds &&
                response.Results.Length == request.Items.Length &&
                response.Results.All(static result => IsValidFailure(result.Failure))),
            cancellationToken);
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

    private async Task<TResponse> ExecuteAsync<TRequest, TResponse>(
        string requestMessageType,
        string responseMessageType,
        TRequest request,
        Action<TResponse> validate,
        CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestTimeout);
        try
        {
            await _requestGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(Ui.Validation.TheLeftoverCleanupRequestTimedOut, error);
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
                IpcEnvelope.Create(requestMessageType, requestId, sequence, request),
                deadline.Token).ConfigureAwait(false);
            var envelope = await _transport.ReceiveAsync(deadline.Token).ConfigureAwait(false);
            if (envelope is null || envelope.RequestId != requestId ||
                !string.Equals(envelope.MessageType, responseMessageType, StringComparison.Ordinal))
            {
                throw new InvalidDataException(Ui.Validation.TheAgentReturnedAnInvalidLeftoverList);
            }

            TResponse response;
            try
            {
                response = envelope.DeserializePayload<TResponse>();
            }
            catch (JsonException error)
            {
                throw new InvalidDataException(Ui.Validation.TheAgentReturnedAnInvalidLeftoverList, error);
            }

            validate(response);
            return response;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            await DisconnectAsync().ConfigureAwait(false);
            throw new TimeoutException(Ui.Validation.TheLeftoverCleanupRequestTimedOut, error);
        }
        catch (Exception)
        {
            // A failed exchange leaves the pipe in an unknown state; a late reply must never be
            // read as the answer to the next request.
            await DisconnectAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _requestGate.Release();
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

    private static void Require<T>(T? request, bool validBounds)
        where T : class
    {
        if (request is null || !validBounds)
        {
            throw new ArgumentException(Ui.Validation.TheLeftoverCleanupRequestIsInvalid, nameof(request));
        }
    }

    private static void Ensure(bool valid)
    {
        if (!valid)
        {
            throw new InvalidDataException(Ui.Validation.TheAgentReturnedAnInvalidLeftoverList);
        }
    }

    private static bool IsValidFailure(StorageIpcFailure? failure) => failure is null ||
        !string.IsNullOrWhiteSpace(failure.Code) &&
        failure.Code.Length <= StorageIpcLimits.MaximumFailureCodeLength &&
        !string.IsNullOrWhiteSpace(failure.Message) &&
        failure.Message.Length <= StorageIpcLimits.MaximumFailureMessageLength &&
        !failure.Message.Any(char.IsControl) &&
        Enum.IsDefined(failure.Category);

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
