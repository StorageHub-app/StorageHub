using System.Collections.Concurrent;
using System.Text.Json;
using StorageHub.Contracts.Ipc;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Ipc;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;
using StorageHub.Sync;

namespace StorageHub.Agent.Host;

public sealed partial class ObjectInspectorIpcCommandService
{
    private readonly ConcurrentDictionary<Guid, EditTransfer> _editTransfers = new();
    private readonly SemaphoreSlim _editSlots = new(8, 8);
    private readonly Timer _editCleanup;
    private int _editCleanupStarted;

    private async ValueTask<AgentIpcCommandResponse> HandleEditTransferAsync(IpcEnvelope envelope, CancellationToken token)
    {
        try
        {
            if (envelope.MessageType == EditableFileTransferMessages.Open)
                return EditResponse(await OpenEditTransferAsync(envelope.DeserializePayload<EditableFileTransferOpenRequest>(), token).ConfigureAwait(false));

            var chunk = envelope.MessageType == EditableFileTransferMessages.Chunk
                ? envelope.DeserializePayload<EditableFileTransferChunkRequest>() : null;
            var id = chunk?.SessionId ?? envelope.DeserializePayload<EditableFileTransferEndRequest>().SessionId;
            if (chunk is { HasValidBounds: false } || !_editTransfers.TryGetValue(id, out var transfer))
                return EditResponse(EditFailure(id, "The editing transfer is invalid or has expired."));

            await transfer.Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!_editTransfers.ContainsKey(id)) return EditResponse(EditFailure(id, "The editing transfer has closed."));
                transfer.TouchedUtc = DateTimeOffset.UtcNow;
                if (envelope.MessageType == EditableFileTransferMessages.Close)
                {
                    await RemoveEditTransferAsync(id, transfer).ConfigureAwait(false);
                    return EditResponse(new(id, Completed: true));
                }
                if (envelope.MessageType == EditableFileTransferMessages.Finish)
                {
                    if (transfer.Write is null || transfer.Offset != transfer.Length)
                        throw new InvalidDataException("The edited file upload is incomplete.");
                    var committed = await transfer.Write.CommitAsync(token).ConfigureAwait(false);
                    if (committed.IsFailure)
                    {
                        await RemoveEditTransferAsync(id, transfer).ConfigureAwait(false);
                        return EditResponse(new(id, Failure: SanitizeFailure(committed.Error)));
                    }
                    var entry = committed.Value;
                    var address = transfer.Address with
                    {
                        NativeItemId = entry.Address.NativeItemId,
                        VersionId = entry.Address.VersionId,
                        EntityTag = entry.Address.EntityTag ?? entry.ETag
                    };
                    if (!address.HasValidBounds) throw new InvalidDataException("The provider returned an invalid edited-file identity.");
                    await RemoveEditTransferAsync(id, transfer).ConfigureAwait(false);
                    return EditResponse(new(id, transfer.Offset, Completed: true, Address: address,
                        Size: entry.Size ?? transfer.Offset, LastModifiedUtc: entry.LastModifiedUtc));
                }
                if (chunk is null || chunk.Offset != transfer.Offset)
                    throw new InvalidDataException("The editing transfer offset does not match.");

                if (transfer.Write is not null)
                {
                    if (chunk.Content is not { Length: > 0 } content || content.Length > transfer.Length - transfer.Offset)
                        throw new InvalidDataException("The edited-file chunk exceeds its declared length.");
                    await transfer.Stream.WriteAsync(content, token).ConfigureAwait(false);
                    transfer.Offset += content.Length;
                    return EditResponse(new(id, transfer.Offset));
                }
                if (chunk.Content is not null) throw new InvalidDataException("A download cannot accept upload content.");
                var buffer = new byte[(int)Math.Min(EditableFileIpcContract.ChunkBytes, transfer.MaximumBytes - transfer.Offset + 1)];
                var read = await transfer.Stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (transfer.Offset + read > transfer.MaximumBytes)
                    throw new InvalidDataException("The file exceeds the external-editor size limit.");
                transfer.Offset += read;
                if (read == 0 && transfer.Length is { } length && length != transfer.Offset)
                    throw new InvalidDataException("The file length changed while downloading it for editing.");
                var response = new EditableFileTransferResponse(id, transfer.Offset, buffer[..read], read == 0,
                    transfer.Address, transfer.ContentType, transfer.Length);
                if (read == 0) await RemoveEditTransferAsync(id, transfer).ConfigureAwait(false);
                return EditResponse(response);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                await RemoveEditTransferAsync(id, transfer).ConfigureAwait(false);
                throw;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                await RemoveEditTransferAsync(id, transfer).ConfigureAwait(false);
                return EditResponse(EditFailure(id, error is InvalidDataException ? error.Message : "The editing transfer could not complete."));
            }
            finally { transfer.Gate.Release(); }
        }
        catch (JsonException) { return InvalidPayload(); }
    }

    private async Task<EditableFileTransferResponse> OpenEditTransferAsync(EditableFileTransferOpenRequest request, CancellationToken token)
    {
        if (!request.HasValidBounds) return EditFailure(Guid.Empty, "The editing transfer request is invalid.");
        if (Interlocked.Exchange(ref _editCleanupStarted, 1) == 0)
            _editCleanup.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        if (!await _editSlots.WaitAsync(0, token).ConfigureAwait(false))
            return EditFailure(Guid.Empty, "Too many editing transfers are open. Try again shortly.");
        ISyncEndpointConnection? connection = null;
        Stream? stream = null;
        IStorageWriteHandle? write = null;
        var retained = false;
        try
        {
            var opened = await _connector.OpenAsync(new ConnectionProfileId(request.Address.ConnectionId), token).ConfigureAwait(false);
            if (opened.IsFailure) return new(Guid.Empty, Failure: SanitizeFailure(opened.Error));
            connection = opened.Value;
            var validated = ValidateAndCreateAddress(connection.Session, request.Address);
            if (validated.Failure is not null) return new(Guid.Empty, Failure: validated.Failure);
            var address = request.Address;
            long? length = request.Length;
            var contentType = request.ContentType;
            if (request.Upload)
            {
                var result = await connection.Session.OpenWriteAsync(new StorageWriteRequest(validated.Address!,
                    StorageWriteMode.Overwrite, expectedLength: request.Length, contentType: request.ContentType,
                    expectedDestinationVersionId: address.VersionId, expectedDestinationEntityTag: address.EntityTag), token).ConfigureAwait(false);
                if (result.IsFailure) return new(Guid.Empty, Failure: SanitizeFailure(result.Error));
                write = result.Value;
                stream = write.Content;
            }
            else
            {
                var entry = await connection.Session.GetEntryAsync(validated.Address!, token).ConfigureAwait(false);
                if (entry.IsFailure) return new(Guid.Empty, Failure: SanitizeFailure(entry.Error));
                if (entry.Value.Kind != StorageEntryKind.File || entry.Value.Size > request.MaximumBytes)
                    return EditFailure(Guid.Empty, "The file exceeds the external-editor size limit or is not a regular file.");
                length = entry.Value.Size;
                contentType = entry.Value.ContentType;
                var result = await connection.Session.OpenReadAsync(new StorageReadRequest(validated.Address!,
                    ExpectedVersionId: address.VersionId, ExpectedEntityTag: address.EntityTag), token).ConfigureAwait(false);
                if (result.IsFailure) return new(Guid.Empty, Failure: SanitizeFailure(result.Error));
                stream = result.Value;
            }
            var id = Guid.NewGuid();
            _editTransfers[id] = new(connection, stream, write, address, request.MaximumBytes, length, contentType);
            retained = true;
            return new(id, Address: address, ContentType: contentType, Size: length);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { return EditFailure(Guid.Empty, "The editing transfer could not be opened."); }
        finally
        {
            if (!retained)
            {
                try
                {
                    if (write is not null) await write.DisposeAsync().ConfigureAwait(false);
                    else if (stream is not null) await stream.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    try { if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false); }
                    finally { _editSlots.Release(); }
                }
            }
        }
    }

    private async Task RemoveEditTransferAsync(Guid id, EditTransfer transfer)
    {
        if (!_editTransfers.TryRemove(id, out _)) return;
        try { await transfer.DisposeAsync().ConfigureAwait(false); }
        finally { _editSlots.Release(); }
    }

    private async Task PurgeEditTransfersAsync()
    {
        foreach (var (id, transfer) in _editTransfers)
        {
            if (!await transfer.Gate.WaitAsync(0).ConfigureAwait(false)) continue;
            try
            {
                if (transfer.TouchedUtc < DateTimeOffset.UtcNow.AddMinutes(-3))
                    await RemoveEditTransferAsync(id, transfer).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OutOfMemoryException) { /* The slot is released even if provider cleanup fails. */ }
            finally { transfer.Gate.Release(); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _editCleanup.DisposeAsync().ConfigureAwait(false);
        foreach (var (id, transfer) in _editTransfers)
        {
            await transfer.Gate.WaitAsync().ConfigureAwait(false);
            try { await RemoveEditTransferAsync(id, transfer).ConfigureAwait(false); }
            finally { transfer.Gate.Release(); }
        }
    }

    private static AgentIpcCommandResponse EditResponse(EditableFileTransferResponse response) =>
        AgentIpcCommandResponse.Create(EditableFileTransferMessages.Response, response);
    private static EditableFileTransferResponse EditFailure(Guid id, string message) => new(id, Failure:
        new StorageIpcFailure("storage.editor.transfer_failed", StorageIpcFailureCategory.Validation, message, false));

    private sealed class EditTransfer(ISyncEndpointConnection connection, Stream stream, IStorageWriteHandle? write,
        ObjectInspectorAddress address, int maximumBytes, long? length, string? contentType) : IAsyncDisposable
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal DateTimeOffset TouchedUtc { get; set; } = DateTimeOffset.UtcNow;
        internal Stream Stream => stream;
        internal IStorageWriteHandle? Write => write;
        internal ObjectInspectorAddress Address => address;
        internal int MaximumBytes => maximumBytes;
        internal long? Length => length;
        internal string? ContentType => contentType;
        internal long Offset { get; set; }
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (write is not null) await write.DisposeAsync().ConfigureAwait(false);
                else await stream.DisposeAsync().ConfigureAwait(false);
            }
            finally { await connection.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
