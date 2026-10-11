using System.Text.Json;
using StorageHub.Agent.Transfers;
using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;
using StorageHub.Domain.Capabilities;
using StorageHub.Domain.Identifiers;
using StorageHub.Domain.Storage;
using StorageHub.Ipc;
using StorageHub.Storage.Abstractions;
using StorageHub.Storage.Models;
using StorageHub.Sync;
using StorageHub.Transfers;

namespace StorageHub.Agent.Windows.Tests;

/// <summary>
/// Leftover working files: found in the folders the queue wrote to, shown with what is still in
/// use, and removed only when nothing is using them.
/// </summary>
public sealed class StagingCleanupIpcCommandServiceTests
{
    private static readonly Guid RemoteId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private const string RemoteRoot = "remote-root";

    [Fact]
    public async Task LeftoversBesideAFinishedTransferAreFoundAndRemoved()
    {
        using var folder = new Folder();
        var pending = Guid.NewGuid();
        folder.Write(".storagehub-resumekey.partial", 1234);
        folder.Write(".storagehub-write-0123.tmp", 0);
        folder.Write($".storagehub-{pending:N}.staging", 50);
        folder.Write("report.txt", 10);
        var store = new Jobs(
            Job(LocalDestination(folder.Path, "done.bin"), TransferState.Completed),
            Job(Remote("elsewhere/x.bin"), TransferState.Pending, pending));
        using var service = new StagingCleanupIpcCommandService(store, new Connector(new RemoteSession()), () => 0);

        var scan = await service.ScanAsync(includeRemote: false, CancellationToken.None);

        Assert.True(scan.HasValidBounds);
        Assert.Equal(1, scan.FoldersSearched);
        var byName = scan.Items.ToDictionary(static item => item.Name);
        Assert.Equal(3, byName.Count);
        Assert.Equal(StagingLeftoverKind.ResumePartial, byName[".storagehub-resumekey.partial"].Kind);
        Assert.Equal(1234, byName[".storagehub-resumekey.partial"].Size);
        Assert.Equal(StagingLeftoverKind.WriteProbe, byName[".storagehub-write-0123.tmp"].Kind);
        Assert.False(byName[".storagehub-resumekey.partial"].InUse);
        // Named after a transfer that has not run yet, wherever that transfer writes.
        Assert.True(byName[$".storagehub-{pending:N}.staging"].InUse);

        var removed = await service.RemoveAsync([.. scan.Items.Select(static item => item.Address)], CancellationToken.None);

        Assert.True(removed.HasValidBounds);
        Assert.Equal(2, removed.Results.Count(static result => result.Removed));
        Assert.Equal("cleanup.in_use", removed.Results.Single(static result => !result.Removed).Failure!.Code);
        Assert.False(File.Exists(Path.Combine(folder.Path, ".storagehub-resumekey.partial")));
        Assert.True(File.Exists(Path.Combine(folder.Path, $".storagehub-{pending:N}.staging")));
        Assert.True(File.Exists(Path.Combine(folder.Path, "report.txt")));
    }

    [Theory]
    [InlineData(TransferState.Paused, 0)]
    [InlineData(TransferState.Completed, 1)]
    public async Task NothingIsRemovedWhileATransferOrASyncMayStillUseIt(TransferState state, int runningSyncs)
    {
        using var folder = new Folder();
        folder.Write(".storagehub-resumekey.partial", 10);
        var store = new Jobs(Job(LocalDestination(folder.Path, "a.bin"), state));
        using var service = new StagingCleanupIpcCommandService(store, new Connector(new RemoteSession()), () => runningSyncs);

        var scan = await service.ScanAsync(includeRemote: true, CancellationToken.None);
        var removed = await service.RemoveAsync([scan.Items.Single().Address], CancellationToken.None);

        Assert.True(scan.Items.Single().InUse);
        Assert.Equal(runningSyncs > 0, scan.SyncRunning);
        Assert.Equal("cleanup.in_use", removed.Results.Single().Failure!.Code);
        Assert.True(File.Exists(Path.Combine(folder.Path, ".storagehub-resumekey.partial")));
    }

