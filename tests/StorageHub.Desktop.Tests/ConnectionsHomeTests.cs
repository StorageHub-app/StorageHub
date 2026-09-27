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

        Assert.Equal(Ui.Pane.ConnectionsHome, pane.Path);
        var row = Assert.Single(pane.Rows);
        Assert.Equal("Studio Assets", row.Name);

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
    [AvaloniaFact]
    public async Task ASecondPaneOpensOnConnectionsHome()
    {
        await using var pane = new BrowserPaneModel(new WorkspaceFakes.FakeBrowsingAgent([])) { PaneNumber = 2 };
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PaneContentKind.ConnectionsHome, pane.Connection?.Kind);
    }
}
