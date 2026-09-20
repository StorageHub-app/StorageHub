using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The agent a workspace suite runs against: connections, listings, a queue and an inspector.
/// </summary>
/// <remarks>
/// Shared rather than nested in one suite, because the layout tests and the transfer tests need
/// the same agent and a second copy of it is a second thing to keep in step with the contract.
/// Everything here answers only what the shell actually asks; the rest throws, so a test that
/// reaches an unanswered call says so instead of quietly seeing an empty result.
/// </remarks>
internal static class WorkspaceFakes
{
    internal sealed record Page(StorageListItem[] Entries, string? ContinuationToken = null);

    /// <param name="provider">
    /// S3 unless a test needs otherwise. SSH with a client profile is the one combination that
    /// makes a pane a terminal instead of a listing, so it is worth being able to ask for.
    /// </param>
    internal static ConnectionSummary Summary(
        string name,
        StorageConnectionProvider provider = StorageConnectionProvider.S3,
        ConnectionProfileType type = ConnectionProfileType.Storage) => new(
        Guid.NewGuid(),
        name,
        provider,
        FolderPath: null,
        Tags: [],
        IsFavorite: false,
        IsEnabled: true,
        IconKey: null,
        AccentColor: null,
        Version: 1,
        type);

    /// <summary>
    /// An entry with an entity tag, as a real provider reports one.
    /// </summary>
    /// <remarks>
    /// Without it a move is refused: the agent will only delete the source of an object it can
    /// prove is the one it listed. Copy has no such requirement, which is why the two commands
    /// are enabled by different rules.
    /// </remarks>
    internal static StorageListItem Entry(
        string name,
        long? size = null,
        bool container = false,
        string? parent = null) =>
        new(
            name,
            parent is null ? name : parent + "/" + name,
            container ? StorageItemKind.Directory : StorageItemKind.File,
            container ? null : size,
            DateTimeOffset.UtcNow,
            ContentType: null,
            container,
            EntityTag: container ? null : "etag-" + name);

