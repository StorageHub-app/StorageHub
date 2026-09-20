using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Views;
using Xunit;
using static StorageHub.Desktop.Avalonia.Tests.WorkspaceFakes;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// One to four panes, in all six arrangements, and what a transfer means in each.
/// </summary>
/// <remarks>
/// <para>
/// The arrangements themselves are <see cref="WorkspaceLayoutModel"/>'s, which Desktop.Core
/// already tests as a tree. What is tested here is the half that only exists once there are pane
/// view models in the leaves: that growing an arrangement keeps the connections that were already
/// open, that shrinking one closes only the panes that lost their leaf, and that staging and
/// pasting still name exactly one pane each when there are four to choose from.
/// </para>
/// <para>
/// That last one is the reason the two-pane shortcut had to go. "Copy to the other pane" is a
/// sentence with no meaning at four, and a shell that quietly picked one would be worse than one
/// that asked.
/// </para>
/// </remarks>
public class WorkspacePaneLayoutTests
{
    /// <summary>Six arrangements: one pane, two either way, three either way, and a grid.</summary>
    [AvaloniaFact]
    public void TheArrangementsOfferedAreOneToFourPanes()
    {
        var counts = WorkspaceModel.Presets.Select(static preset => preset.PaneCount).ToArray();

        Assert.Equal(6, counts.Length);
        Assert.Equal([1, 2, 2, 3, 3, 4], counts);
        Assert.Equal(WorkspaceLayoutModel.MaximumPanes, counts.Max());
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task EveryArrangementBuildsThePanesItPromises(int index)
    {
        var preset = WorkspaceModel.Presets[index];
        await using var workspace = Create();

        workspace.Preset = preset;

        Assert.Equal(preset.PaneCount, workspace.Panes.Count);
        Assert.Equal(preset.PaneCount, workspace.Layout.PaneCount);
        Assert.Equal(preset.PaneCount, workspace.Panes.Distinct().Count());
    }

    /// <summary>
    /// Growing an arrangement keeps the panes that were already open.
    /// </summary>
    /// <remarks>
    /// Somebody who has two connections open and wants a third has not asked for the first two to
    /// be reconnected. Rebuilding every pane would make the layout buttons cost two round trips
    /// each, which is how a control ends up being pressed once and never again.
    /// </remarks>
    [AvaloniaFact]
    public async Task GrowingTheArrangementKeepsTheOpenPanes()
    {
        await using var workspace = Create();
        var before = workspace.Panes.ToArray();

        workspace.Preset = WorkspaceModel.Presets[5];

        Assert.Equal(4, workspace.Panes.Count);
        Assert.Same(before[0], workspace.Panes[0]);
        Assert.Same(before[1], workspace.Panes[1]);
    }

    /// <summary>Shrinking closes only the panes that lost their leaf.</summary>
    [AvaloniaFact]
    public async Task ShrinkingTheArrangementKeepsTheFirstPanes()
    {
        await using var workspace = Create();
        workspace.Preset = WorkspaceModel.Presets[5];
        var before = workspace.Panes.ToArray();

        workspace.Preset = WorkspaceModel.Presets[1];

        Assert.Equal(2, workspace.Panes.Count);
        Assert.Same(before[0], workspace.Panes[0]);
        Assert.Same(before[1], workspace.Panes[1]);
    }

    /// <summary>
    /// However many panes there are, exactly one is active.
    /// </summary>
    /// <remarks>
    /// The invariant every transfer command rests on. With two panes a bug here is survivable
    /// because the other pane is the destination either way; with four it would mean a paste
    /// landing somewhere nobody chose.
    /// </remarks>
    [AvaloniaFact]
    public async Task ExactlyOnePaneIsActiveAtAnyPaneCount()
    {
        await using var workspace = Create();
        workspace.Preset = WorkspaceModel.Presets[5];

        Assert.Single(workspace.Panes, static pane => pane.IsActive);

        workspace.Panes[2].IsActive = true;

        Assert.Same(workspace.Panes[2], workspace.Active);
        Assert.Single(workspace.Panes, static pane => pane.IsActive);
    }

    /// <summary>The four-pane arrangement is a grid: two stacks side by side.</summary>
    [AvaloniaFact]
    public async Task TheGridIsTwoStacksSideBySide()
    {
        await using var workspace = Create();
        workspace.Preset = WorkspaceModel.Presets[5];

        var root = Assert.IsType<WorkspaceSplitNode>(workspace.Layout.Root);
        Assert.Equal(WorkspaceSplitOrientation.Vertical, root.Orientation);
        foreach (var half in new[] { root.First, root.Second })
        {
            var column = Assert.IsType<WorkspaceSplitNode>(half);
            Assert.Equal(WorkspaceSplitOrientation.Horizontal, column.Orientation);
            Assert.IsType<WorkspacePaneLeaf>(column.First);
            Assert.IsType<WorkspacePaneLeaf>(column.Second);
        }
    }

    /// <summary>Closing a pane leaves the rest where they were.</summary>
    [AvaloniaFact]
    public async Task ClosingAPaneLeavesTheOthers()
    {
        await using var workspace = Create();
        workspace.Preset = WorkspaceModel.Presets[3];
        var kept = new[] { workspace.Panes[0], workspace.Panes[2] };

        workspace.ClosePane(workspace.Panes[1]);

        Assert.Equal(2, workspace.Panes.Count);
        Assert.Equal(kept, workspace.Panes);
    }

    /// <summary>The last pane cannot be closed, because a workspace with none is not one.</summary>
    [AvaloniaFact]
    public async Task TheLastPaneStays()
    {
        await using var workspace = Create();
        workspace.Preset = WorkspaceModel.Presets[0];

        workspace.ClosePane(workspace.Panes[0]);

        Assert.Single(workspace.Panes);
    }

    /// <summary>
    /// With four panes, a copy goes from the pane it was staged in to the pane it was pasted in.
    /// </summary>
    /// <remarks>
    /// The case the whole staging model exists for. Three of the four panes are plausible
    /// destinations and the workspace picks none of them: it waits to be told, and the one it is
    /// told is the one that receives.
    /// </remarks>
    [AvaloniaFact]
    public async Task AcrossFourPanesTheStagedItemsLandWhereTheyArePasted()
    {
        var connections = new[]
        {
            Summary("Studio Assets"), Summary("Site Backups"), Summary("Archive"), Summary("Scratch")
        };
        var agent = new FakeBrowsingAgent(connections);
        agent.Listings[(connections[0].ConnectionId, "")] = new Page([Entry("render.exr", 1024)]);
        var queue = new FakeTransferQueue();

        await using var workspace = new WorkspaceModel(
            () => new BrowserPaneModel(agent),
            () => queue,
            () => agent,
            static () => new FakeInspector(),
            preset: WorkspaceModel.Presets[5]);

        for (var index = 0; index < workspace.Panes.Count; index++)
        {
            await workspace.Panes[index].LoadConnectionsAsync(TestContext.Current.CancellationToken);
            await workspace.Panes[index].OpenConnectionAsync(
                connections[index].ConnectionId, TestContext.Current.CancellationToken);
        }

        workspace.Panes[0].IsActive = true;
        workspace.Panes[0].SelectedRows.Add(workspace.Panes[0].Rows[0]);
        workspace.Stage(TransferQueueOperation.Copy);

        Assert.True(workspace.HasClipboard);

        // Three panes could take it. The one that is active when paste runs is the one that does.
        workspace.Panes[3].IsActive = true;
        await workspace.PasteAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(queue.Enqueued);
        Assert.Equal(connections[0].ConnectionId, request.Source.ConnectionId);
        Assert.Equal(connections[3].ConnectionId, request.Destination.ConnectionId);
    }

    /// <summary>
    /// A staged copy survives its first paste, so it can be pasted into a second pane.
    /// </summary>
    /// <remarks>
    /// The other thing four panes make worth having: one selection sent to three destinations
    /// without going back to the source each time. A move is spent instead, because the originals
    /// are gone after the first one.
    /// </remarks>
    [AvaloniaFact]
    public async Task ACopyCanBePastedIntoMoreThanOnePane()
    {
        var connections = new[] { Summary("Studio Assets"), Summary("Site Backups"), Summary("Archive") };
        var agent = new FakeBrowsingAgent(connections);
        agent.Listings[(connections[0].ConnectionId, "")] = new Page([Entry("render.exr", 1024)]);
        var queue = new FakeTransferQueue();

        await using var workspace = new WorkspaceModel(
            () => new BrowserPaneModel(agent),
            () => queue,
            () => agent,
            static () => new FakeInspector(),
            preset: WorkspaceModel.Presets[3]);

        for (var index = 0; index < workspace.Panes.Count; index++)
        {
            await workspace.Panes[index].LoadConnectionsAsync(TestContext.Current.CancellationToken);
            await workspace.Panes[index].OpenConnectionAsync(
                connections[index].ConnectionId, TestContext.Current.CancellationToken);
        }

        workspace.Panes[0].IsActive = true;
        workspace.Panes[0].SelectedRows.Add(workspace.Panes[0].Rows[0]);
        workspace.Stage(TransferQueueOperation.Copy);

        workspace.Panes[1].IsActive = true;
        await workspace.PasteAsync(TestContext.Current.CancellationToken);
        workspace.Panes[2].IsActive = true;
        await workspace.PasteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, queue.Enqueued.Count);
        Assert.Equal(
            [connections[1].ConnectionId, connections[2].ConnectionId],
            queue.Enqueued.Select(static request => request.Destination.ConnectionId));
        Assert.True(workspace.HasClipboard);
    }

