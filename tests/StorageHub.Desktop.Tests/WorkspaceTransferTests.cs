using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Views;
using Xunit;
using static StorageHub.Desktop.Tests.WorkspaceFakes;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Copying between panes, end to end, without a window.
/// </summary>
/// <remarks>
/// The whole path is exercised: a selection staged in one pane, a listing in another, the snapshots
/// PaneTransferSnapshots builds from both, the plan ManualTransferController makes of them, and the
/// enqueue request that reaches the agent. In the WinForms shell that path ran through a 3,621-line
/// control and could only be checked by doing it by hand.
/// </remarks>
public class WorkspaceTransferTests
{
    [AvaloniaFact]
    public async Task CopyingSendsTheStagedSelectionToThePaneItIsPastedInto()
    {
        var dialogs = new KeyStoreTests.RecordingDialogs { Choice = DialogChoice.Ok };
        await using var fixture = await Fixture.CreateAsync(
            dialogs: dialogs, destinationProvider: StorageConnectionProvider.Sftp);

        fixture.Left.SelectedRows.Add(fixture.Left.Rows.Single(row => row.Name == "render.exr"));
        await fixture.StageAndPasteAsync(TransferQueueOperation.Copy, fixture.Right);

        // The review says, in one line, that an SFTP destination is written in place.
        Assert.Equal(Ui.Dialogs.TransferNonAtomicNote, dialogs.LastRequest?.Detail);

        var request = Assert.Single(fixture.Queue.Enqueued);
        Assert.Equal(TransferQueueOperation.Copy, request.Operation);
        Assert.Equal("render.exr", request.Source.RelativePath);
        Assert.Equal(fixture.SourceConnectionId, request.Source.ConnectionId);
        Assert.Equal(fixture.DestinationConnectionId, request.Destination.ConnectionId);
    }

    /// <summary>
    /// Staging takes from the pane that is active, and pasting puts into the pane that is active.
    /// </summary>
    /// <remarks>
    /// That is the whole rule, and it is the same sentence at one pane or at four. The two-pane
    /// shortcut -- the source is active, the destination is the other one -- reads well until a
    /// third pane exists and it stops naming anything, which is why the 1.x shell staged even with
    /// two. Worth a test because it is what makes the same three buttons mean different things
    /// depending on where somebody last clicked.
    /// </remarks>
    [AvaloniaFact]
    public async Task StagingTakesFromTheActivePaneAndPastingPutsIntoIt()
    {
        await using var fixture = await Fixture.CreateAsync();

        fixture.Right.IsActive = true;
        fixture.Right.SelectedRows.Add(fixture.Right.Rows.Single(row => row.Name == "archive.zip"));
        await fixture.StageAndPasteAsync(TransferQueueOperation.Move, fixture.Left);

        var request = Assert.Single(fixture.Queue.Enqueued);
        Assert.Equal(TransferQueueOperation.Move, request.Operation);
        Assert.Equal(fixture.DestinationConnectionId, request.Source.ConnectionId);
        Assert.Equal(fixture.SourceConnectionId, request.Destination.ConnectionId);

        // A pane's own Copy is enabled by its own selection, not the active pane's, and pressing
        // it makes that pane the active one before staging from it.
        fixture.Right.IsActive = true;
        fixture.Right.SelectedRows.Clear();
        fixture.Left.SelectedRows.Add(fixture.Left.Rows.Single(row => row.Name == "render.exr"));
        Assert.False(fixture.Right.CopyCommand!.CanExecute(null));
        Assert.True(fixture.Left.CopyCommand!.CanExecute(null));

        fixture.Left.CopyCommand.Execute(null);

        Assert.True(fixture.Left.IsActive);
        Assert.Equal("render.exr", fixture.Workspace.Clipboard!.ItemSummary);
    }

    [AvaloniaFact]
    public async Task ActivatingOnePaneDeactivatesTheOther()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.True(fixture.Left.IsActive);

        fixture.Right.IsActive = true;

