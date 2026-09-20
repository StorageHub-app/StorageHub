using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// Copying between two panes, end to end, without a window.
/// </summary>
/// <remarks>
/// The whole path is exercised: a selection in one pane, a listing in the other, the snapshots
/// PaneTransferSnapshots builds from both, the plan ManualTransferController makes of them, and the
/// enqueue request that reaches the agent. In the WinForms shell that path ran through a 3,621-line
/// control and could only be checked by doing it by hand.
/// </remarks>
public class WorkspaceTransferTests
{
    [AvaloniaFact]
    public async Task CopyingSendsTheSelectionToTheOtherPane()
    {
        await using var fixture = await Fixture.CreateAsync();

        fixture.Left.SelectedRows.Add(fixture.Left.Rows.Single(row => row.Name == "render.exr"));
        await fixture.Workspace.TransferAsync(
            TransferQueueOperation.Copy, TestContext.Current.CancellationToken);

        var request = Assert.Single(fixture.Queue.Enqueued);
        Assert.Equal(TransferQueueOperation.Copy, request.Operation);
        Assert.Equal("render.exr", request.Source.RelativePath);
        Assert.Equal(fixture.SourceConnectionId, request.Source.ConnectionId);
        Assert.Equal(fixture.DestinationConnectionId, request.Destination.ConnectionId);
    }

    /// <summary>
    /// The source is whichever pane is active, and the destination is always the other.
    /// </summary>
    /// <remarks>
    /// The rule every two-pane manager has used since Norton Commander, and the only rule here.
    /// Worth a test because it is what makes the same two buttons mean opposite things depending
    /// on where somebody last clicked.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheActivePaneIsTheSource()
    {
        await using var fixture = await Fixture.CreateAsync();

        fixture.Right.IsActive = true;
        fixture.Right.SelectedRows.Add(fixture.Right.Rows.Single(row => row.Name == "archive.zip"));
        await fixture.Workspace.TransferAsync(
            TransferQueueOperation.Move, TestContext.Current.CancellationToken);

        var request = Assert.Single(fixture.Queue.Enqueued);
        Assert.Equal(TransferQueueOperation.Move, request.Operation);
        Assert.Equal(fixture.DestinationConnectionId, request.Source.ConnectionId);
        Assert.Equal(fixture.SourceConnectionId, request.Destination.ConnectionId);
    }

    [AvaloniaFact]
    public async Task ActivatingOnePaneDeactivatesTheOther()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.True(fixture.Left.IsActive);

        fixture.Right.IsActive = true;