    /// <summary>
    /// An SSH client makes a pane a terminal, and a terminal is not a transfer endpoint.
    /// </summary>
    /// <remarks>
    /// SFTP and SSH are the same protocol and different panes. The profile type separates them,
    /// so a saved SSH profile opens a shell here and a file listing there, which is what 1.x did
    /// and what the connection picker already implies by offering both.
    /// </remarks>
    [AvaloniaFact]
    public async Task AnSshClientPaneIsATerminalAndNotATransferEndpoint()
    {
        var storage = Summary("Studio Assets");
        var shell = Summary("build-box", StorageConnectionProvider.Ssh, ConnectionProfileType.Client);
        var agent = new FakeBrowsingAgent([storage, shell]);
        agent.Listings[(storage.ConnectionId, "")] = new Page([Entry("render.exr", 1024)]);

        await using var workspace = new WorkspaceModel(
            () => new BrowserPaneModel(agent),
            static () => new FakeTransferQueue(),
            () => agent,
            static () => new FakeInspector());

        foreach (var pane in workspace.Panes)
        {
            await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        }

        await workspace.Panes[0].OpenConnectionAsync(
            storage.ConnectionId, TestContext.Current.CancellationToken);
        await workspace.Panes[1].OpenConnectionAsync(
            shell.ConnectionId, TestContext.Current.CancellationToken);

        Assert.True(workspace.Panes[0].IsListing);
        Assert.True(workspace.Panes[1].IsTerminal);
        Assert.Equal(PaneContentKind.SshClient, workspace.Panes[1].ContentKind);

        // Nothing to stage from a shell, and nowhere in one to paste to.
        workspace.Panes[1].IsActive = true;
        Assert.False(workspace.CanStage(TransferQueueOperation.Copy));

        workspace.Panes[0].IsActive = true;
        workspace.Panes[0].SelectedRows.Add(workspace.Panes[0].Rows[0]);
        workspace.Stage(TransferQueueOperation.Copy);
        workspace.Panes[1].IsActive = true;

        Assert.False(workspace.CanPaste);
    }