        Assert.False(fixture.Left.IsActive);
        Assert.Same(fixture.Right, fixture.Workspace.Active);
    }

    [AvaloniaFact]
    public async Task NothingSelectedMeansNothingToCopy()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.False(fixture.Workspace.StageCopyCommand.CanExecute(null));

        fixture.Left.SelectedRows.Add(fixture.Left.Rows[0]);

        Assert.True(fixture.Workspace.StageCopyCommand.CanExecute(null));
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

        await fixture.StageAndPasteAsync(TransferQueueOperation.Copy, fixture.Right);

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

        await fixture.StageAndPasteAsync(TransferQueueOperation.Copy, fixture.Right);

        Assert.True(fixture.Queue.Enqueued.Count > 0, "refused: " + fixture.Workspace.Message);
        Assert.Equal(fixture.Left.Rows.Count, fixture.Queue.Enqueued.Count);
    }

    /// <summary>
    /// A destination still paging cannot be one, and saying so is 1.x's "Transfer queue" warning.
    /// </summary>
    /// <remarks>
    /// A collision is detected by comparing against what is already in the destination, so a
    /// listing that has not finished would report a file just past the last page as absent -- and
    /// the transfer would silently overwrite it. The refusal is a warning rather than eight seconds
    /// in the status bar, as every refused paste or drop was in 1.x, and is not said there too.
    /// </remarks>
    [AvaloniaFact]
    public async Task ADestinationThatHasNotFinishedListingIsRefusedInAWarning()
    {
        var dialogs = new KeyStoreTests.RecordingDialogs { Choice = DialogChoice.Ok };
        await using var fixture = await Fixture.CreateAsync(destinationHasMorePages: true, dialogs: dialogs);

        fixture.Left.SelectedRows.Add(fixture.Left.Rows[0]);
        await fixture.StageAndPasteAsync(TransferQueueOperation.Copy, fixture.Right);

        Assert.Empty(fixture.Queue.Enqueued);
        var warning = dialogs.LastRequest!;
        Assert.Equal(Ui.Dialogs.TransferQueueCaption, warning.Title);
        Assert.Equal(Ui.Shell.CouldNotFinishIndexing, warning.Message);
        Assert.Equal(DialogSeverity.Warning, warning.Severity);
        Assert.False(fixture.Workspace.HasMessage);
    }

    /// <summary>
    /// An agent that never answers leaves 1.x's sentence about the transfer it could not confirm,
    /// said in the status bar when there is nowhere to show a warning.
    /// </summary>
    [AvaloniaFact]
    public async Task AQueueThatIsNotThereLeavesAMessageRatherThanThrowing()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Queue.Throw = true;

        fixture.Left.SelectedRows.Add(fixture.Left.Rows[0]);
        await fixture.StageAndPasteAsync(TransferQueueOperation.Copy, fixture.Right);

        Assert.StartsWith(Ui.Format(Ui.Shell.AcknowledgedTransfersFormat, 0), fixture.Workspace.Message);
        Assert.EndsWith(Ui.Shell.CheckQueueForAmbiguousTransfers, fixture.Workspace.Message);
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
        await fixture.StageAndPasteAsync(TransferQueueOperation.Copy, fixture.Right);

        Assert.Equal(1, refreshes);
    }

    /// <summary>
    /// A drop lands in the pane it was dropped on, and leaves what is staged alone.
    /// </summary>
    /// <remarks>
    /// The same transfer a paste is, with the destination named by where the pointer let go. The
    /// staged clipboard is the other thing a person may be holding, and dragging one selection
    /// must not lose it.
    /// </remarks>
    [AvaloniaFact]
    public async Task ADropQueuesIntoThePaneItLandedOnAndKeepsWhatIsStaged()
    {
        await using var fixture = await Fixture.CreateAsync();

        // Something staged for a paste, to prove the drop does not disturb it.
        fixture.Left.SelectedRows.Add(fixture.Left.Rows.Single(row => row.Name == "reports"));
        fixture.Workspace.Stage(TransferQueueOperation.Copy);
        Assert.True(fixture.Workspace.HasClipboard);

        // And a different selection dragged, as the handler would carry it.
        fixture.Left.SelectedRows.Clear();
        fixture.Left.SelectedRows.Add(fixture.Left.Rows.Single(row => row.Name == "render.exr"));
        var payload = PaneDragHandler.Payload(fixture.Left);
        Assert.NotNull(payload);
        Assert.True(payload!.CanMove);

        Assert.True(fixture.Right.CanReceiveDrop);
        await fixture.Right.ReceiveDropAsync(
            new PaneClipboard(payload.Selection, TransferQueueOperation.Move, payload.SourceName));

        var request = Assert.Single(fixture.Queue.Enqueued);
        Assert.Equal(TransferQueueOperation.Move, request.Operation);
        Assert.Equal("render.exr", request.Source.RelativePath);
        Assert.Equal(fixture.DestinationConnectionId, request.Destination.ConnectionId);
        Assert.True(fixture.Workspace.HasClipboard);
        Assert.Equal("reports", fixture.Workspace.Clipboard!.Selection.Items[0].Name);
    }

    /// <summary>A folder cannot be moved, so a drag of one can only copy.</summary>
    [AvaloniaFact]
    public async Task ADraggedFolderCanOnlyBeCopied()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Left.SelectedRows.Add(fixture.Left.Rows.Single(row => row.Name == "reports"));

        var payload = PaneDragHandler.Payload(fixture.Left);

        Assert.NotNull(payload);
        Assert.False(payload!.CanMove);
    }

    /// <summary>A pane with nothing behind it, or a terminal, refuses drops; nothing selected is no drag.</summary>
    [AvaloniaFact]
    public async Task APaneWithNoQueueRefusesDropsAndNothingSelectedIsNoDrag()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Null(PaneDragHandler.Payload(fixture.Left));

        await using var lonely = new BrowserPaneModel(new FakeBrowsingAgent([]));
        Assert.False(lonely.CanReceiveDrop);
    }

    /// <summary>The payload a drag carries is found by its token while the drag lasts, and not after.</summary>
    [AvaloniaFact]
    public async Task ADragPayloadLivesForTheDrag()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Left.SelectedRows.Add(fixture.Left.Rows.Single(row => row.Name == "render.exr"));
        var payload = PaneDragHandler.Payload(fixture.Left)!;

        var token = PaneDragPayloads.Register(payload);
        Assert.Same(payload, PaneDragPayloads.Find(token));

        PaneDragPayloads.Release(token);
        Assert.Null(PaneDragPayloads.Find(token));
        Assert.Null(PaneDragPayloads.Find(null));
    }

    /// <summary>
    /// Files dropped in from the desktop go through the agent's review first, as 1.x's did: one
    /// already there is asked about (Yes replaces, No skips, Cancel stops) before anything is
    /// queued. A folder from another pane is read first, and while it is, the queue's Active tab
    /// shows the reading and how far it has got; Cancel there stops it, and what it had already
    /// queued stays queued.
    /// </summary>
    /// <remarks>
    /// Desktop files used to be queued at once, so one already at the destination was refused
    /// rather than asked about, and a desktop folder was refused outright.
    /// </remarks>
    [AvaloniaFact]
    public async Task ADesktopDropIsReviewedAndAFolderBeingReadShowsInTheQueueUntilCancelled()
    {
        var drops = new PendingDropRegistry();
        var dialogs = new KeyStoreTests.RecordingDialogs { Choice = DialogChoice.No };
        await using var fixture = await Fixture.CreateAsync(drops: drops, dialogs: dialogs);
        await using var queue = new TransferQueueModel(() => fixture.Queue) { PendingDrops = drops };

        // A folder and a file from the left pane, dropped on the right once the desktop files are in.
        fixture.Left.SelectedRows.Add(fixture.Left.Rows.Single(row => row.Name == "reports"));
        fixture.Left.SelectedRows.Add(fixture.Left.Rows.Single(row => row.Name == "render.exr"));
        var payload = PaneDragHandler.Payload(fixture.Left)!;

        // The agent finds a folder, a new file and one already there; No skips that one.
        fixture.Queue.PlanItems =
        [
            new ShellImportItem("shots", true, null, false),
            new ShellImportItem("shots/new.png", false, 10, false),
            new ShellImportItem("shots/archive.zip", false, 20, true)
        ];
        await fixture.Right.ReceiveFilesAsync([@"C:\Users\me\Desktop\shots"]);

        var planned = Assert.Single(fixture.Queue.Planned);
        Assert.Equal([@"C:\Users\me\Desktop\shots"], planned.SourcePaths);
        Assert.Equal(fixture.DestinationConnectionId, planned.Destination.ConnectionId);
        Assert.Equal(Ui.Dialogs.ImportConflictsCaption, dialogs.LastRequest?.Title);
        Assert.Equal(DialogButtons.YesNoCancel, dialogs.LastRequest?.Buttons);
        Assert.Equal(ShellImportDisposition.SkipConflictingFiles, Assert.Single(fixture.Queue.Imported).Disposition);
        Assert.Equal(Ui.Format(Ui.Shell.QueuedExplorerImportFormat, 1), fixture.Workspace.Message);

        // Nothing in the way is OK or Cancel; dropped on a folder in the tree, it goes there.
        fixture.Queue.PlanItems = [new ShellImportItem("new.png", false, 10, false)];
        dialogs.Choice = DialogChoice.Cancel;
        await fixture.Right.ReceiveFilesAsync([@"C:\Users\me\Desktop\new.png"], "incoming");
        Assert.Equal("incoming", fixture.Queue.Planned[^1].Destination.RelativePath);
        Assert.Equal(Ui.Dialogs.ImportFromExplorerCaption, dialogs.LastRequest?.Title);
        Assert.Equal(ShellImportDisposition.Cancel, fixture.Queue.Imported[^1].Disposition);
        Assert.Empty(fixture.Queue.Enqueued);
        Assert.Empty(drops.Snapshot());
        dialogs.Choice = DialogChoice.Ok;

        // The folder's listing is held open, so the reading is caught part way.
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Agent.BeforeListing = async (request, cancellationToken) =>
        {
            if (!request.Recursive || request.ConnectionId != fixture.SourceConnectionId) return;
            reading.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        };
        var dropping = fixture.Right.ReceiveDropAsync(
            new PaneClipboard(payload.Selection, TransferQueueOperation.Copy, payload.SourceName));
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await queue.RefreshAsync();
        var row = Assert.Single(queue.Rows);
        Assert.Equal(Ui.Format(Ui.Transfer.DropItemsFromFormat, 2, "/"), row.Source);
        Assert.Equal("/", row.Destination);
        Assert.Equal(Ui.Format(Ui.Transfer.DropGatheringFormat, 1, 0), row.Status);

        queue.Selected = row;
        Assert.True(queue.CancelCommand.CanExecute(null));
        queue.CancelCommand.Execute(null);
        await dropping.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // Stopped rather than refused, so it is said and not put up as a warning, in 1.x's words,
        // the file queued before the folder was read counted.
        Assert.Equal(
            Ui.Format(Ui.Shell.PartiallyQueuedFormat, 1, Ui.Validation.ReadingTheFolderWasStopped),
            fixture.Workspace.Message);
        Assert.NotEqual(Ui.Dialogs.TransferQueueCaption, dialogs.LastRequest?.Title);
        Assert.Equal(
            ["render.exr"],
            fixture.Queue.Enqueued.Select(static request => request.Source.RelativePath));
        await queue.RefreshAsync();
        row = Assert.Single(queue.Rows);
        Assert.Equal(Ui.Format(Ui.Transfer.DropCancelledFormat, Ui.Validation.ReadingTheFolderWasStopped), row.Status);
        Assert.False(queue.CancelCommand.CanExecute(null));
    }

    /// <summary>
    /// A connection's rows dragged out to Explorer wait on the queue's Active tab until the agent
    /// says where they landed, then read as queued there; one that lands on a StorageHub pane
    /// instead leaves no row. Without the drop broker, or off Windows, the drag carries nothing
    /// out and says why once it ends, as 1.x said it.
    /// </summary>
    [AvaloniaFact]
    public async Task ADragOutWaitsInTheQueueUntilTheAgentSaysWhereItLanded()
    {
        var drops = new PendingDropRegistry();
        var markers = Path.Combine(Path.GetTempPath(), $"storagehub-markers-{Guid.NewGuid():N}");
        var registered = true;
        await using var fixture = await Fixture.CreateAsync(
            drops: drops,
            dragOut: queue => new ExplorerDragOut(() => queue, drops, () => registered, markers));
        try
        {
            fixture.Left.SelectedRows.Add(fixture.Left.Rows.Single(row => row.Name == "render.exr"));
            var selection = PaneDragHandler.Payload(fixture.Left)!.Selection;

            var drag = fixture.Left.StartDragOut(selection);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Null(drag.MarkerPath);
                Assert.Equal(Ui.Pane.DragOutNeedsExplorer, await drag.FinishAsync(landedInStorageHub: false));
                Assert.Empty(drops.Snapshot());
                return;
            }

            Assert.True(Directory.Exists(drag.MarkerPath));
            var waiting = Assert.Single(drops.Snapshot());
            Assert.Equal(PendingDropState.AwaitingDestination, waiting.State);
            Assert.Equal("render.exr", waiting.Source);

            fixture.Queue.ExplorerDestination = @"C:\Users\me\Desktop";
            Assert.Equal(
                Ui.Format(Ui.Pane.QueuedToFormat, @"C:\Users\me\Desktop"),
                await drag.FinishAsync(landedInStorageHub: false));
            var source = Assert.Single(Assert.Single(fixture.Queue.DragsOut).Sources);
            Assert.Equal(fixture.SourceConnectionId, source.Address.ConnectionId);
            Assert.Equal("render.exr", source.Address.RelativePath);
            Assert.Equal(PendingDropState.Queued, Assert.Single(drops.Snapshot()).State);

            // Landed on a pane: that pane's transfer, and no row of its own.
            var landed = fixture.Left.StartDragOut(selection);
            Assert.Equal(2, drops.Snapshot().Count);
            Assert.Null(await landed.FinishAsync(landedInStorageHub: true));
            Assert.Equal(PendingDropState.Queued, Assert.Single(drops.Snapshot()).State);

            registered = false;
            var unavailable = fixture.Left.StartDragOut(selection);
            Assert.Null(unavailable.MarkerPath);
            Assert.Equal(Ui.Shell.ExplorerIntegrationUnavailable, await unavailable.FinishAsync(landedInStorageHub: false));
            Assert.Null(await unavailable.FinishAsync(landedInStorageHub: false));
        }
        finally
        {
            if (Directory.Exists(markers)) Directory.Delete(markers, recursive: true);
        }
    }

    /// <summary>Two panes, two connections, and a queue that records what it was asked.</summary>
    /// <remarks>
    /// Two because that is the default arrangement, not because the workspace is limited to it --
    /// <see cref="WorkspacePaneLayoutTests"/> drives the other five.
    /// </remarks>
    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            WorkspaceModel workspace,
            FakeTransferQueue queue,
            FakeBrowsingAgent agent,
            Guid source,
            Guid destination)
        {
            Workspace = workspace;
            Queue = queue;
            Agent = agent;
            SourceConnectionId = source;
            DestinationConnectionId = destination;
        }

        internal WorkspaceModel Workspace { get; }

        internal FakeTransferQueue Queue { get; }

        internal FakeBrowsingAgent Agent { get; }

        internal BrowserPaneModel Left => Workspace.Panes[0];

        internal BrowserPaneModel Right => Workspace.Panes[1];

        internal Guid SourceConnectionId { get; }

        internal Guid DestinationConnectionId { get; }

        internal static async Task<Fixture> CreateAsync(
            bool destinationHasMorePages = false,
            Func<Task>? onQueueChanged = null,
            PendingDropRegistry? drops = null,
            IDialogService? dialogs = null,
            Func<FakeTransferQueue, ExplorerDragOut>? dragOut = null,
            StorageConnectionProvider destinationProvider = StorageConnectionProvider.S3)
        {
            var source = Summary("Studio Assets");
            var destination = Summary("Site Backups", destinationProvider);

            var agent = new FakeBrowsingAgent([source, destination]);
            agent.Listings[(source.ConnectionId, "")] =
                new Page([Entry("reports", container: true), Entry("render.exr", 1024)]);
            agent.Listings[(source.ConnectionId, "reports")] =
                new Page([Entry("q1.pdf", 2048, parent: "reports")]);
            agent.Listings[(destination.ConnectionId, "")] =
                new Page([Entry("archive.zip", 4096)], destinationHasMorePages ? "more" : null);

            var queue = new FakeTransferQueue();
            var workspace = new WorkspaceModel(
                () => new BrowserPaneModel(agent),
                () => queue,
                () => agent,
                () => new FakeInspector(),
                onQueueChanged,
                dialogs: dialogs)
            {
                PendingDrops = drops,
                DragOut = dragOut?.Invoke(queue)
            };

            foreach (var pane in workspace.Panes)
            {
                await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
            }

            await workspace.Panes[0].OpenConnectionAsync(
                source.ConnectionId, TestContext.Current.CancellationToken);
            await workspace.Panes[1].OpenConnectionAsync(
                destination.ConnectionId, TestContext.Current.CancellationToken);
            workspace.Panes[0].IsActive = true;
            return new Fixture(workspace, queue, agent, source.ConnectionId, destination.ConnectionId);
        }

        /// <summary>
        /// Stages what is selected in the active pane, then pastes it into the one given.
        /// </summary>
        /// <remarks>
        /// Two steps rather than one call, because two steps is what somebody does: select, press
        /// copy, click the pane they want it in, press paste. Writing it as one helper keeps each
        /// test about what was transferred rather than about the gesture.
        /// </remarks>
        internal async Task StageAndPasteAsync(
            TransferQueueOperation operation,
            BrowserPaneModel destination)
        {
            Workspace.Stage(operation);
            destination.IsActive = true;
            await Workspace.PasteAsync(TestContext.Current.CancellationToken);
        }

        public ValueTask DisposeAsync() => Workspace.DisposeAsync();
    }
}
