using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop;

public sealed partial class NamedPipeObjectInspectorAgentClient
{
    private Task<EditableFileTransferResponse> EditExchangeAsync<T>(string message, T request, CancellationToken token) where T : class =>
        ExecuteAsync<T, EditableFileTransferResponse>(message, EditableFileTransferMessages.Response, request, response =>
        {
            if (response.Offset is < 0 or > EditableFileIpcContract.MaximumContentBytes ||
                response.Content is { Length: > EditableFileIpcContract.ChunkBytes } ||
                response.Size is < 0 or > EditableFileIpcContract.MaximumContentBytes ||
                response.Address is { HasValidBounds: false } || !IsSafeContentType(response.ContentType) ||
                !IsValidFailure(response.Failure) || response.LastModifiedUtc is { Offset: var offset } && offset != TimeSpan.Zero)
                throw new InvalidDataException("The agent returned an invalid editing transfer response.");
        }, token);

    private async Task<EditableFileDownloadResponse> DownloadChunksAsync(EditableFileDownloadRequest request, CancellationToken token)
    {
        var opened = await EditExchangeAsync(EditableFileTransferMessages.Open,
            new EditableFileTransferOpenRequest(request.ContractVersion, request.Address, request.MaximumBytes), token).ConfigureAwait(false);
        if (opened.Failure is not null) return new(request.ContractVersion, request.Address, [], Failure: opened.Failure);
        var completed = false;
        try
        {
            if (opened.SessionId == Guid.Empty || opened.Address != request.Address || opened.Size > request.MaximumBytes)
                throw new InvalidDataException("The agent opened an invalid editing download.");
            using var content = new MemoryStream();
            long offset = 0;
            while (true)
            {
                var response = await EditExchangeAsync(EditableFileTransferMessages.Chunk,
                    new EditableFileTransferChunkRequest(opened.SessionId, offset), token).ConfigureAwait(false);
                if (response.Failure is not null) return new(request.ContractVersion, request.Address, [], Failure: response.Failure);
                if (response.SessionId != opened.SessionId || response.Content is null ||
                    response.Offset != offset + response.Content.Length || response.Offset > request.MaximumBytes ||
                    response.Content.Length == 0 && !response.Completed ||
                    response.Address != opened.Address || response.ContentType != opened.ContentType)
                    throw new InvalidDataException("The agent returned an invalid editing download chunk.");
                await content.WriteAsync(response.Content, token).ConfigureAwait(false);
                offset = response.Offset;
                if (!response.Completed) continue;
                if (opened.Size is { } length && length != offset)
                    throw new InvalidDataException("The downloaded file does not match its declared length.");
                completed = true;
                return new(request.ContractVersion, opened.Address, content.ToArray(), opened.ContentType);
            }
        }
        finally { if (!completed) await CloseEditTransferAsync(opened.SessionId).ConfigureAwait(false); }
    }

    private async Task<EditableFileUploadResponse> UploadChunksAsync(EditableFileUploadRequest request, CancellationToken token)
    {
        var opened = await EditExchangeAsync(EditableFileTransferMessages.Open,
            new EditableFileTransferOpenRequest(request.ContractVersion, request.Address, EditableFileIpcContract.MaximumContentBytes,
                Upload: true, Length: request.Content.LongLength, ContentType: request.ContentType), token).ConfigureAwait(false);
        if (opened.Failure is not null) return new(request.ContractVersion, request.Address, 0, null, opened.Failure);
        var completed = false;
        try
        {
            if (opened.SessionId == Guid.Empty || opened.Address != request.Address)
                throw new InvalidDataException("The agent opened an invalid editing upload.");
            for (var offset = 0; offset < request.Content.Length;)
            {
                var length = Math.Min(EditableFileIpcContract.ChunkBytes, request.Content.Length - offset);
                var response = await EditExchangeAsync(EditableFileTransferMessages.Chunk,
                    new EditableFileTransferChunkRequest(opened.SessionId, offset, request.Content.AsSpan(offset, length).ToArray()), token).ConfigureAwait(false);
                if (response.Failure is not null) return new(request.ContractVersion, request.Address, 0, null, response.Failure);
                if (response.SessionId != opened.SessionId || response.Offset != offset + length)
                    throw new InvalidDataException("The agent acknowledged an invalid editing upload offset.");
                offset += length;
            }
            var result = await EditExchangeAsync(EditableFileTransferMessages.Finish,
                new EditableFileTransferEndRequest(opened.SessionId), token).ConfigureAwait(false);
            if (result.Failure is not null) return new(request.ContractVersion, request.Address, 0, null, result.Failure);
            if (result.SessionId != opened.SessionId || !result.Completed || result.Address is null ||
                result.Address.ConnectionId != request.Address.ConnectionId || result.Address.RootIdentity != request.Address.RootIdentity ||
                result.Address.RelativePath != request.Address.RelativePath || result.Size != request.Content.LongLength)
                throw new InvalidDataException("The agent returned an invalid edited-file commit.");
            completed = true;
            return new(request.ContractVersion, result.Address, result.Size.Value, result.LastModifiedUtc);
        }
        finally { if (!completed) await CloseEditTransferAsync(opened.SessionId).ConfigureAwait(false); }
    }

    private async Task CloseEditTransferAsync(Guid id)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await EditExchangeAsync(EditableFileTransferMessages.Close, new EditableFileTransferEndRequest(id), timeout.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { /* A broken pipe is retired by ExecuteAsync; the agent also expires abandoned streams. */ }
    }
}