    /// <summary>
    /// One agent that both lists and mutates, recording what it was asked.
    /// </summary>
    /// <remarks>
    /// Both surfaces on one object because a file operation is two halves that have to agree: the
    /// mutation happens, and then the pane lists again to see what the provider actually stored.
    /// Counting the listings is how a test can say the second half happened at all.
    /// </remarks>
    internal sealed class RecordingAgent(ConnectionSummary[] connections)
        : IRemoteStorageAgentClient, IObjectInspectorAgentClient
    {
        internal int Listings { get; private set; }

        internal List<string> CreatedDirectories { get; } = [];

        internal List<string> CreatedFiles { get; } = [];

        internal List<(string From, string To)> Renames { get; } = [];

        internal List<string> Deletes { get; } = [];

        internal StorageIpcFailure? Failure { get; set; }

        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionListResponse(StorageIpcContract.CurrentVersion, connections));

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResponse(
                StorageIpcContract.CurrentVersion, request.ConnectionId, Succeeded: true, 1));

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default)
        {
            Listings++;
            StorageListItem[] entries = request.RelativePath switch
            {
                "" => [Entry("reports", container: true), Entry("render.exr", 1024)],
                "reports" => [Entry("q1.pdf", 2048, parent: "reports")],
                _ => []
            };

            return Task.FromResult(new StorageListPageResponse(
                StorageIpcContract.CurrentVersion,
                request.ConnectionId,
                request.RelativePath,
                entries,
                ContinuationToken: null,
                RootIdentity: "root"));
        }

        public Task<StorageDirectoryCreateResponse> CreateDirectoryAsync(
            StorageDirectoryCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Failure is null) CreatedDirectories.Add(request.Address.RelativePath);
            return Task.FromResult(new StorageDirectoryCreateResponse(
                EditableFileIpcContract.CurrentVersion, request.Address, Failure is null, Failure));
        }

        public Task<StorageFileCreateResponse> CreateFileAsync(
            StorageFileCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Failure is null) CreatedFiles.Add(request.Address.RelativePath);
            return Task.FromResult(new StorageFileCreateResponse(
                EditableFileIpcContract.CurrentVersion, request.Address, Failure is null, Failure));
        }

        public Task<StorageItemRenameResponse> RenameItemAsync(
            StorageItemRenameRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Failure is null)
            {
                Renames.Add((request.Source.RelativePath, request.Destination.RelativePath));
            }

            return Task.FromResult(new StorageItemRenameResponse(
                EditableFileIpcContract.CurrentVersion,
                request.Source,
                request.Destination,
                Failure is null,
                Failure));
        }

        public Task<StorageItemDeleteResponse> DeleteItemAsync(
            StorageItemDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Failure is null) Deletes.Add(request.Address.RelativePath);
            return Task.FromResult(new StorageItemDeleteResponse(
                EditableFileIpcContract.CurrentVersion, request.Address, Failure is null, Failure));
        }

        public Task<ObjectVersionListResponse> ListVersionsAsync(
            ObjectVersionListRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ObjectMetadataGetResponse> GetMetadataAsync(
            ObjectMetadataGetRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ObjectTagsGetResponse> GetTagsAsync(
            ObjectTagsGetRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StorageDirectoryEnsureResponse> EnsureDirectoryAsync(
            StorageDirectoryEnsureRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class FakeBrowsingAgent(ConnectionSummary[] connections) : IRemoteStorageAgentClient
    {
        internal Dictionary<(Guid Connection, string Path), Page> Listings { get; } = [];

        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionListResponse(StorageIpcContract.CurrentVersion, connections));

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResponse(
                StorageIpcContract.CurrentVersion, request.ConnectionId, Succeeded: true, 1));

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default)
        {
            var page = Listings.TryGetValue((request.ConnectionId, request.RelativePath), out var found)
                ? found
                : new Page([]);

            return Task.FromResult(new StorageListPageResponse(
                StorageIpcContract.CurrentVersion,
                request.ConnectionId,
                request.RelativePath,
                page.Entries,
                page.ContinuationToken,
                RootIdentity: "root"));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// The inspector, which a recursive copy uses to create the destination folders.
    /// </summary>
    /// <remarks>
    /// Only EnsureDirectoryAsync is answered. The rest throw rather than returning something empty:
    /// a test that reaches one is asking a question this fake cannot answer and should say so.
    /// </remarks>
    internal sealed class FakeInspector : IObjectInspectorAgentClient
    {
        public Task<StorageDirectoryEnsureResponse> EnsureDirectoryAsync(
            StorageDirectoryEnsureRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StorageDirectoryEnsureResponse(
                EditableFileIpcContract.CurrentVersion, request.Address, Created: true));

        public Task<ObjectVersionListResponse> ListVersionsAsync(
            ObjectVersionListRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ObjectMetadataGetResponse> GetMetadataAsync(
            ObjectMetadataGetRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ObjectTagsGetResponse> GetTagsAsync(
            ObjectTagsGetRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<EditableFileDownloadResponse> DownloadEditableFileAsync(
            EditableFileDownloadRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<EditableFileUploadResponse> UploadEditedFileAsync(
            EditableFileUploadRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StorageDirectoryCreateResponse> CreateDirectoryAsync(
            StorageDirectoryCreateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StorageFileCreateResponse> CreateFileAsync(
            StorageFileCreateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StorageItemRenameResponse> RenameItemAsync(
            StorageItemRenameRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StorageItemDeleteResponse> DeleteItemAsync(
            StorageItemDeleteRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class FakeTransferQueue : ITransferQueueAgentClient
    {
        internal List<TransferEnqueueRequest> Enqueued { get; } = [];

        internal bool Throw { get; set; }

        public Task<TransferEnqueueResponse> EnqueueAsync(
            TransferEnqueueRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Throw) throw new IOException("The agent is not listening.");
            Enqueued.Add(request);
            return Task.FromResult(new TransferEnqueueResponse(
                TransferQueueIpcContract.CurrentVersion,
                request.TransferId,
                Accepted: true,
                AlreadyExisted: false,
                new TransferQueueSummary(
                    request.TransferId,
                    request.Operation,
                    request.Source.ConnectionId,
                    request.Source.RelativePath,
                    request.Destination.ConnectionId,
                    request.Destination.RelativePath,
                    TransferQueueState.Pending,
                    Revision: 1,
                    Attempt: 0,
                    Priority: request.Priority,
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

        public Task<TransferListResponse> ListAsync(
            TransferListRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TransferListResponse(
                TransferQueueIpcContract.CurrentVersion, [], null));

        public Task<TransferStatusResponse> GetStatusAsync(
            TransferStatusRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TransferMutationResponse> CancelAsync(
            TransferCancelRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TransferMutationResponse> RetryAsync(
            TransferRetryRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TransferMutationResponse> ReconcileAsync(
            TransferReconcileRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
