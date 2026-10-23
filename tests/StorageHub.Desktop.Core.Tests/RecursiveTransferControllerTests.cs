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

    /// <summary>
    /// A folder copied onto one that already holds two of its files, one of them two levels down,
    /// is asked about once, at the first, and the answer is applied to both.
    /// </summary>
    [Theory]
    [InlineData(TransferConflictChoice.Skip)]
    [InlineData(TransferConflictChoice.Rename)]
    [InlineData(TransferConflictChoice.Overwrite)]
    public async Task Files_already_anywhere_in_the_destination_tree_are_asked_about_once(TransferConflictChoice answer)
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var storage = new FakeStorageClient(
            sourceId,
            destinationId,
            "source-root",
            "destination-root",
            destinationExisting:
            [
                FakeStorageClient.Item("root.txt", "destination/folder/root.txt", StorageItemKind.File, 4, "dest-root-v1"),
                FakeStorageClient.Item("nested", "destination/folder/nested", StorageItemKind.Directory),
                FakeStorageClient.Item("child.bin", "destination/folder/nested/child.bin", StorageItemKind.File, 8, "dest-child-v1")
            ]);
        var queue = new FakeTransferClient();
        await using var transfers = new ManualTransferController(queue);
        await using var controller = new RecursiveTransferController(transfers, storage, new FakeMutationClient());
        var asked = new List<string>();
        var conflicts = new TransferConflictPolicy((existing, _) =>
        {
            asked.Add(existing.RelativePath);
            return Task.FromResult<TransferConflictChoice?>(answer);
        });

        var result = await controller.EnqueueAsync(
            CreateFolderSelection(sourceId, "source-root"),
            CreateDestination(destinationId, "destination-root"),
            TransferQueueOperation.Copy,
            progress: null,
            conflicts,
            stop: default,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal(["destination/folder/root.txt"], asked);
        switch (answer)
        {
            case TransferConflictChoice.Skip:
                Assert.Empty(queue.Requests);
                Assert.Equal(2, conflicts.Skipped);
                break;
            case TransferConflictChoice.Rename:
                Assert.Equal(
                    ["destination/folder/root (1).txt", "destination/folder/nested/child (1).bin"],
                    queue.Requests.Select(static request => request.Destination.RelativePath));
                Assert.All(queue.Requests, static request => Assert.Null(request.ExpectedDestinationVersionId));
                Assert.Equal(2, conflicts.Renamed);
                break;
            default:
                Assert.Equal(
                    ["dest-root-v1", "dest-child-v1"],
                    queue.Requests.Select(static request => request.ExpectedDestinationVersionId));
                Assert.Equal(0, conflicts.Skipped + conflicts.Renamed);
                break;
        }
    }

    /// <summary>
    /// A renamed file must not take a name a sibling later in the same folder already has: "a.txt"
    /// renamed to "a (1).txt" made the real "a (1).txt" a duplicate target and stopped the copy.
    /// </summary>
    [Fact]
    public async Task A_rename_skips_names_that_files_later_in_the_folder_bring()
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var storage = new FakeStorageClient(
            sourceId,
            destinationId,
            "source-root",
            "destination-root",
            destinationExisting:
            [
                FakeStorageClient.Item("a.txt", "destination/folder/a.txt", StorageItemKind.File, 4, "dest-a-v1")
            ],
            sourceEntries:
            [
                FakeStorageClient.Item("a.txt", "source/folder/a.txt", StorageItemKind.File, 4, "source-a-v1"),
                FakeStorageClient.Item("a (1).txt", "source/folder/a (1).txt", StorageItemKind.File, 4, "source-a1-v1")
            ]);
        var queue = new FakeTransferClient();
        await using var transfers = new ManualTransferController(queue);
        await using var controller = new RecursiveTransferController(transfers, storage, new FakeMutationClient());
        var conflicts = new TransferConflictPolicy(static (_, _) =>
            Task.FromResult<TransferConflictChoice?>(TransferConflictChoice.Rename));

        var result = await controller.EnqueueAsync(
            CreateFolderSelection(sourceId, "source-root"),
            CreateDestination(destinationId, "destination-root"),
            TransferQueueOperation.Copy,
            progress: null,
            conflicts,
            stop: default,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal(
            [("source/folder/a (1).txt", "destination/folder/a (1).txt"), ("source/folder/a.txt", "destination/folder/a (2).txt")],
            queue.Requests.Select(static request => (request.Source.RelativePath, request.Destination.RelativePath)));
        Assert.Equal(1, conflicts.Renamed);
    }

    [Fact]
    public async Task Cancelling_the_question_stops_the_walk_without_queueing_the_file_it_was_about()
    {
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var storage = new FakeStorageClient(
            sourceId,
            destinationId,
            "source-root",
            "destination-root",
            destinationExisting:
            [
                FakeStorageClient.Item("root.txt", "destination/folder/root.txt", StorageItemKind.File, 4, "dest-root-v1")
            ]);
        var queue = new FakeTransferClient();
        await using var transfers = new ManualTransferController(queue);
        await using var controller = new RecursiveTransferController(transfers, storage, new FakeMutationClient());

        var result = await controller.EnqueueAsync(
            CreateFolderSelection(sourceId, "source-root"),
            CreateDestination(destinationId, "destination-root"),
            TransferQueueOperation.Copy,
            progress: null,
            new TransferConflictPolicy(static (_, _) => Task.FromResult<TransferConflictChoice?>(null)),
            stop: default,
            CancellationToken.None);

        Assert.Equal("manual_transfer.conflict_cancelled", result.Failure?.Code);
        Assert.Equal(Contracts.Results.StorageFailureKind.Cancelled, result.Failure?.Kind);
        Assert.Empty(queue.Requests);
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
        bool sourceKindCollision = false,
        StorageListItem[]? destinationExisting = null,
        StorageListItem[]? sourceEntries = null) : IRemoteStorageAgentClient
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
                var entries = sourceEntries ?? (sourceKindCollision
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
                    });
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
            if (destinationExisting is not null)
            {
                return Task.FromResult(new StorageListPageResponse(
                    StorageIpcContract.CurrentVersion,
                    destinationId,
                    request.RelativePath,
                    destinationExisting,
                    ContinuationToken: null,
                    RootIdentity: destinationRoot));
            }

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

        internal static StorageListItem Item(
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
