using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Tests;

public sealed class RecursiveTransferControllerTests
{
    [Fact]
    public async Task Nested_folder_copy_preserves_files_and_empty_directories()
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var sourceRoot = "source-root";
        var destinationRoot = "destination-root";
        var storage = new FakeStorageClient(sourceId, destinationId, sourceRoot, destinationRoot);
        var mutations = new FakeMutationClient();
        var queue = new FakeTransferClient();
        await using var transfers = new ManualTransferController(queue);
        await using var controller = new RecursiveTransferController(
            transfers, storage, mutations);
        var sourceContext = PaneTransferContext.Create(
            PaneTransferContextKind.SavedConnection, sourceId, sourceRoot, "source").Value;
        var folder = PaneTransferItem.Create(
            "folder", "source/folder", StorageItemKind.Directory, length: null).Value;
        var selection = PaneSelectionSnapshot.Create(sourceContext, [folder]).Value;
        var destinationContext = PaneTransferContext.Create(
            PaneTransferContextKind.SavedConnection,
            destinationId,
            destinationRoot,
            "destination").Value;
        var destination = PaneDestinationSnapshot.Create(destinationContext, []).Value;

        var result = await controller.EnqueueAsync(
            selection,
            destination,
            TransferQueueOperation.Copy,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal(
            ["destination/folder", "destination/folder/empty", "destination/folder/nested"],
            mutations.EnsuredPaths);
        Assert.Equal(2, queue.Requests.Count);
        Assert.Contains(queue.Requests, request =>
            request.Source.RelativePath == "source/folder/root.txt" &&
            request.Destination.RelativePath == "destination/folder/root.txt");
        Assert.Contains(queue.Requests, request =>
            request.Source.RelativePath == "source/folder/nested/child.bin" &&
            request.Destination.RelativePath == "destination/folder/nested/child.bin");
    }

    [Fact]
    public async Task A_repeated_page_token_stops_the_walk_and_keeps_what_it_already_queued()
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var storage = new FakeStorageClient(
            sourceId,
            destinationId,
            "source-root",
            "destination-root",
            repeatSourcePageToken: true);
        var mutations = new FakeMutationClient();
        var queue = new FakeTransferClient();
        await using var transfers = new ManualTransferController(queue);
        await using var controller = new RecursiveTransferController(transfers, storage, mutations);

        var result = await controller.EnqueueAsync(
            CreateFolderSelection(sourceId, "source-root"),
            CreateDestination(destinationId, "destination-root"),
            TransferQueueOperation.Copy,
            CancellationToken.None);

        Assert.Equal("manual_transfer.repeated_page_token", result.Failure?.Code);
        Assert.Equal(2, storage.SourceListCount);

