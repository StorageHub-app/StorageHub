using System.Text.Json;
using StorageHub.Agent.Host;
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

namespace StorageHub.Agent.Windows.Tests;

/// <summary>
/// Compare folders in the agent: started, polled, read a page at a time, and released, with a
/// folder on this PC on either side.
/// </summary>
public sealed class FolderCompareIpcCommandServiceTests
{
    private static readonly DateTime Noon = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TwoFoldersOnThisPcAreComparedAndReadInPages()
    {
        using var left = new Folder();
        using var right = new Folder();
        left.Write("same.txt", "hello", Noon);
        right.Write("same.txt", "hello", Noon);
        left.Write("docs/only-left.txt", "a", Noon);
        right.Write("only-right.txt", "b", Noon);
        left.Write("changed.txt", "longer text", Noon);
        right.Write("changed.txt", "short", Noon);
        left.Write(".storagehub-0123.partial", "half", Noon);
        await using var service = new FolderCompareIpcCommandService(new NoConnector());

        var id = await StartAsync(service, Local(left.Path), Local(right.Path));
        var first = await WaitAsync(service, id, pageSize: 2);
        var second = await StatusAsync(service, id, offset: 2, pageSize: 10);

        Assert.Equal(FolderCompareState.Completed, first.State);
        Assert.Equal(5, first.TotalEntries);
        var entries = first.Entries.Concat(second.Entries).ToDictionary(static entry => entry.RelativePath);
        Assert.Equal(
            ["changed.txt", "docs", "docs/only-left.txt", "only-right.txt", "same.txt"],
            entries.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(FolderCompareKind.Different, entries["changed.txt"].Kind);
        Assert.Equal(FolderCompareReasons.Size, entries["changed.txt"].Reasons);
        Assert.Equal(FolderCompareKind.OnlyLeft, entries["docs/only-left.txt"].Kind);
        Assert.Equal(FolderCompareKind.OnlyRight, entries["only-right.txt"].Kind);
        Assert.Equal(FolderCompareKind.Same, entries["same.txt"].Kind);

        // A file on this PC carries the identity a transfer holds it to, so a copy over it is safe.
        Assert.Equal(
            LocalTransferFolder.FileIdentity(Noon, 5),
            entries["same.txt"].Right!.EntityTag);
    }

    [Fact]
    public async Task ContentsAreComparedBySha256WhenAsked()
    {
        using var left = new Folder();
        using var right = new Folder();
        left.Write("a.txt", "abcd", Noon);
        right.Write("a.txt", "abce", Noon);
        await using var service = new FolderCompareIpcCommandService(new NoConnector());

        var id = await StartAsync(service, Local(left.Path), Local(right.Path), compareContents: true);
        var status = await WaitAsync(service, id);

        var entry = Assert.Single(status.Entries);
        Assert.Equal(FolderCompareKind.Different, entry.Kind);
        Assert.Equal(FolderCompareReasons.Checksum, entry.Reasons);
        Assert.Equal(64, entry.Left!.Sha256!.Length);
        Assert.NotEqual(entry.Left.Sha256, entry.Right!.Sha256);
    }

    /// <summary>
    /// A connection that now opens somewhere other than the folder the pane showed is refused, and
    /// the connection it opened is let go.
    /// </summary>
    [Fact]
    public async Task ASavedConnectionThatMovedIsRefused()
    {
        using var right = new Folder();
        var profileId = ConnectionProfileId.New();
        var connection = new Connection(new EmptySession(profileId, "opened-root"));
        await using var service = new FolderCompareIpcCommandService(new OneConnector(connection));

        var id = await StartAsync(
            service,
            new TransferQueueAddress(profileId.Value, "stale-root", "photos"),
            Local(right.Path));
        var status = await WaitAsync(service, id);

        Assert.Equal(FolderCompareState.Failed, status.State);
        Assert.Equal("compare.root_changed", status.Failure!.Code);
        Assert.True(connection.IsDisposed);
    }

    [Fact]
    public async Task ASavedConnectionIsComparedWithAFolderOnThisPc()
    {
        using var right = new Folder();
        right.Write("readme.md", "x", Noon);
        var profileId = ConnectionProfileId.New();
        var connection = new Connection(new EmptySession(profileId, "s3:bucket"));
        await using var service = new FolderCompareIpcCommandService(new OneConnector(connection));

        var id = await StartAsync(service, new TransferQueueAddress(profileId.Value, "s3:bucket", ""), Local(right.Path));
        var status = await WaitAsync(service, id);

        Assert.Equal(FolderCompareState.Completed, status.State);
        Assert.Equal(FolderCompareKind.OnlyRight, Assert.Single(status.Entries).Kind);
        Assert.True(connection.IsDisposed);
    }

    [Fact]
    public async Task AReleasedComparisonIsGone()
    {
        using var left = new Folder();
        using var right = new Folder();
        await using var service = new FolderCompareIpcCommandService(new NoConnector());
        var id = await StartAsync(service, Local(left.Path), Local(right.Path));
        await WaitAsync(service, id);

        var released = await service.HandleAsync(IpcEnvelope.Create(
            FolderCompareIpcMessageTypes.ReleaseRequest, Guid.NewGuid(), 1,
            new FolderCompareReleaseRequest(FolderCompareIpcContract.CurrentVersion, id)));
        var status = await StatusAsync(service, id);

        Assert.True(released.Payload.Deserialize<FolderCompareReleaseResponse>()!.Released);
        Assert.Equal("compare.not_found", status.Failure!.Code);
    }

    [Fact]
    public async Task AFolderThatDoesNotExistIsRefusedByTheLocalGate()
    {
        using var right = new Folder();
        var missing = Path.Combine(right.Path, "gone");
        await using var service = new FolderCompareIpcCommandService(new NoConnector());

        var id = await StartAsync(service, Local(missing), Local(right.Path));
        var status = await WaitAsync(service, id);

        Assert.Equal("local-user.not-found", status.Failure!.Code);
    }

    [Fact]
    public async Task AnUnboundedRequestIsRejected()
    {
        await using var service = new FolderCompareIpcCommandService(new NoConnector());
        var response = await service.HandleAsync(IpcEnvelope.Create(
            FolderCompareIpcMessageTypes.StatusRequest, Guid.NewGuid(), 1,
            new FolderCompareStatusRequest(FolderCompareIpcContract.CurrentVersion, Guid.NewGuid(), PageSize: 5_000)));

        Assert.Equal(IpcProtocol.ErrorResponseMessageType, response.MessageType);
    }

    /// <summary>
    /// An entry's paths are relative, slash-separated and free of dot names, its sides agree with its
    /// kind, and the right's spelling may differ from the left's only in case.
    /// </summary>
    [Theory]
    [InlineData("a/b.txt", null, true)]
    [InlineData("Docs/a.txt", "docs/a.txt", true)]
    [InlineData("Docs/a.txt", "other/a.txt", false)]
    [InlineData("a/../b.txt", null, false)]
    [InlineData("/a.txt", null, false)]
    [InlineData("a\\b.txt", null, false)]
    [InlineData("a//b.txt", null, false)]
    public void AnEntrysPathsAreBounded(string path, string? rightPath, bool valid)
    {
        var item = new FolderCompareItem(StorageItemKind.File, 1, Noon);
        Assert.Equal(
            valid,
            new FolderCompareEntry(path, FolderCompareKind.Same, FolderCompareReasons.None, item, item, rightPath).HasValidBounds);
    }

    [Fact]
    public void AnEntrysSidesMustMatchItsKind()
    {
        var item = new FolderCompareItem(StorageItemKind.File, 1, Noon);
        Assert.False(new FolderCompareEntry("a", FolderCompareKind.OnlyLeft, FolderCompareReasons.None, item, item).HasValidBounds);
        Assert.False(new FolderCompareEntry("a", FolderCompareKind.Different, FolderCompareReasons.None, item, item).HasValidBounds);
        Assert.False(new FolderCompareEntry("a", FolderCompareKind.Same, FolderCompareReasons.Size, item, item).HasValidBounds);
        Assert.False(new FolderCompareEntry("a", FolderCompareKind.Different, (FolderCompareReasons)64, item, item).HasValidBounds);
        Assert.False(new FolderCompareItem(StorageItemKind.File, 1, Noon, Sha256: "abc").HasValidBounds);
        Assert.True(new FolderCompareEntry("a", FolderCompareKind.OnlyRight, FolderCompareReasons.None, null, item).HasValidBounds);
    }

    private static TransferQueueAddress Local(string folder) => new(
        LocalTransferFolder.CreateConnectionId(folder),
        LocalTransferFolder.CreateRootIdentity(folder),
        string.Empty);

    private static async Task<Guid> StartAsync(
        FolderCompareIpcCommandService service,
        TransferQueueAddress left,
        TransferQueueAddress right,
        bool compareContents = false)
    {
        var response = await service.HandleAsync(IpcEnvelope.Create(
            FolderCompareIpcMessageTypes.StartRequest, Guid.NewGuid(), 1,
            new FolderCompareStartRequest(FolderCompareIpcContract.CurrentVersion, left, right, compareContents)));
        var started = response.Payload.Deserialize<FolderCompareStartResponse>()!;
        Assert.Null(started.Failure);
        return started.CompareId;
    }

    private static async Task<FolderCompareStatusResponse> WaitAsync(
        FolderCompareIpcCommandService service,
        Guid id,
        int pageSize = FolderCompareIpcLimits.MaximumPageSize)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var status = await StatusAsync(service, id, 0, pageSize);
            if (status.State != FolderCompareState.Running || DateTime.UtcNow > deadline)
            {
                Assert.True(status.HasValidBounds);
                return status;
            }

            await Task.Delay(20);
        }
    }

    private static async Task<FolderCompareStatusResponse> StatusAsync(
        FolderCompareIpcCommandService service,
        Guid id,
        int offset = 0,
        int pageSize = FolderCompareIpcLimits.MaximumPageSize)
    {
        var response = await service.HandleAsync(IpcEnvelope.Create(
            FolderCompareIpcMessageTypes.StatusRequest, Guid.NewGuid(), 1,
            new FolderCompareStatusRequest(FolderCompareIpcContract.CurrentVersion, id, offset, pageSize)));
        return response.Payload.Deserialize<FolderCompareStatusResponse>()!;
    }

    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"storagehub-compare-{Guid.NewGuid():N}");

        public Folder() => Directory.CreateDirectory(Path);

        public void Write(string relative, string content, DateTime modifiedUtc)
        {
            var path = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, modifiedUtc);
        }

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

    private sealed class NoConnector : ISyncEndpointConnector
    {
        public ValueTask<StorageResult<ISyncEndpointConnection>> OpenAsync(
            ConnectionProfileId profileId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No saved connection is involved.");
    }

    private sealed class OneConnector(Connection connection) : ISyncEndpointConnector
    {
        public ValueTask<StorageResult<ISyncEndpointConnection>> OpenAsync(
            ConnectionProfileId profileId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageResult<ISyncEndpointConnection>.Success(connection));
    }

    private sealed class Connection(IStorageEndpointSession session) : ISyncEndpointConnection
    {
        public IStorageEndpointSession Session { get; } = session;

        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A connection whose folder is empty.</summary>
    private sealed class EmptySession(ConnectionProfileId profileId, string rootIdentity) : IStorageEndpointSession
    {
        public ConnectionProfileId ProfileId { get; } = profileId;

        public string RootIdentity { get; } = rootIdentity;

        public EffectiveStorageCapabilities Capabilities { get; } = new(
            [new KeyValuePair<StorageFeature, FeatureSupport>(StorageFeature.List, FeatureSupport.Native())],
            StorageCaseSensitivity.Sensitive);

        public ValueTask<StorageResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageResult.Success());

        public ValueTask<StorageResult<StoragePage>> ListAsync(
            StorageAddress address,
            StorageListRequest? request = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(StorageResult<StoragePage>.Success(new StoragePage([])));

        public ValueTask<StorageResult<StorageEntry>> GetEntryAsync(StorageAddress address, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<StorageResult<Stream>> OpenReadAsync(StorageReadRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<StorageResult<IStorageWriteHandle>> OpenWriteAsync(StorageWriteRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<StorageResult<StorageEntry>> CreateDirectoryAsync(StorageAddress address, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<StorageResult> DeleteAsync(StorageDeleteRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<StorageResult<StorageEntry>> CopyAsync(StorageCopyRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<StorageResult<StorageEntry>> MoveAsync(StorageMoveRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
