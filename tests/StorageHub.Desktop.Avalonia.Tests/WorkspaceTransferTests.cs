using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Views;
using Xunit;
using static StorageHub.Desktop.Avalonia.Tests.WorkspaceFakes;

namespace StorageHub.Desktop.Avalonia.Tests;

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
        await using var fixture = await Fixture.CreateAsync();

        fixture.Left.SelectedRows.Add(fixture.Left.Rows.Single(row => row.Name == "render.exr"));
        await fixture.StageAndPasteAsync(TransferQueueOperation.Copy, fixture.Right);

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
        await fixture.StageAndPasteAsync(TransferQueueOperation.Copy, fixture.Right);

        Assert.Empty(fixture.Queue.Enqueued);
        Assert.True(fixture.Workspace.HasMessage);
    }

    [AvaloniaFact]
    public async Task AQueueThatIsNotThereLeavesAMessageRatherThanThrowing()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Queue.Throw = true;

        fixture.Left.SelectedRows.Add(fixture.Left.Rows[0]);
        await fixture.StageAndPasteAsync(TransferQueueOperation.Copy, fixture.Right);

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
        await fixture.StageAndPasteAsync(TransferQueueOperation.Copy, fixture.Right);

        Assert.Equal(1, refreshes);
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

        internal BrowserPaneModel Left => Workspace.Panes[0];

        internal BrowserPaneModel Right => Workspace.Panes[1];

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
            var workspace = new WorkspaceModel(
                () => new BrowserPaneModel(agent),
                () => queue,
                () => agent,
                () => new FakeInspector(),
                onQueueChanged);

            foreach (var pane in workspace.Panes)
            {
                await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
            }

            await workspace.Panes[0].OpenConnectionAsync(
                source.ConnectionId, TestContext.Current.CancellationToken);
            await workspace.Panes[1].OpenConnectionAsync(
                destination.ConnectionId, TestContext.Current.CancellationToken);
            workspace.Panes[0].IsActive = true;
            return new Fixture(workspace, queue, source.ConnectionId, destination.ConnectionId);
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