    /// <summary>Pointing a pane back at storage turns it from a terminal into a listing again.</summary>
    [AvaloniaFact]
    public async Task APaneCanGoFromATerminalBackToAListing()
    {
        var storage = Summary("Studio Assets");
        var shell = Summary("build-box", StorageConnectionProvider.Ssh, ConnectionProfileType.Client);
        var agent = new FakeBrowsingAgent([storage, shell]);
        agent.Listings[(storage.ConnectionId, "")] = new Page([Entry("render.exr", 1024)]);

        await using var workspace = new WorkspaceModel(
            () => new BrowserPaneModel(agent),
            static () => new FakeTransferQueue(),
            () => agent,
            static () => new FakeInspector());
        var pane = workspace.Panes[0];
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);

        await pane.OpenConnectionAsync(shell.ConnectionId, TestContext.Current.CancellationToken);
        Assert.True(pane.IsTerminal);
        Assert.Empty(pane.Rows);

        await pane.OpenConnectionAsync(storage.ConnectionId, TestContext.Current.CancellationToken);

        Assert.True(pane.IsListing);
        Assert.Equal(["render.exr"], pane.Rows.Select(static row => row.Name));
    }

    /// <summary>A workspace with no agent behind it, for the tests that only need the shape.</summary>
    private static WorkspaceModel Create() => new(
        static () => new BrowserPaneModel(),
        static () => new FakeTransferQueue(),
        static () => new FakeBrowsingAgent([]),
        static () => new FakeInspector());
}
