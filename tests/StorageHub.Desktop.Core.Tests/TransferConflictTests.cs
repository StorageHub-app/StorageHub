using static StorageHub.Desktop.Tests.TestPaths;

using StorageHub.Contracts.Ipc;
using StorageHub.Contracts.Results;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// What a transfer does with a file already at its destination: the six choices, the plan each
/// makes, and the one question asked to choose.
/// </summary>
public sealed class TransferConflictTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TransferConflictChoice.Overwrite, 10, 10, 0, "overwrite")]
    [InlineData(TransferConflictChoice.Skip, 10, 99, 3600, "skip")]
    [InlineData(TransferConflictChoice.Rename, 10, 10, 0, "rename")]
    // Newer by more than the two seconds a FAT disk or a minute-listing server blurs; within them,
    // or older, the copy there is taken as current.
    [InlineData(TransferConflictChoice.OverwriteIfNewer, 10, 10, 60, "overwrite")]
    [InlineData(TransferConflictChoice.OverwriteIfNewer, 10, 10, 2, "skip")]
    [InlineData(TransferConflictChoice.OverwriteIfNewer, 10, 10, -60, "skip")]
    [InlineData(TransferConflictChoice.OverwriteIfSizeDiffers, 10, 11, 0, "overwrite")]
    [InlineData(TransferConflictChoice.OverwriteIfSizeDiffers, 10, 10, 3600, "skip")]
    // Resume finishes a shorter file and leaves one as long as the source, or longer, alone.
    [InlineData(TransferConflictChoice.Resume, 10, 4, 0, "overwrite")]
    [InlineData(TransferConflictChoice.Resume, 10, 10, 0, "skip")]
    [InlineData(TransferConflictChoice.Resume, 10, 12, 0, "skip")]
    public void EachChoiceDecidesFromWhatBothListingsSay(
        TransferConflictChoice choice,
        long sourceLength,
        long existingLength,
        int sourceNewerBySeconds,
        string expected)
    {
        var source = Item("a.txt", "source/a.txt", sourceLength, Noon.AddSeconds(sourceNewerBySeconds), "s-v1");
        var existing = Item("a.txt", "destination/a.txt", existingLength, Noon, "d-v1");

        Assert.Equal(expected, TransferConflictRules.Decide(choice, source, existing).ToString().ToLowerInvariant());
    }

    /// <summary>
    /// A rule that cannot tell the two apart -- a listing with no time or no size -- cannot say the
    /// destination is already right, so it replaces.
    /// </summary>
    [Theory]
    [InlineData(TransferConflictChoice.OverwriteIfNewer)]
    [InlineData(TransferConflictChoice.OverwriteIfSizeDiffers)]
    [InlineData(TransferConflictChoice.Resume)]
    public void WhatARuleCannotSeeIsReplaced(TransferConflictChoice choice)
    {
        var source = Item("a.txt", "source/a.txt", length: null, modified: null, "s-v1");
        var existing = Item("a.txt", "destination/a.txt", length: null, modified: null, "d-v1");

        Assert.Equal(TransferConflictOutcome.Overwrite, TransferConflictRules.Decide(choice, source, existing));
    }

    [Theory]
    [InlineData("report.pdf", "report (1).pdf")]
    [InlineData("archive.tar.gz", "archive.tar (1).gz")]
    [InlineData("Makefile", "Makefile (1)")]
    [InlineData(".bashrc", ".bashrc (1)")]
    public void ARenameNumbersTheNameBeforeItsExtension(string name, string expected) =>
        Assert.Equal(expected, TransferConflictRules.FreeName(name, static _ => false));

    [Fact]
    public void ARenameSkipsNumbersAlreadyTakenAndGivesUpRatherThanLoopingForever()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "a (1).txt", "a (2).txt" };
        Assert.Equal("a (3).txt", TransferConflictRules.FreeName("a.txt", taken.Contains));
        Assert.Null(TransferConflictRules.FreeName("a.txt", static _ => true));
    }

    [Fact]
    public void SkipLeavesTheFileOutOfThePlanAndCountsIt()
    {
        var plan = Plan(TransferConflictChoice.Skip);

        Assert.True(plan.IsSuccess, plan.IsFailure ? plan.Error.Message : null);
        var request = Assert.Single(plan.Value.Requests);
        Assert.Equal("destination/new.txt", request.Destination.RelativePath);
        Assert.Equal(1, plan.Value.Skipped);
    }

    [Fact]
    public void RenameWritesBesideTheExistingFileAsANewOneAndAvoidsEveryNameInSight()
    {
        // "a (1).txt" is at the destination and "a (2).txt" is another selected file, so the
        // copy of a.txt lands on "a (3).txt", created rather than replacing anything.
        var source = Saved(Guid.NewGuid(), "source-root", "source");
        var selection = Selection(
            source,
            Item("a.txt", "source/a.txt", 5, Noon, "s-a"),
            Item("a (2).txt", "source/a (2).txt", 5, Noon, "s-a2"));
        var destination = Destination(
            Saved(Guid.NewGuid(), "destination-root", "destination"),
            Item("a.txt", "destination/a.txt", 5, Noon, "d-a"),
            Item("a (1).txt", "destination/a (1).txt", 5, Noon, "d-a1"));

        var plan = new ManualTransferController(new FakeTransferClient())
            .BuildPlan(selection, destination, TransferQueueOperation.Copy, conflicts: TransferConflictChoice.Rename);

        Assert.True(plan.IsSuccess, plan.IsFailure ? plan.Error.Message : null);
        Assert.Equal(
            ["destination/a (3).txt", "destination/a (2).txt"],
            plan.Value.Requests.Select(static request => request.Destination.RelativePath));
        Assert.All(plan.Value.Requests, static request =>
        {
            Assert.Null(request.ExpectedDestinationVersionId);
            Assert.Null(request.ExpectedDestinationEntityTag);
        });
        Assert.Equal(1, plan.Value.Renamed);
    }

    [Fact]
    public void AnOverwriteStillNeedsBothIdentitiesWhateverRuleChoseIt()
    {
        var selection = Selection(
            Saved(Guid.NewGuid(), "source-root", "source"),
            Item("a.txt", "source/a.txt", 5, Noon.AddHours(1), versionId: null));
        var destination = Destination(
            Saved(Guid.NewGuid(), "destination-root", "destination"),
            Item("a.txt", "destination/a.txt", 5, Noon, "d-a"));

        var plan = new ManualTransferController(new FakeTransferClient())
            .BuildPlan(selection, destination, TransferQueueOperation.Copy, conflicts: TransferConflictChoice.OverwriteIfNewer);

        Assert.True(plan.IsFailure);
        Assert.Equal("manual_transfer.overwrite.identity_required", plan.Error.Code);
    }

    [Fact]
    public async Task TheQuestionIsAskedOnceAtTheFirstConflictAndOnlyWhenThereIsOne()
    {
        var client = new FakeTransferClient();
        await using var controller = new ManualTransferController(client);
        var asked = new List<string>();
        var conflicts = new TransferConflictPolicy((existing, _) =>
        {
            asked.Add(existing.Name);
            return Task.FromResult<TransferConflictChoice?>(TransferConflictChoice.Skip);
        });
        var source = Saved(Guid.NewGuid(), "source-root", "source");
        var target = Saved(Guid.NewGuid(), "destination-root", "destination");

        // Nothing in the way: nothing asked.
        var clear = await controller.EnqueueAsync(
            Selection(source, Item("new.txt", "source/new.txt", 5, Noon, "s-new")),
            Destination(target),
            TransferQueueOperation.Copy,
            conflicts: conflicts,
            cancellationToken: CancellationToken.None);
        Assert.True(clear.IsSuccess);
        Assert.Empty(asked);

        var result = await controller.EnqueueAsync(
            Selection(
                source,
                Item("a.txt", "source/a.txt", 5, Noon, "s-a"),
                Item("b.txt", "source/b.txt", 5, Noon, "s-b"),
                Item("c.txt", "source/c.txt", 5, Noon, "s-c")),
            Destination(
                target,
                Item("a.txt", "destination/a.txt", 5, Noon, "d-a"),
                Item("b.txt", "destination/b.txt", 5, Noon, "d-b")),
            TransferQueueOperation.Copy,
            conflicts: conflicts,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.Failure?.Message);
        Assert.Equal(["a.txt"], asked);
        Assert.Equal(["source/new.txt", "source/c.txt"], client.Requests.Select(static request => request.Source.RelativePath));
        Assert.Equal(2, result.Skipped);
        Assert.Equal(2, conflicts.Skipped);
    }

    [Fact]
    public async Task CancellingTheQuestionQueuesNothing()
    {
        var client = new FakeTransferClient();
        await using var controller = new ManualTransferController(client);

        var result = await controller.EnqueueAsync(
            Selection(Saved(Guid.NewGuid(), "source-root", "source"), Item("a.txt", "source/a.txt", 5, Noon, "s-a")),
            Destination(
                Saved(Guid.NewGuid(), "destination-root", "destination"),
                Item("a.txt", "destination/a.txt", 5, Noon, "d-a")),
            TransferQueueOperation.Copy,
            conflicts: new TransferConflictPolicy(static (_, _) => Task.FromResult<TransferConflictChoice?>(null)),
            cancellationToken: CancellationToken.None);

        Assert.Equal("manual_transfer.conflict_cancelled", result.Failure?.Code);
        Assert.Equal(StorageFailureKind.Cancelled, result.Failure?.Kind);
        Assert.Empty(client.Requests);
    }

    /// <summary>
    /// A file on this PC has no version, so it could neither be replaced nor replace anything.
    /// Its listed time and length are the identity the agent checks it against now, on both sides.
    /// </summary>
    [Fact]
    public async Task FilesOnThisPcCarryTheIdentityTheAgentFindsThemBy()
    {
        var folder = Directory.CreateTempSubdirectory("storagehub-conflict-").FullName;
        try
        {
            await using var source = new LocalPaneSource(new LocalBrowserController());
            var moved = await source.MoveAsync(
                PaneNavigationKind.Navigate, folder, CancellationToken.None);
            Assert.True(moved.Listing is not null, moved.Error);
            var row = new BrowserListItem(
                "report.pdf", "1 KB", "PDF", "now", string.Empty,
                Location: Path.Combine(folder, "report.pdf"),
                Kind: StorageItemKind.File,
                Length: 1024,
                ModifiedUtc: Noon);

            var destination = PaneTransferSnapshots.DestinationFor(source, [row], hasMorePages: false);
            var selection = PaneTransferSnapshots.SelectionFor(source, [row]);

            Assert.True(destination.IsSuccess, destination.IsFailure ? destination.Error.Message : null);
            var expected = LocalTransferFolder.FileIdentity(Noon, 1024);
            Assert.Equal(expected, Assert.Single(destination.Value.Entries).EntityTag);
            Assert.Equal(expected, Assert.Single(selection.Value.Items).EntityTag);

            // Downloading over it is now an ordinary, identity-checked overwrite.
            var plan = new ManualTransferController(new FakeTransferClient()).BuildPlan(
                Selection(
                    Saved(Guid.NewGuid(), "source-root", "source"),
                    Item("report.pdf", "source/report.pdf", 2048, Noon, "s-v")),
                destination.Value,
                TransferQueueOperation.Copy);
            Assert.True(plan.IsSuccess, plan.IsFailure ? plan.Error.Message : null);
            Assert.Equal(expected, Assert.Single(plan.Value.Requests).ExpectedDestinationEntityTag);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AFileOnThisPcIsCopiedButNotMoved()
    {
        var local = Context(PaneTransferContextKind.ThisPc, null, null, Rooted("Data"));
        var item = Item("a.txt", Rooted(@"Data\a.txt"), 5, Noon, versionId: null).WithLocalIdentity();
        Assert.True(item.HasStableIdentity);

        var plan = new ManualTransferController(new FakeTransferClient()).BuildPlan(
            Selection(local, item),
            Destination(Saved(Guid.NewGuid(), "destination-root", "destination")),
            TransferQueueOperation.Move);

        Assert.True(plan.IsFailure);
        Assert.Equal("manual_transfer.move.local_source_unsupported", plan.Error.Code);
    }

    private static StorageResult<ManualTransferPlan> Plan(TransferConflictChoice choice)
    {
        var selection = Selection(
            Saved(Guid.NewGuid(), "source-root", "source"),
            Item("a.txt", "source/a.txt", 5, Noon, "s-a"),
            Item("new.txt", "source/new.txt", 5, Noon, "s-new"));
        var destination = Destination(
            Saved(Guid.NewGuid(), "destination-root", "destination"),
            Item("a.txt", "destination/a.txt", 5, Noon, "d-a"));
        return new ManualTransferController(new FakeTransferClient())
            .BuildPlan(selection, destination, TransferQueueOperation.Copy, conflicts: choice);
    }

    private static PaneTransferContext Saved(Guid connectionId, string rootIdentity, string path) =>
        Context(PaneTransferContextKind.SavedConnection, connectionId, rootIdentity, path);

    private static PaneTransferContext Context(
        PaneTransferContextKind kind,
        Guid? connectionId,
        string? rootIdentity,
        string path) => PaneTransferContext.Create(kind, connectionId, rootIdentity, path).Value;

    private static PaneTransferItem Item(
        string name,
        string path,
        long? length,
        DateTimeOffset? modified,
        string? versionId) =>
        PaneTransferItem.Create(name, path, StorageItemKind.File, length, versionId: versionId, modifiedUtc: modified).Value;

    private static PaneSelectionSnapshot Selection(PaneTransferContext context, params PaneTransferItem[] items) =>
        PaneSelectionSnapshot.Create(context, items).Value;

    private static PaneDestinationSnapshot Destination(PaneTransferContext context, params PaneTransferItem[] items) =>
        PaneDestinationSnapshot.Create(context, items).Value;

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