        // The walk queues each page as it reads it, so the first page's two files are already
        // durable when the bad token arrives on the second. They stay: this is the partial drop
        // that streaming trades the all-or-nothing manifest for.
        Assert.True(result.IsPartial);
        Assert.Equal(2, queue.Requests.Count);
        Assert.NotEmpty(mutations.EnsuredPaths);
    }

    [Fact]
    public async Task A_file_and_folder_sharing_one_path_stops_the_walk_before_either_is_queued()
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var storage = new FakeStorageClient(
            sourceId,
            destinationId,
            "source-root",
            "destination-root",
            sourceKindCollision: true);
        var mutations = new FakeMutationClient();
        var queue = new FakeTransferClient();
        await using var transfers = new ManualTransferController(queue);
        await using var controller = new RecursiveTransferController(transfers, storage, mutations);

        var result = await controller.EnqueueAsync(
            CreateFolderSelection(sourceId, "source-root"),
            CreateDestination(destinationId, "destination-root"),
            TransferQueueOperation.Copy,
            CancellationToken.None);

        Assert.Equal("manual_transfer.source_kind_collision", result.Failure?.Code);

        // Nothing is queued: the collision is met on the entry that follows the folder, before the
        // page is flushed. The folder itself was created as it was met, which is what lets the
        // files inside it be queued before the walk has seen the rest of the tree.
        Assert.Empty(queue.Requests);
        Assert.False(result.IsPartial);
        Assert.NotEmpty(mutations.EnsuredPaths);
    }

    [Fact]
    public async Task The_walk_reports_its_running_counts_so_the_queue_can_show_them()
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var storage = new FakeStorageClient(sourceId, destinationId, "source-root", "destination-root");
        var mutations = new FakeMutationClient();
        var queue = new FakeTransferClient();
        await using var transfers = new ManualTransferController(queue);
        await using var controller = new RecursiveTransferController(transfers, storage, mutations);
        var reported = new List<(int Files, int Folders)>();

        var result = await controller.EnqueueAsync(
            CreateFolderSelection(sourceId, "source-root"),
            CreateDestination(destinationId, "destination-root"),
            TransferQueueOperation.Copy,
            CancellationToken.None,
            (files, folders) => reported.Add((files, folders)));

        Assert.Null(result.Failure);
        Assert.NotEmpty(reported);

        // The last report is what the row ends on, and it has to match what was actually queued.
        Assert.Equal(queue.Requests.Count, reported[^1].Files);
        Assert.Equal(mutations.EnsuredPaths.Count, reported[^1].Folders);
    }

    private static PaneSelectionSnapshot CreateFolderSelection(Guid connectionId, string rootIdentity)
    {
        var context = PaneTransferContext.Create(
            PaneTransferContextKind.SavedConnection,
            connectionId,
            rootIdentity,
            "source").Value;
        var folder = PaneTransferItem.Create(
            "folder",
            "source/folder",
            StorageItemKind.Directory,
            length: null).Value;
        return PaneSelectionSnapshot.Create(context, [folder]).Value;
    }

    private static PaneDestinationSnapshot CreateDestination(Guid connectionId, string rootIdentity) =>
        PaneDestinationSnapshot.Create(
            PaneTransferContext.Create(
                PaneTransferContextKind.SavedConnection,
                connectionId,
                rootIdentity,
                "destination").Value,
            []).Value;

    private sealed class FakeStorageClient(
        Guid sourceId,
        Guid destinationId,
        string sourceRoot,
        string destinationRoot,
        bool repeatSourcePageToken = false,
        bool sourceKindCollision = false) : IRemoteStorageAgentClient
    {
        public int SourceListCount { get; private set; }

        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.ConnectionId == sourceId && request.RelativePath == "source/folder")
            {
                SourceListCount++;
                var entries = sourceKindCollision
                    ? new[]
                    {
                        Item("same", "source/folder/same", StorageItemKind.Directory),
                        Item("same", "source/folder/same", StorageItemKind.File, 4, "source-same-v1")
                    }
                    : new[]
                    {
                        Item("empty", "source/folder/empty", StorageItemKind.Directory),
                        Item("nested", "source/folder/nested", StorageItemKind.Directory),
                        Item("root.txt", "source/folder/root.txt", StorageItemKind.File, 4, "source-root-v1"),
                        Item("child.bin", "source/folder/nested/child.bin", StorageItemKind.File, 8, "source-child-v1")
                    };
                return Task.FromResult(new StorageListPageResponse(
                    StorageIpcContract.CurrentVersion,
                    sourceId,
                    request.RelativePath,
                    entries,
                    ContinuationToken: repeatSourcePageToken ? "repeat-token" : null,
                    RootIdentity: sourceRoot));
            }

            Assert.Equal(destinationId, request.ConnectionId);
            Assert.Equal("destination/folder", request.RelativePath);
            return Task.FromResult(new StorageListPageResponse(
                StorageIpcContract.CurrentVersion,
                destinationId,
                request.RelativePath,
                [],
                ContinuationToken: null,
                new StorageIpcFailure(
                    "storage.not_found",
                    StorageIpcFailureCategory.NotFound,
                    "Not found.",
                    IsTransient: false),
                destinationRoot));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static StorageListItem Item(
            string name,
            string path,
            StorageItemKind kind,
            long? length = null,
            string? version = null) => new(
                name,
                path,
                kind,
                length,
                LastModifiedUtc: null,
                ContentType: null,
                IsContainer: kind is StorageItemKind.Directory or StorageItemKind.Prefix,
                VersionId: version);
    }

    private sealed class FakeMutationClient : IObjectInspectorAgentClient
    {
        public List<string> EnsuredPaths { get; } = [];

        public Task<StorageDirectoryEnsureResponse> EnsureDirectoryAsync(
            StorageDirectoryEnsureRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsuredPaths.Add(request.Address.RelativePath);
            return Task.FromResult(new StorageDirectoryEnsureResponse(
                EditableFileIpcContract.CurrentVersion,
                request.Address,
                Created: true));
        }

        public Task<ObjectVersionListResponse> ListVersionsAsync(ObjectVersionListRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ObjectMetadataGetResponse> GetMetadataAsync(ObjectMetadataGetRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ObjectTagsGetResponse> GetTagsAsync(ObjectTagsGetRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeTransferClient : ITransferQueueAgentClient
    {
        public List<TransferEnqueueRequest> Requests { get; } = [];

        public Task<TransferEnqueueResponse> EnqueueAsync(
            TransferEnqueueRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new TransferEnqueueResponse(
                TransferQueueIpcContract.CurrentVersion,
                request.TransferId,
                Accepted: true,
                AlreadyExisted: false,
                Transfer: new TransferQueueSummary(
                    request.TransferId,
                    request.Operation,
                    request.Source.ConnectionId,
                    request.Source.RelativePath,
                    request.Destination.ConnectionId,
                    request.Destination.RelativePath,
                    TransferQueueState.Pending,
                    Revision: 0,
                    Attempt: 0,
                    request.Priority,
                    request.ExpectedLength,
                    ProgressBytes: 0,
                    DateTimeOffset.UtcNow,
                    RetryAvailableUtc: null,
                    ErrorCode: null,
                    ErrorSummary: null,
                    CanCancel: true,
                    CanRetry: false,
                    NeedsReconciliation: false)));
        }

        public Task<TransferListResponse> ListAsync(TransferListRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TransferStatusResponse> GetStatusAsync(TransferStatusRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TransferMutationResponse> CancelAsync(TransferCancelRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TransferMutationResponse> RetryAsync(TransferRetryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TransferMutationResponse> ReconcileAsync(TransferReconcileRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
