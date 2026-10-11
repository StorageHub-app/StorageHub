using static StorageHub.Desktop.Tests.TestPaths;

using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Compare Folders from the desktop: the agent's comparison read to the end and let go, and the
/// differences somebody picks planned as ordinary queued copies.
/// </summary>
public sealed class FolderCompareControllerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid RemoteId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task A_comparison_is_waited_for_read_in_pages_and_released()
    {
        var entries = Enumerable.Range(0, 5)
            .Select(index => Entry($"f{index}.txt", FolderCompareKind.OnlyLeft, left: File(1)))
            .ToArray();
        await using var agent = new FakeCompareClient(entries, runningPolls: 2, pageSize: 2);
        await using var controller = new FolderCompareController(agent, pollInterval: TimeSpan.FromMilliseconds(1));

        var result = await controller.CompareAsync(Remote("base"), Local(), compareContents: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(entries.Select(static entry => entry.RelativePath), result.Value.Entries.Select(static entry => entry.RelativePath));
        Assert.True(agent.Started!.CompareContents);
        Assert.Equal("base", agent.Started.Left.RelativePath);
        Assert.Equal(string.Empty, agent.Started.Right.RelativePath);
        Assert.Equal([0, 0, 0, 2, 4], agent.Offsets);
        Assert.True(agent.Released);
    }

    [Fact]
    public async Task A_comparison_that_failed_says_why_and_is_still_released()
    {
        await using var agent = new FakeCompareClient([], failure: new StorageIpcFailure(
            "local-user.not-found", StorageIpcFailureCategory.NotFound, "The local folder no longer exists.", false));
        await using var controller = new FolderCompareController(agent, pollInterval: TimeSpan.FromMilliseconds(1));

        var result = await controller.CompareAsync(Remote(""), Local(), compareContents: false, CancellationToken.None);

        Assert.Equal("local-user.not-found", result.Error!.Code);
        Assert.Equal(StorageFailureKind.NotFound, result.Error.Kind);
        Assert.True(agent.Released);
    }

    /// <summary>The drive list, and anything else that is not one folder, has nothing to compare.</summary>
    [Fact]
    public async Task A_pane_without_a_folder_is_refused_before_the_agent_is_asked()
    {
        await using var agent = new FakeCompareClient([]);
        await using var controller = new FolderCompareController(agent);
        var drives = PaneTransferContext.Create(PaneTransferContextKind.ThisPc, null, null, string.Empty).Value;

        var result = await controller.CompareAsync(drives, Remote(""), compareContents: false, CancellationToken.None);

        Assert.Equal("compare.pane_unsupported", result.Error!.Code);
        Assert.Null(agent.Started);
    }

    [Fact]
    public async Task Copying_to_the_right_creates_missing_folders_and_queues_files_held_to_what_they_replace()
    {
        var compared = Outcome(Remote("base"), Local(),
            Entry("docs", FolderCompareKind.OnlyLeft, left: Folder()),
            Entry("docs/a.txt", FolderCompareKind.OnlyLeft, left: File(3, "etag-a")),
            Entry("docs/sub", FolderCompareKind.OnlyLeft, left: Folder()),
            Entry("b.txt", FolderCompareKind.Different, FolderCompareReasons.Size, File(5, "etag-b"), File(4, "local:b")),
            Entry("c.txt", FolderCompareKind.OnlyRight, right: File(1)),
            Entry("same.txt", FolderCompareKind.Same, left: File(1, "s"), right: File(1, "s")),
            Entry("clash", FolderCompareKind.Different, FolderCompareReasons.Type, Folder(), File(1)));
        var queue = new FakeTransferClient();
        await using var transfers = new ManualTransferController(queue);
        var created = new List<string>();

        var result = await FolderCompareController.CopyAsync(
            compared,
            [compared.Entries[0], compared.Entries[3], compared.Entries[4], compared.Entries[5], compared.Entries[6]],
            FolderCompareDirection.LeftToRight,
            transfers,
            (folder, _) => { created.Add(folder); return Task.FromResult<StorageFailure?>(null); },
            cancellationToken: CancellationToken.None);

        Assert.Null(result.Failure);
        Assert.Equal(["docs", "docs/sub"], created);
        Assert.Equal(2, result.Queued);
        Assert.Equal(1, result.LeftOut);
        var copy = queue.Requests.Single(request => request.Source.RelativePath == "base/docs/a.txt");
        Assert.Equal(TransferQueueOperation.Copy, copy.Operation);
        Assert.Equal("docs/a.txt", copy.Destination.RelativePath);
        Assert.Equal(LocalTransferFolder.CreateRootIdentity(Rooted("work")), copy.Destination.RootIdentity);
        Assert.Null(copy.ExpectedDestinationEntityTag);
        var replace = queue.Requests.Single(request => request.Source.RelativePath == "base/b.txt");
        Assert.Equal("etag-b", replace.Source.EntityTag);
        Assert.Equal("local:b", replace.ExpectedDestinationEntityTag);
    }

    /// <summary>
    /// Names matched ignoring case keep each side's spelling: a copy lands in the folder that is
    /// already there, under the name already there, rather than beside it in a second spelling.
    /// </summary>
    [Fact]
    public async Task A_copy_uses_each_sides_own_spelling()
    {
        var compared = Outcome(Remote("base"), Local(),
            Entry("Docs", FolderCompareKind.Same, left: Folder(), right: Folder(), rightPath: "docs"),
            Entry("Docs/new.txt", FolderCompareKind.OnlyRight, right: File(2, "local:n"), rightPath: "docs/new.txt"),
            Entry("Docs/old.txt", FolderCompareKind.OnlyLeft, left: File(2, "e"), rightPath: "docs/old.txt"),
            caseInsensitive: true);
        var queue = new FakeTransferClient();
        await using var transfers = new ManualTransferController(queue);

        await FolderCompareController.CopyAsync(
            compared, [compared.Entries[0]], FolderCompareDirection.LeftToRight, transfers, NoFolders,
            cancellationToken: CancellationToken.None);
        await FolderCompareController.CopyAsync(
            compared, [compared.Entries[0]], FolderCompareDirection.RightToLeft, transfers, NoFolders,
            cancellationToken: CancellationToken.None);

        Assert.Collection(
            queue.Requests,
            toRight =>
            {
                Assert.Equal("base/Docs/old.txt", toRight.Source.RelativePath);
                Assert.Equal("docs/old.txt", toRight.Destination.RelativePath);
            },
            toLeft =>
            {
                Assert.Equal("docs/new.txt", toLeft.Source.RelativePath);
                Assert.Equal("base/Docs/new.txt", toLeft.Destination.RelativePath);
            });
    }

    [Theory]
    [InlineData(TransferConflictChoice.Skip, null, 1, 0)]
    [InlineData(TransferConflictChoice.Rename, "base/b (2).txt", 0, 1)]
    [InlineData(TransferConflictChoice.OverwriteIfNewer, "base/b.txt", 0, 0)]
    public async Task A_file_already_there_is_met_with_the_conflict_choice(
        TransferConflictChoice choice,
        string? destination,
        int skipped,
        int renamed)
    {
        var compared = Outcome(Local(), Remote("base"),
            Entry("b.txt", FolderCompareKind.Different, FolderCompareReasons.LeftNewer,
                File(5, "local:b", Noon.AddHours(1)), File(5, "etag-b", Noon)),
            Entry("b (1).txt", FolderCompareKind.OnlyRight, right: File(1)));
        var queue = new FakeTransferClient();
        await using var transfers = new ManualTransferController(queue);
        var policy = new TransferConflictPolicy(choice);

        var result = await FolderCompareController.CopyAsync(
            compared, [compared.Entries[0]], FolderCompareDirection.LeftToRight, transfers, NoFolders, policy,
            CancellationToken.None);

        Assert.Equal(destination, queue.Requests.SingleOrDefault()?.Destination.RelativePath);
        Assert.Equal((skipped, renamed), (result.Skipped, result.Renamed));
        Assert.Equal((skipped, renamed), (policy.Skipped, policy.Renamed));
        if (choice == TransferConflictChoice.Rename)
        {
            Assert.Null(queue.Requests[0].ExpectedDestinationEntityTag);
        }
    }

    [Fact]
    public async Task The_question_is_asked_once_and_cancel_queues_nothing()
    {
        var compared = Outcome(Remote("a"), Local(),
            Entry("x.txt", FolderCompareKind.Different, FolderCompareReasons.Size, File(2, "e1"), File(1, "l1")),
            Entry("y.txt", FolderCompareKind.Different, FolderCompareReasons.Size, File(2, "e2"), File(1, "l2")));
        var queue = new FakeTransferClient();
        await using var transfers = new ManualTransferController(queue);
        var asked = 0;
        var policy = new TransferConflictPolicy((_, _) => { asked++; return Task.FromResult<TransferConflictChoice?>(null); });

        var result = await FolderCompareController.CopyAsync(
            compared, compared.Entries, FolderCompareDirection.LeftToRight, transfers, NoFolders, policy,
            CancellationToken.None);

        Assert.Equal(1, asked);
        Assert.Equal(ManualTransferController.StoppedAtConflict().Code, result.Failure!.Code);
        Assert.Empty(queue.Requests);
    }

    /// <summary>
    /// A file whose version nobody listed cannot be replaced safely, so it is left out rather than
    /// overwritten blind.
    /// </summary>
    [Fact]
    public async Task A_file_to_replace_without_an_identity_is_left_out()
    {
        var compared = Outcome(Remote("a"), Local(),
            Entry("x.txt", FolderCompareKind.Different, FolderCompareReasons.Size, File(2, "e1"), File(1)));
        var queue = new FakeTransferClient();
        await using var transfers = new ManualTransferController(queue);

        var result = await FolderCompareController.CopyAsync(
            compared, compared.Entries, FolderCompareDirection.LeftToRight, transfers, NoFolders,
            cancellationToken: CancellationToken.None);

        Assert.Equal(1, result.LeftOut);
        Assert.Empty(queue.Requests);
    }

    [Fact]
    public async Task Two_folders_on_this_pc_are_compared_but_not_copied_between()
    {
        var compared = Outcome(Local(), Local("other"), Entry("a.txt", FolderCompareKind.OnlyLeft, left: File(1)));
        var queue = new FakeTransferClient();
        await using var transfers = new ManualTransferController(queue);

        var result = await FolderCompareController.CopyAsync(
            compared, compared.Entries, FolderCompareDirection.LeftToRight, transfers, NoFolders,
            cancellationToken: CancellationToken.None);

        Assert.Equal("manual_transfer.local_to_local_unsupported", result.Failure!.Code);
        Assert.Empty(queue.Requests);
    }

    [Fact]
    public void A_local_folder_is_created_inside_its_base_and_never_above_it()
    {
        var root = Path.Combine(Path.GetTempPath(), $"storagehub-compare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(FolderCompareController.EnsureLocalFolder(root, "a/b"));
            Assert.True(Directory.Exists(Path.Combine(root, "a", "b")));
            Assert.NotNull(FolderCompareController.EnsureLocalFolder(root, "../escape"));
            Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(root)!, "escape")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Task<StorageFailure?> NoFolders(string folder, CancellationToken cancellationToken) =>
        Task.FromResult<StorageFailure?>(null);

    private static PaneTransferContext Remote(string path) =>
        PaneTransferContext.Create(PaneTransferContextKind.SavedConnection, RemoteId, "root-1", path).Value;

    private static PaneTransferContext Local(string name = "work") =>
        PaneTransferContext.Create(PaneTransferContextKind.ThisPc, null, null, Rooted(name)).Value;

    private static FolderCompareOutcome Outcome(
        PaneTransferContext left,
        PaneTransferContext right,
        params FolderCompareEntry[] entries) => Outcome(left, right, entries, caseInsensitive: false);

    private static FolderCompareOutcome Outcome(
        PaneTransferContext left,
        PaneTransferContext right,
        FolderCompareEntry first,
        FolderCompareEntry second,
        FolderCompareEntry third,
        bool caseInsensitive) => Outcome(left, right, [first, second, third], caseInsensitive);

    private static FolderCompareOutcome Outcome(
        PaneTransferContext left,
        PaneTransferContext right,
        FolderCompareEntry[] entries,
        bool caseInsensitive)
    {
        Assert.All(entries, static entry => Assert.True(entry.HasValidBounds));
        return new FolderCompareOutcome(left, right, entries, caseInsensitive, 0);
    }

    private static FolderCompareEntry Entry(
        string path,
        FolderCompareKind kind,
        FolderCompareReasons reasons = FolderCompareReasons.None,
        FolderCompareItem? left = null,
        FolderCompareItem? right = null,
        string? rightPath = null) => new(path, kind, reasons, left, right, rightPath);

    private static FolderCompareItem File(long size, string? entityTag = null, DateTimeOffset? modified = null) =>
        new(StorageItemKind.File, size, modified ?? Noon, EntityTag: entityTag);

    private static FolderCompareItem Folder() => new(StorageItemKind.Directory, null, Noon);

    private sealed class FakeCompareClient(
        FolderCompareEntry[] entries,
        int runningPolls = 0,
        int pageSize = 500,
        StorageIpcFailure? failure = null) : IFolderCompareAgentClient
    {
        private static readonly Guid Id = Guid.NewGuid();
        private int _polls;

        public FolderCompareStartRequest? Started { get; private set; }
        public List<int> Offsets { get; } = [];
        public bool Released { get; private set; }

        public Task<FolderCompareStartResponse> StartAsync(
            FolderCompareStartRequest request,
            CancellationToken cancellationToken = default)
        {
            Started = request;
            return Task.FromResult(new FolderCompareStartResponse(request.ContractVersion, Id));
        }

        public Task<FolderCompareStatusResponse> GetStatusAsync(
            FolderCompareStatusRequest request,
            CancellationToken cancellationToken = default)
        {
            Offsets.Add(request.Offset);
            if (_polls++ < runningPolls)
            {
                return Task.FromResult(new FolderCompareStatusResponse(
                    request.ContractVersion, Id, FolderCompareState.Running, 0, []));
            }

            if (failure is not null)
            {
                return Task.FromResult(new FolderCompareStatusResponse(
                    request.ContractVersion, Id, FolderCompareState.Failed, 0, [], Failure: failure));
            }

            return Task.FromResult(new FolderCompareStatusResponse(
                request.ContractVersion,
                Id,
                FolderCompareState.Completed,
                entries.Length,
                [.. entries.Skip(request.Offset).Take(pageSize)]));
        }

        public Task<FolderCompareReleaseResponse> ReleaseAsync(
            FolderCompareReleaseRequest request,
            CancellationToken cancellationToken = default)
        {
            Released = true;
            return Task.FromResult(new FolderCompareReleaseResponse(request.ContractVersion, Id, true));
        }

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
