using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The folder tree beside a pane's list, which 1.x had and 2.0 had not yet ported.
/// </summary>
public class PaneTreeTests
{
    private static BrowserListItem Folder(string name, string location) =>
        new(name, "", "", "", "", Location: location, IsContainer: true);

    private static BrowserListItem File(string name, string location) =>
        new(name, "1 B", "", "", "", Location: location);

    [Fact]
    public void TheWayDownToTheCurrentFolderIsThereAndOpen()
    {
        var tree = new PaneTreeModel();

        tree.Follow("bucket", "Studio Assets", local: false, "campaigns/2026",
            [Folder("final", "campaigns/2026/final"), File("brief.pdf", "campaigns/2026/brief.pdf")]);

        var root = Assert.Single(tree.Roots);
        Assert.Equal("Studio Assets", root.Name);
        var campaigns = Assert.Single(root.Children);
        var year = Assert.Single(campaigns.Children);
        Assert.Equal("2026", year.Name);
        Assert.True(campaigns.IsExpanded && year.IsExpanded);
        Assert.True(year.IsSelected);

        // Only folders become nodes.
        Assert.Equal(["final"], year.Children.Select(static child => child.Name));
    }

    /// <summary>A folder passed through stays, with what is under it, as in 1.x.</summary>
    [Fact]
    public void FoldersAlreadySeenStayInTheTree()
    {
        var tree = new PaneTreeModel();
        tree.Follow("bucket", "B", false, "", [Folder("a", "a"), Folder("b", "b")]);
        tree.Follow("bucket", "B", false, "a", [Folder("deep", "a/deep")]);
        tree.Follow("bucket", "B", false, "", [Folder("a", "a"), Folder("b", "b")]);

        var root = tree.Roots[0];
        Assert.Equal(["a", "b"], root.Children.Select(static child => child.Name));
        Assert.Equal(["deep"], root.Children[0].Children.Select(static child => child.Name));
        Assert.True(root.IsSelected);
    }

    [Fact]
    public void AFolderThatIsGoneLeavesTheTreeAndANewPageOnlyAdds()
    {
        var tree = new PaneTreeModel();
        tree.Follow("bucket", "B", false, "", [Folder("a", "a"), Folder("b", "b")]);
        tree.Follow("bucket", "B", false, "", [Folder("b", "b")]);
        Assert.Equal(["b"], tree.Roots[0].Children.Select(static child => child.Name));

        tree.Follow("bucket", "B", false, "", [Folder("c", "c")], append: true);
        Assert.Equal(["b", "c"], tree.Roots[0].Children.Select(static child => child.Name));
    }

    [Fact]
    public void AnotherConnectionStartsANewTree()
    {
        var tree = new PaneTreeModel();
        tree.Follow("one", "One", false, "", [Folder("a", "a")]);
        tree.Follow("two", "Two", false, "", [Folder("z", "z")]);

        Assert.Equal("Two", Assert.Single(tree.Roots).Name);
        Assert.Equal(["z"], tree.Roots[0].Children.Select(static child => child.Name));
    }