        Assert.False(fixture.Left.IsActive);
        Assert.Same(fixture.Right, fixture.Workspace.Source);
    }

    [AvaloniaFact]
    public async Task NothingSelectedMeansNothingToCopy()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.False(fixture.Workspace.CopyCommand.CanExecute(null));

        fixture.Left.SelectedRows.Add(fixture.Left.Rows[0]);

        Assert.True(fixture.Workspace.CopyCommand.CanExecute(null));
    }

    /// <summary>The parent row is not a file, and selecting everything must not try to copy it.</summary>
    [AvaloniaFact]
    public async Task TheParentRowIsNeverTransferred()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Left.NavigateAsync("reports", TestContext.Current.CancellationToken);

        foreach (var row in fixture.Left.Rows)
        {
            fixture.Left.SelectedRows.Add(row);
        }

        Assert.Contains(fixture.Left.SelectedRows, row => row.IsParentNavigation);

        await fixture.Workspace.TransferAsync(
            TransferQueueOperation.Copy, TestContext.Current.CancellationToken);

        Assert.All(fixture.Queue.Enqueued, request => Assert.NotEqual("..", request.Source.RelativePath));
        Assert.Single(fixture.Queue.Enqueued);
    }

    [AvaloniaFact]
    public async Task ManySelectedRowsQueueManyTransfers()
    {
        await using var fixture = await Fixture.CreateAsync();

        foreach (var row in fixture.Left.Rows)
        {
            fixture.Left.SelectedRows.Add(row);
        }

        await fixture.Workspace.TransferAsync(
            TransferQueueOperation.Copy, TestContext.Current.CancellationToken);

        Assert.True(fixture.Queue.Enqueued.Count > 0, "refused: " + fixture.Workspace.Message);
        Assert.Equal(fixture.Left.Rows.Count, fixture.Queue.Enqueued.Count);
    }

    /// <summary>
    /// A destination still paging cannot be one.
    /// </summary>
    /// <remarks>
    /// A collision is detected by comparing against what is already in the destination, so a
    /// listing that has not finished would report a file just past the last page as absent -- and
    /// the transfer would silently overwrite it.
    /// </remarks>
    [AvaloniaFact]
    public async Task ADestinationThatHasNotFinishedListingIsRefused()
    {
        await using var fixture = await Fixture.CreateAsync(destinationHasMorePages: true);

        fixture.Left.SelectedRows.Add(fixture.Left.Rows[0]);
        await fixture.Workspace.TransferAsync(
            TransferQueueOperation.Copy, TestContext.Current.CancellationToken);

        Assert.Empty(fixture.Queue.Enqueued);
        Assert.True(fixture.Workspace.HasMessage);
    }

    [AvaloniaFact]
    public async Task AQueueThatIsNotThereLeavesAMessageRatherThanThrowing()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Queue.Throw = true;

        fixture.Left.SelectedRows.Add(fixture.Left.Rows[0]);
        await fixture.Workspace.TransferAsync(
            TransferQueueOperation.Copy, TestContext.Current.CancellationToken);

        Assert.True(fixture.Workspace.HasMessage);
    }

    /// <summary>An accepted transfer tells the queue to look again straight away.</summary>
    [AvaloniaFact]
    public async Task QueueingRefreshesTheQueue()
    {
        var refreshes = 0;
        await using var fixture = await Fixture.CreateAsync(onQueueChanged: () =>
        {
            refreshes++;
            return Task.CompletedTask;
        });

        fixture.Left.SelectedRows.Add(fixture.Left.Rows[0]);
        await fixture.Workspace.TransferAsync(
            TransferQueueOperation.Copy, TestContext.Current.CancellationToken);

        Assert.Equal(1, refreshes);
    }

    /// <summary>Two panes, two connections, and a queue that records what it was asked.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            WorkspaceModel workspace,
            FakeTransferQueue queue,
            Guid source,
            Guid destination)
        {
            Workspace = workspace;
            Queue = queue;
            SourceConnectionId = source;
            DestinationConnectionId = destination;
        }

        internal WorkspaceModel Workspace { get; }

        internal FakeTransferQueue Queue { get; }

        internal BrowserPaneModel Left => Workspace.Left;

        internal BrowserPaneModel Right => Workspace.Right;

        internal Guid SourceConnectionId { get; }

        internal Guid DestinationConnectionId { get; }

        internal static async Task<Fixture> CreateAsync(
            bool destinationHasMorePages = false,
            Func<Task>? onQueueChanged = null)
        {
            var source = Summary("Studio Assets");
            var destination = Summary("Site Backups");

            var agent = new FakeBrowsingAgent([source, destination]);
            agent.Listings[(source.ConnectionId, "")] =
                new Page([Entry("reports", container: true), Entry("render.exr", 1024)]);
            agent.Listings[(source.ConnectionId, "reports")] =
                new Page([Entry("q1.pdf", 2048, parent: "reports")]);
            agent.Listings[(destination.ConnectionId, "")] =
                new Page([Entry("archive.zip", 4096)], destinationHasMorePages ? "more" : null);

            var queue = new FakeTransferQueue();
            var left = new BrowserPaneModel(agent) { IsActive = true };
            var right = new BrowserPaneModel(agent);

            await left.LoadConnectionsAsync(TestContext.Current.CancellationToken);
            await right.LoadConnectionsAsync(TestContext.Current.CancellationToken);
            await left.OpenConnectionAsync(source.ConnectionId, TestContext.Current.CancellationToken);
            await right.OpenConnectionAsync(destination.ConnectionId, TestContext.Current.CancellationToken);

            var workspace = new WorkspaceModel(
                left,
                right,
                () => queue,
                () => agent,
                () => new FakeInspector(),
                onQueueChanged);
            return new Fixture(workspace, queue, source.ConnectionId, destination.ConnectionId);
        }

        public ValueTask DisposeAsync() => Workspace.DisposeAsync();
    }

    private sealed record Page(StorageListItem[] Entries, string? ContinuationToken = null);

    private static ConnectionSummary Summary(string name) => new(
        Guid.NewGuid(),
        name,
        StorageConnectionProvider.S3,
        FolderPath: null,
        Tags: [],
        IsFavorite: false,
        IsEnabled: true,
        IconKey: null,
        AccentColor: null,
        Version: 1);

    /// <summary>
    /// An entry with an entity tag, as a real provider reports one.
    /// </summary>
    /// <remarks>
    /// Without it a move is refused: the agent will only delete the source of an object it can
    /// prove is the one it listed. Copy has no such requirement, which is why the two commands
    /// are enabled by different rules.
    /// </remarks>
    private static StorageListItem Entry(
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

    private sealed class FakeBrowsingAgent(ConnectionSummary[] connections) : IRemoteStorageAgentClient
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
    private sealed class FakeInspector : IObjectInspectorAgentClient
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

    private sealed class FakeTransferQueue : ITransferQueueAgentClient
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
