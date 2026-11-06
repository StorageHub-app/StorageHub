using System.Text.Json;
using StorageHub.Agent.Host;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Desktop;
using StorageHub.Domain.Capabilities;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Ipc;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;
using StorageHub.Sync;

namespace StorageHub.Agent.Windows.Tests;

public sealed class EditableFileTransferTests
{
    [Fact]
    public async Task OneHundredMiBRoundTripsInBoundedFramesWithConditionalUploadAndNewIdentity()
    {
        var session = new Session(EditableFileIpcContract.MaximumContentBytes);
        await using var service = new ObjectInspectorIpcCommandService(new Connector(session));
        var transport = new Transport(service);
        await using var client = new NamedPipeObjectInspectorAgentClient(transport);
        var address = session.Address;
        var downloaded = await client.DownloadEditableFileAsync(new(1, address, EditableFileIpcContract.MaximumContentBytes));
        Assert.Null(downloaded.Failure);
        Assert.Equal(100 * 1024 * 1024, downloaded.Content.Length);
        Assert.Equal(0x5a, downloaded.Content[0]);
        Assert.Equal(0x5a, downloaded.Content[^1]);
        var uploaded = await client.UploadEditedFileAsync(new(1, address, downloaded.Content, "text/plain"));
        Assert.Null(uploaded.Failure);
        Assert.Equal(downloaded.Content.LongLength, uploaded.Size);
        Assert.Equal("v2", uploaded.Address.VersionId);
        Assert.Equal("etag-2", uploaded.Address.EntityTag);
        Assert.Equal("v1", session.LastWrite!.ExpectedDestinationVersionId);
        Assert.Equal("etag-1", session.LastWrite.ExpectedDestinationEntityTag);
        Assert.True(session.Write.Committed);
        Assert.Equal(100 * 1024 * 1024, session.Write.Bytes);
        Assert.True(transport.Exchanges > 400);
        Assert.InRange(transport.LargestFrame, 1, 1024 * 1024);
        Assert.Equal(2, session.DisposedConnections);
    }

    [Fact]
    public async Task BadChunkOffsetAbortsTheUploadWithoutCommit()
    {
        var session = new Session(10);
        await using var service = new ObjectInspectorIpcCommandService(new Connector(session));
        var opened = await Exchange(service, EditableFileTransferMessages.Open,
            new EditableFileTransferOpenRequest(1, session.Address, 10, Upload: true, Length: 10));
        Assert.Null(opened.Failure);
        var response = await Exchange(service, EditableFileTransferMessages.Chunk,
            new EditableFileTransferChunkRequest(opened.SessionId, 5, [1, 2]));
        Assert.NotNull(response.Failure);
        Assert.False(session.Write.Committed);
        Assert.True(session.Write.Disposed);
        Assert.Equal(1, session.DisposedConnections);
    }

    [Fact]
    public async Task FinishingAnIncompleteUploadDoesNotCommit()
    {
        var session = new Session(10);
        await using var service = new ObjectInspectorIpcCommandService(new Connector(session));
        var opened = await Exchange(service, EditableFileTransferMessages.Open,
            new EditableFileTransferOpenRequest(1, session.Address, 10, Upload: true, Length: 10));
        var result = await Exchange(service, EditableFileTransferMessages.Finish, new EditableFileTransferEndRequest(opened.SessionId));
        Assert.NotNull(result.Failure);
        Assert.False(session.Write.Committed);
        Assert.True(session.Write.Disposed);
    }

    [Fact]
    public async Task UnknownLengthDownloadsStillEnforceTheLimitAndReleaseTheStream()
    {
        var session = new Session(11) { ReportLength = false };
        await using var service = new ObjectInspectorIpcCommandService(new Connector(session));
        var opened = await Exchange(service, EditableFileTransferMessages.Open,
            new EditableFileTransferOpenRequest(1, session.Address, 10));
        var result = await Exchange(service, EditableFileTransferMessages.Chunk,
            new EditableFileTransferChunkRequest(opened.SessionId, 0));
        Assert.NotNull(result.Failure);
        Assert.Equal(1, session.DisposedConnections);
    }

    [Fact]
    public async Task ExplicitCloseReleasesAnUnfinishedTransferAndRejectsFurtherChunks()
    {
        var session = new Session(10);
        await using var service = new ObjectInspectorIpcCommandService(new Connector(session));
        var opened = await Exchange(service, EditableFileTransferMessages.Open, new EditableFileTransferOpenRequest(1, session.Address, 10));
        var closed = await Exchange(service, EditableFileTransferMessages.Close, new EditableFileTransferEndRequest(opened.SessionId));
        Assert.True(closed.Completed);
        Assert.Equal(1, session.DisposedConnections);
        var result = await Exchange(service, EditableFileTransferMessages.Chunk, new EditableFileTransferChunkRequest(opened.SessionId, 0));
        Assert.NotNull(result.Failure);
    }

    private static async Task<EditableFileTransferResponse> Exchange<T>(ObjectInspectorIpcCommandService service, string message, T request)
    {
        var response = await service.HandleAsync(IpcEnvelope.Create(message, Guid.NewGuid(), 1, request));
        return response.Payload.Deserialize<EditableFileTransferResponse>()!;
    }

