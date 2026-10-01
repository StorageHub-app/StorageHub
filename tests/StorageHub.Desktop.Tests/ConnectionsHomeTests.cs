using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Connections Home: the saved connections as rows, each opening in the pane, as 1.x's second pane showed.
/// </summary>
public class ConnectionsHomeTests
{
    [AvaloniaFact]
    public async Task ItListsTheSavedConnectionsAndOpeningOneOpensIt()
    {
        var studio = WorkspaceFakes.Summary("Studio Assets");
        var agent = new WorkspaceFakes.FakeBrowsingAgent([studio]);
        await using var pane = new BrowserPaneModel(agent);
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);

        await pane.OpenAsync(
            pane.Connections.Single(static c => c.Kind == PaneContentKind.ConnectionsHome),
            TestContext.Current.CancellationToken);

        // As 1.x's ref 05 shows it: "Connections" in the address and as the tree's one node, a
        // connection's provider as its type and Saved or Favorite as its status, and the state
        // line asking for a connection to be chosen.
        Assert.Equal(Ui.Pane.Connections, pane.Path);
        Assert.Equal(Ui.Pane.Connections, Assert.Single(pane.Tree.Roots).Name);
        Assert.Equal((Ui.Pane.StateChooseConnection, false), (pane.ConnectionState, pane.ConnectionStateIsReady));
        var row = Assert.Single(pane.Rows);
        Assert.Equal(("Studio Assets", "S3", Ui.Pane.Saved), (row.Name, row.Type, row.Status));

        // Not a place files live, so nothing can be pasted or made here.
        Assert.False(pane.NewFolderCommand.CanExecute(null));

        pane.Selected = row;
        await pane.OpenSelectedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(studio.ConnectionId, pane.Connection?.Id);
        Assert.Equal("Studio Assets", pane.Title);
    }

    [AvaloniaFact]
    public async Task WithNothingSavedItSaysHowToAddOne()
    {
        await using var pane = new BrowserPaneModel(new WorkspaceFakes.FakeBrowsingAgent([]));
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenAsync(
            pane.Connections.Single(static c => c.Kind == PaneContentKind.ConnectionsHome),
            TestContext.Current.CancellationToken);

        Assert.Contains(Ui.Pane.UseManageToAddOne, pane.EmptyNotice, StringComparison.Ordinal);
    }

    /// <summary>The first pane opens on This PC, and the others on Connections Home, as in 1.x.</summary>
    /// <remarks>
    /// Unless it has been given a connection by then, which is what "Open in new pane" does: the
    /// split starts the new pane reading its list, and the connection is opened in it straight
    /// away. The connection waits for the list, as 1.x's did, where it used to fail at once as no
    /// longer saved and leave an empty warning once the list arrived.
    /// </remarks>
    [AvaloniaFact]
    public async Task ASecondPaneOpensOnConnectionsHome()
    {
        await using var pane = new BrowserPaneModel(new WorkspaceFakes.FakeBrowsingAgent([])) { PaneNumber = 2 };
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PaneContentKind.ConnectionsHome, pane.Connection?.Kind);

        var studio = WorkspaceFakes.Summary("Studio Assets");
        var list = new TaskCompletionSource();
        var agent = new WorkspaceFakes.FakeBrowsingAgent([studio]) { HeldList = list.Task };
        agent.Listings[(studio.ConnectionId, "")] = new WorkspaceFakes.Page([WorkspaceFakes.Entry("render.exr", 1024)]);
        await using var split = new BrowserPaneModel(agent) { PaneNumber = 2 };
        var reading = split.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        var opening = split.OpenConnectionAsync(studio.ConnectionId, TestContext.Current.CancellationToken);
        list.SetResult();
        await Task.WhenAll(reading, opening);

        Assert.Equal("Studio Assets", split.Title);
        Assert.Equal(["render.exr"], split.Rows.Select(row => row.Name));
        Assert.False(split.StatusIsWarning);
        Assert.False(split.ShowsLoading);
    }
}