    /// <summary>A partial a write holds this very moment is left, even though its transfer looks finished.</summary>
    [Fact]
    public async Task APartialBeingWrittenIsLeft()
    {
        using var folder = new Folder();
        folder.Write(".storagehub-resumekey.partial", 10);
        var path = Path.Combine(folder.Path, ".storagehub-resumekey.partial");
        var store = new Jobs(Job(LocalDestination(folder.Path, "a.bin"), TransferState.Failed));
        using var service = new StagingCleanupIpcCommandService(store, new Connector(new RemoteSession()), () => 0);
        Assert.True(LocalStagedFileWriteHandle.TryClaim(path));
        try
        {
            var removed = await service.RemoveAsync(
                [new TransferQueueAddress(
                    LocalTransferFolder.CreateConnectionId(folder.Path),
                    LocalTransferFolder.CreateRootIdentity(folder.Path),
                    ".storagehub-resumekey.partial")],
                CancellationToken.None);

            Assert.Equal("cleanup.in_use", removed.Results.Single().Failure!.Code);
            Assert.True(File.Exists(path));
        }
        finally
        {
            LocalStagedFileWriteHandle.ReleaseClaim(path);
        }
    }

    /// <summary>Only StorageHub's own names, and only in folders the queue wrote to, are ever removed.</summary>
    [Fact]
    public async Task AnythingElseIsRefused()
    {
        using var known = new Folder();
        using var unknown = new Folder();
        known.Write("report.txt", 1);
        unknown.Write(".storagehub-resumekey.partial", 1);
        var store = new Jobs(Job(LocalDestination(known.Path, "a.bin"), TransferState.Completed));
        using var service = new StagingCleanupIpcCommandService(store, new Connector(new RemoteSession()), () => 0);

        var removed = await service.RemoveAsync(
            [
                new TransferQueueAddress(
                    LocalTransferFolder.CreateConnectionId(known.Path), LocalTransferFolder.CreateRootIdentity(known.Path), "report.txt"),
                new TransferQueueAddress(
                    LocalTransferFolder.CreateConnectionId(unknown.Path), LocalTransferFolder.CreateRootIdentity(unknown.Path),
                    ".storagehub-resumekey.partial")
            ],
            CancellationToken.None);

        Assert.All(removed.Results, static result => Assert.Equal("cleanup.not_leftover", result.Failure!.Code));
        Assert.True(File.Exists(Path.Combine(known.Path, "report.txt")));
        Assert.True(File.Exists(Path.Combine(unknown.Path, ".storagehub-resumekey.partial")));
    }

    [Fact]
    public async Task ALeftoverOnASavedConnectionIsFoundAndRemovedThroughIt()
    {
        var session = new RemoteSession();
        session.Files["uploads/.storagehub-abcdef.partial"] = 4096;
        session.Files["uploads/photo.jpg"] = 10;
        var store = new Jobs(Job(Remote("uploads/photo.jpg"), TransferState.Completed));
        using var service = new StagingCleanupIpcCommandService(store, new Connector(session), () => 0);

        var scan = await service.ScanAsync(includeRemote: true, CancellationToken.None);
        var leftover = Assert.Single(scan.Items);
        Assert.Equal("/uploads", leftover.Folder);
        Assert.Equal("uploads/.storagehub-abcdef.partial", leftover.Address.RelativePath);

        var removed = await service.RemoveAsync([leftover.Address], CancellationToken.None);

        Assert.True(Assert.Single(removed.Results).Removed);
        Assert.Equal(4096, removed.Results[0].Size);
        Assert.Equal(["uploads/photo.jpg"], session.Files.Keys);
    }

    [Fact]
    public async Task AnUnboundedRequestIsRejected()
    {
        using var service = new StagingCleanupIpcCommandService(new Jobs(), new Connector(new RemoteSession()), () => 0);
        var response = await service.HandleAsync(IpcEnvelope.Create(
            StagingCleanupIpcMessageTypes.RemoveRequest, Guid.NewGuid(), 1,
            new StagingCleanupRemoveRequest(StagingCleanupIpcContract.CurrentVersion, [])));
        var scanned = await service.HandleAsync(IpcEnvelope.Create(
            StagingCleanupIpcMessageTypes.ScanRequest, Guid.NewGuid(), 1,
            new StagingCleanupScanRequest(StagingCleanupIpcContract.CurrentVersion)));

        Assert.Equal(IpcProtocol.ErrorResponseMessageType, response.MessageType);
        Assert.Equal(StagingCleanupIpcMessageTypes.ScanResponse, scanned.MessageType);
        Assert.Empty(scanned.Payload.Deserialize<StagingCleanupScanResponse>()!.Items);
    }

    private static StorageAddress LocalDestination(string folder, string name) => StorageAddress.Create(
        new ConnectionProfileId(LocalTransferFolder.CreateConnectionId(folder)),
        LocalTransferFolder.CreateRootIdentity(folder),
        name).Value;