    private sealed class Transport(ObjectInspectorIpcCommandService service) : IStorageIpcTransport
    {
        private IpcEnvelope? _request;
        public bool IsConnected { get; private set; }
        internal int Exchanges { get; private set; }
        internal long LargestFrame { get; private set; }
        public Task ConnectAsync(CancellationToken cancellationToken = default) { IsConnected = true; return Task.CompletedTask; }
        public Task DisconnectAsync(CancellationToken cancellationToken = default) { IsConnected = false; return Task.CompletedTask; }
        public async ValueTask SendAsync(IpcEnvelope envelope, CancellationToken cancellationToken = default) =>
            _request = await Frame(envelope, cancellationToken);
        public async ValueTask<IpcEnvelope> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            Exchanges++;
            var response = await service.HandleAsync(_request!, cancellationToken);
            return await Frame(IpcEnvelope.Create(response.MessageType, _request!.RequestId,
                _request.Sequence, response.Payload), cancellationToken);
        }
        private async Task<IpcEnvelope> Frame(IpcEnvelope envelope, CancellationToken token)
        {
            using var stream = new MemoryStream();
            await LengthPrefixedJsonChannel.WriteAsync(stream, envelope, cancellationToken: token);
            LargestFrame = Math.Max(LargestFrame, stream.Length);
            stream.Position = 0;
            return await LengthPrefixedJsonChannel.ReadAsync<IpcEnvelope>(stream, cancellationToken: token);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Connector(Session session) : ISyncEndpointConnector
    {
        public ValueTask<StorageResult<ISyncEndpointConnection>> OpenAsync(ConnectionProfileId profileId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageResult<ISyncEndpointConnection>.Success(new Connection(session)));
    }
    private sealed class Connection(Session session) : ISyncEndpointConnection
    {
        public IStorageEndpointSession Session => session;
        public ValueTask DisposeAsync() { session.DisposedConnections++; return ValueTask.CompletedTask; }
    }
    private sealed class Session(int length) : IStorageEndpointSession
    {
        public ConnectionProfileId ProfileId { get; } = ConnectionProfileId.New();
        public string RootIdentity => "test-root";
        internal ObjectInspectorAddress Address => new(ProfileId.Value, RootIdentity, "file.txt", VersionId: "v1", EntityTag: "etag-1");
        internal bool ReportLength { get; init; } = true;
        internal StorageWriteRequest? LastWrite { get; private set; }
        internal WriteHandle Write { get; } = new();
        internal int DisposedConnections { get; set; }
        public EffectiveStorageCapabilities Capabilities { get; } = new([]);
        public ValueTask<StorageResult<StorageEntry>> GetEntryAsync(StorageAddress address, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageEntry.Create(address, StorageEntryKind.File, ReportLength ? length : null, contentType: "text/plain"));
        public ValueTask<StorageResult<Stream>> OpenReadAsync(StorageReadRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageResult<Stream>.Success(new PatternStream(length)));
        public ValueTask<StorageResult<IStorageWriteHandle>> OpenWriteAsync(StorageWriteRequest request, CancellationToken cancellationToken = default)
        {
            LastWrite = request;
            Write.Destination = StorageAddress.Create(ProfileId, RootIdentity, "file.txt", versionId: "v2", entityTag: "etag-2").Value;
            return ValueTask.FromResult(StorageResult<IStorageWriteHandle>.Success(Write));
        }
        public ValueTask<StorageResult> CheckHealthAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StorageResult<StoragePage>> ListAsync(StorageAddress address, StorageListRequest? request = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StorageResult<StorageEntry>> CreateDirectoryAsync(StorageAddress address, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StorageResult> DeleteAsync(StorageDeleteRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StorageResult<StorageEntry>> CopyAsync(StorageCopyRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<StorageResult<StorageEntry>> MoveAsync(StorageMoveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class WriteHandle : Stream, IStorageWriteHandle
    {
        public StorageAddress Destination { get; set; } = null!;
        public Stream Content => this;
        public long AcceptedOffset => 0;
        public string? ResumeToken => null;
        public StorageWriteHandleState State => Committed ? StorageWriteHandleState.Committed : StorageWriteHandleState.Open;
        internal long Bytes { get; private set; }
        internal bool Committed { get; private set; }
        internal bool Disposed { get; private set; }
        public ValueTask<StorageResult<StorageEntry>> CommitAsync(CancellationToken cancellationToken = default)
        { Committed = true; return ValueTask.FromResult(StorageEntry.Create(Destination, StorageEntryKind.File, Bytes)); }
        public ValueTask<StorageResult> AbortAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(StorageResult.Success());
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Assert.False(buffer.Span.ContainsAnyExcept((byte)0x5a));
            Bytes += buffer.Length;
            return ValueTask.CompletedTask;
        }
        public override ValueTask DisposeAsync() { Disposed = true; return base.DisposeAsync(); }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Bytes;
        public override long Position { get => Bytes; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class PatternStream(int length) : Stream
    {
        private int _read;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = Math.Min(buffer.Length, length - _read);
            buffer.Span[..count].Fill(0x5a);
            _read += count;
            return ValueTask.FromResult(count);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
