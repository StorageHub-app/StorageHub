using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Welcome's Recent connections, which lists what panes opened first, and opening a connection in a
/// new pane beside the active one -- both 1.x's.
/// </summary>
public class RecentAndNewPaneTests
{
    [AvaloniaFact]
    public async Task AConnectionOpenedThisSessionIsListedFirst()
    {
        var alpha = WorkspaceFakes.Summary("Alpha");
        var zulu = WorkspaceFakes.Summary("Zulu");
        var overview = OverviewModel.ForAgent(
            ShellStatusSnapshot.Initial,
            () => new WorkspaceFakes.FakeBrowsingAgent([alpha, zulu]),
            static () => new WorkspaceFakes.FakeTransferQueue());
        await overview.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Alpha", "Zulu"], overview.RecentConnections.Select(static row => row.Name));

        overview.RecordRecent(zulu.ConnectionId);

        Assert.Equal(["Zulu", "Alpha"], overview.RecentConnections.Select(static row => row.Name));
    }

    [AvaloniaFact]
    public void SplittingTheActivePaneHandsBackTheNewOne()
    {
        var workspace = ShellPreview.CreateOnWorkspace().Workspaces.Last(static tab => tab.IsClosable).Workspace!;
        var before = workspace.Panes.Count;

        var added = workspace.SplitActive(WorkspaceDockEdge.Right);

        Assert.NotNull(added);
        Assert.Equal(before + 1, workspace.Panes.Count);
        Assert.Contains(added, workspace.Panes);

        // At four there is no room for a fifth.
        while (workspace.Panes.Count < WorkspaceLayoutModel.MaximumPanes) workspace.SplitActive(WorkspaceDockEdge.Bottom);
        Assert.Null(workspace.SplitActive(WorkspaceDockEdge.Right));
    }

    [AvaloniaFact]
    public void AConnectionCardOffersOpenOpenInNewPaneEditAndDelete()
    {
        var sidebar = ShellPreview.CreateOnWorkspace().Sidebar;
        var row = new ConnectionRowModel(ConnectionCardFactory.Create(WorkspaceFakes.Summary("Studio")));

        var labels = sidebar.ContextEntriesFor(row).Select(static entry => entry.Label).ToArray();

        Assert.Equal(
            [Localization.Ui.Connections.ContextOpen, Localization.Ui.Connections.ContextOpenInNewPane,
             Localization.Ui.Connections.ContextEdit, Localization.Ui.Connections.ContextDelete],
            labels);
    }
}