    private static StorageAddress Remote(string path) =>
        StorageAddress.Create(new ConnectionProfileId(RemoteId), RemoteRoot, path).Value;

    private static DurableTransferJob Job(StorageAddress destination, TransferState state, Guid? id = null)
    {
        var jobId = new TransferJobId(id ?? Guid.NewGuid());
        var source = StorageAddress.Create(new ConnectionProfileId(Guid.NewGuid()), "source-root", "source.bin").Value;
        var intent = new TransferIntent(
            jobId, TransferOperationKind.Copy, source, destination, 1, TransferVerificationPolicy.Size, DateTimeOffset.UtcNow);
        var snapshot = new TransferStateSnapshot(
            jobId, state, 1, 1, DateTimeOffset.UtcNow,
            state == TransferState.Failed ? TransferStatusCode.ProviderFailure : TransferStatusCode.None);
        return new DurableTransferJob(intent, snapshot, 0, null, null, null);
    }

    private sealed class Jobs(params DurableTransferJob[] jobs) : ITransferQueueQueryStore
    {
        public ValueTask<TransferQueuePage> ListAsync(TransferQueueQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new TransferQueuePage(jobs, null));
    }

    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"storagehub-cleanup-{Guid.NewGuid():N}");

        public Folder() => Directory.CreateDirectory(Path);

        public void Write(string name, int length) =>
            File.WriteAllBytes(System.IO.Path.Combine(Path, name), new byte[length]);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class Connector(RemoteSession session) : ISyncEndpointConnector
    {
        public ValueTask<StorageResult<ISyncEndpointConnection>> OpenAsync(
            ConnectionProfileId profileId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageResult<ISyncEndpointConnection>.Success(new Connection(session)));
    }

    private sealed class Connection(IStorageEndpointSession session) : ISyncEndpointConnection
    {
        public IStorageEndpointSession Session { get; } = session;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>A saved connection holding files by path, listed and removed as a provider would.</summary>
    private sealed class RemoteSession : IStorageEndpointSession
    {
        public Dictionary<string, long> Files { get; } = new(StringComparer.Ordinal);

        public ConnectionProfileId ProfileId { get; } = new(RemoteId);

        public string RootIdentity => RemoteRoot;

        public EffectiveStorageCapabilities Capabilities { get; } = new(
            [new KeyValuePair<StorageFeature, FeatureSupport>(StorageFeature.List, FeatureSupport.Native())],
            StorageCaseSensitivity.Sensitive);

        public ValueTask<StorageResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageResult.Success());

        public ValueTask<StorageResult<StoragePage>> ListAsync(
            StorageAddress address,
            StorageListRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            var prefix = address.CanonicalRelativePath.Length == 0 ? string.Empty : address.CanonicalRelativePath + "/";
            var entries = Files
                .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal) && !pair.Key[prefix.Length..].Contains('/'))
                .Select(pair => Entry(pair.Key, pair.Value))
                .ToArray();
            return ValueTask.FromResult(StorageResult<StoragePage>.Success(new StoragePage(entries)));
        }

        public ValueTask<StorageResult<StorageEntry>> GetEntryAsync(StorageAddress address, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Files.TryGetValue(address.CanonicalRelativePath, out var size)
                ? StorageResult<StorageEntry>.Success(Entry(address.CanonicalRelativePath, size))
                : StorageResult<StorageEntry>.Fail(new StorageFailure("missing", StorageFailureKind.NotFound, "Missing.")));

        public ValueTask<StorageResult> DeleteAsync(StorageDeleteRequest request, CancellationToken cancellationToken = default)
        {
            Files.Remove(request.Address.CanonicalRelativePath);
            return ValueTask.FromResult(StorageResult.Success());
        }

        public ValueTask<StorageResult<Stream>> OpenReadAsync(StorageReadRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<StorageResult<IStorageWriteHandle>> OpenWriteAsync(StorageWriteRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<StorageResult<StorageEntry>> CreateDirectoryAsync(StorageAddress address, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<StorageResult<StorageEntry>> CopyAsync(StorageCopyRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<StorageResult<StorageEntry>> MoveAsync(StorageMoveRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private StorageEntry Entry(string path, long size) => StorageEntry.Create(
            StorageAddress.Create(ProfileId, RemoteRoot, path).Value,
            StorageEntryKind.File,
            size,
            DateTimeOffset.UnixEpoch).Value;
    }
}