    /// <summary>
    /// A path typed in another case than the listing gave is the same folder on this computer, so
    /// "c:\temp" opens under the "C:\" drive the tree already has instead of growing a second one.
    /// </summary>
    [Fact]
    public void ALocalPathInAnotherCaseFindsTheDriveAlreadyThere()
    {
        if (!OperatingSystem.IsWindows()) return;
        var tree = new PaneTreeModel();
        tree.Follow("pc", "This PC", local: true, "", [Folder(@"C:\", @"C:\")]);
        tree.Follow("pc", "This PC", local: true, @"c:\temp", [Folder("inside", @"c:\temp\inside")]);

        var drive = Assert.Single(tree.Roots[0].Children);
        Assert.Equal(@"C:\", drive.Target);
        Assert.Equal(["temp"], drive.Children.Select(static child => child.Name));
    }

    /// <summary>Choosing a node asks to go there; the tree following a listing does not.</summary>
    [Fact]
    public void OnlyAChoiceAsksToGoSomewhere()
    {
        var tree = new PaneTreeModel();
        var asked = new List<string>();
        tree.NavigateRequested += (_, target) => asked.Add(target);

        tree.Follow("bucket", "B", false, "", [Folder("a", "a")]);
        Assert.Empty(asked);

        tree.Chosen(tree.Roots[0].Children[0]);
        Assert.Equal(["a"], asked);
    }

    /// <summary>
    /// The tree scrolls to the folder the pane is in. It followed the pane there but left the
    /// scroll where it was, so a folder far down a long drive was selected out of sight.
    /// </summary>
    [AvaloniaFact]
    public void TheTreeScrollsToTheFolderThePaneIsIn()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var window = new MainWindow { DataContext = model };
        window.Show();
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));
        window.UpdateLayout();

        var tree = window.GetVisualDescendants().OfType<TreeView>().First(static t => t.Name == "PART_Tree");
        var pane = (BrowserPaneModel)tree.DataContext!;
        var folders = Enumerable.Range(0, 120).Select(index => Folder($"folder-{index:000}", $"folder-{index:000}")).ToArray();
        var asked = new List<string>();
        pane.Tree.NavigateRequested += (_, target) => asked.Add(target);
        pane.Tree.Follow("bucket", "B", local: false, "", folders);
        pane.Tree.Follow("bucket", "B", local: false, "folder-110", []);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // Following the pane, and the items it brought into view being made, ask for nothing: a
        // selection the tree made itself is not somebody choosing a folder.
        Assert.Empty(asked);

        var selected = pane.Tree.Roots[0].Children.Single(static node => node.Name == "folder-110");
        Assert.True(selected.IsSelected);
        var item = Assert.IsType<TreeViewItem>(tree.TreeContainerFromItem(selected));
        var scroller = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
        var middle = item.TranslatePoint(new Point(0, item.Bounds.Height / 2), scroller)!.Value.Y;
        Assert.True(scroller.Offset.Y > 0, "The tree did not scroll.");
        Assert.InRange(middle, 0, scroller.Viewport.Height);
    }

    /// <summary>On this computer: This PC, the drive, and every folder down to where the pane is.</summary>
    [AvaloniaFact]
    public async Task ALocalPaneBuildsThisPcTheDriveAndTheFolders()
    {
        var folder = Directory.CreateTempSubdirectory("storagehub-tree-");
        try
        {
            Directory.CreateDirectory(Path.Combine(folder.FullName, "inside"));
            await using var pane = new BrowserPaneModel(new NoConnections());
            await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
            await pane.OpenAsync(pane.Connections.Single(static c => c.Kind == PaneContentKind.ThisPc), TestContext.Current.CancellationToken);

            var root = Assert.Single(pane.Tree.Roots);
            Assert.Equal(Ui.Pane.ThisPc, root.Name);
            Assert.NotEmpty(root.Children);

            await pane.NavigateAsync(folder.FullName, TestContext.Current.CancellationToken);

            var node = pane.Tree.Roots[0];
            var trail = new List<string>();
            while (node.Children.FirstOrDefault(static child => child.IsExpanded) is { } next)
            {
                trail.Add(next.Target);
                node = next;
            }

            Assert.Equal(Path.GetPathRoot(folder.FullName), trail[0], ignoreCase: true);
            Assert.Equal(Path.TrimEndingDirectorySeparator(folder.FullName), trail[^1], ignoreCase: true);
            Assert.True(node.IsSelected);
            Assert.Equal(["inside"], node.Children.Select(static child => child.Name));

            // And choosing the drive in the tree goes there.
            pane.Tree.Chosen(pane.Tree.Roots[0].Children.First(child =>
                string.Equals(child.Target, Path.GetPathRoot(folder.FullName), StringComparison.OrdinalIgnoreCase)));
            for (var wait = 0; wait < 50 && !string.Equals(pane.Path, Path.GetPathRoot(folder.FullName), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.TrimEndingDirectorySeparator(pane.Path), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(folder.FullName)!), StringComparison.OrdinalIgnoreCase); wait++)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.Equal(
                Path.TrimEndingDirectorySeparator(Path.GetPathRoot(folder.FullName)!),
                Path.TrimEndingDirectorySeparator(pane.Path),
                ignoreCase: true);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private sealed class NoConnections : IRemoteStorageAgentClient
    {
        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionListResponse(StorageIpcContract.CurrentVersion, []));

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
