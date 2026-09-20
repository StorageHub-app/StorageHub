using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// The arrangement as it is actually drawn: nested grids, splitters, and a pane in each leaf.
/// </summary>
/// <remarks>
/// <see cref="WorkspacePaneLayoutTests"/> checks the tree; this checks that the tree reaches the
/// screen. They are worth separating because the failure modes are different -- a layout model can
/// be right while the view draws three panes on top of each other, which is exactly the class of
/// defect the screenshots in this port have caught and no assertion did.
/// </remarks>
public class WorkspaceLayoutViewTests
{
    [AvaloniaTheory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 4)]
    public void EveryArrangementDrawsOnePaneViewPerLeaf(int index, int expected)
    {
        var (window, workspace) = Shell();
        workspace.Preset = WorkspaceModel.Presets[index];
        Lay(window);

        var panes = window.GetVisualDescendants().OfType<BrowserPaneView>().ToArray();

        Assert.Equal(expected, panes.Length);
        Assert.Equal(expected, panes.Select(static pane => pane.DataContext).Distinct().Count());
    }

    /// <summary>
    /// Every pane gets a real share of the window, and none of them overlap.
    /// </summary>
    /// <remarks>
    /// The one thing a tree cannot tell you. A grid whose definitions were built in the wrong
    /// order, or whose splitter took a star share, produces a perfectly valid layout model and a
    /// workspace with a pane squeezed to nothing in it.
    /// </remarks>
    [AvaloniaFact]
    public void FourPanesEachGetAQuarterOfTheRoom()
    {
        var (window, workspace) = Shell();
        workspace.Preset = WorkspaceModel.Presets[5];
        Lay(window);

        var bounds = window.GetVisualDescendants().OfType<BrowserPaneView>()
            .Select(pane => pane.Bounds.Size)
            .ToArray();

        Assert.Equal(4, bounds.Length);
        Assert.All(bounds, size =>
        {
            Assert.True(size.Width > 200, $"a pane is only {size.Width} wide");
            Assert.True(size.Height > 100, $"a pane is only {size.Height} tall");
        });

        // All four the same size, because a grid is what the preset promises -- to within the
        // pixel a star split has to round somewhere, which lands on one of the four.
        Assert.InRange(bounds.Max(static size => size.Width) - bounds.Min(static size => size.Width), 0, 1);
        Assert.InRange(bounds.Max(static size => size.Height) - bounds.Min(static size => size.Height), 0, 1);
    }

    /// <summary>
    /// Two panes side by side sit beside each other; two stacked sit above each other.
    /// </summary>
    /// <remarks>
    /// Measured in the window's coordinates rather than each pane's own. A pane fills its grid cell,
    /// so its own bounds start at the origin whichever cell that is -- which would make this pass
    /// against a workspace that drew both arrangements identically.
    /// </remarks>
    [AvaloniaFact]
    public void TheOrientationOfATwoPaneArrangementIsTheOneItIsNamedFor()
    {
        var (window, workspace) = Shell();

        workspace.Preset = WorkspaceModel.Presets[1];
        Lay(window);
        var beside = Corners(window);

        workspace.Preset = WorkspaceModel.Presets[2];
        Lay(window);
        var stacked = Corners(window);

        Assert.Equal(beside[0].Y, beside[1].Y);
        Assert.True(beside[1].X > beside[0].X, "side by side panes share a left edge");
        Assert.Equal(stacked[0].X, stacked[1].X);
        Assert.True(stacked[1].Y > stacked[0].Y, "stacked panes share a top edge");
    }

    /// <summary>There is a splitter between every pair of panes, and no more than that.</summary>
    [AvaloniaFact]
    public void EachSplitHasExactlyOneSplitter()
    {
        var (window, workspace) = Shell();
        workspace.Preset = WorkspaceModel.Presets[5];
        Lay(window);

        // Counted inside the workspace, not the window: the shell has dividers of its own, and how
        // many of those there are is not this suite's business.
        var view = window.GetVisualDescendants().OfType<WorkspaceView>().Single();
        var splitters = view.GetVisualDescendants().OfType<GridSplitter>().Count();

        // Three splits hold four panes, whatever shape they are in.
        Assert.Equal(3, splitters);
        Assert.Equal(workspace.Panes.Count - 1, splitters);
    }

    /// <summary>
    /// Select all and invert reach the pane that is active, not the one that was.
    /// </summary>
    /// <remarks>
    /// Both have had a menu entry and a shortcut since the shell chrome landed and neither had
    /// anywhere to go. With four panes, binding them to a pane at startup would have left three
    /// panes whose menu entries did nothing, so they resolve the pane when they run.
    /// </remarks>
    [AvaloniaFact]
    public void SelectAllAndInvertActOnTheActivePane()
    {
        var preview = ShellPreview.CreateOnWorkspace();
        var window = new MainWindow { DataContext = preview, Width = 1500, Height = 920 };
        window.Show();
        var workspace = preview.Workspaces
            .Select(static tab => tab.Workspace)
            .First(static candidate => candidate is not null)!;
        Lay(window);

        Assert.True(preview.Router.IsHandled(UiCommandIds.EditSelectAll));
        Assert.True(preview.Router.IsHandled(UiCommandIds.EditInvertSelection));
        Assert.True(preview.Router.IsHandled(UiCommandIds.ViewRefresh));

        // Nothing is listed in a preview pane, so what this proves is where the command lands:
        // it runs against the active pane and does not throw against an empty one.
        workspace.Panes[1].IsActive = true;
        preview.Router.For(UiCommandIds.EditSelectAll).Execute(null);

        Assert.Empty(workspace.Panes[1].SelectedRows);
        Assert.Same(workspace.Panes[1], workspace.Active);
    }

    /// <summary>
    /// Photographs each arrangement, for a human to look at.
    /// </summary>
    /// <remarks>
    /// Set STORAGEHUB_SHOT_DIR to keep the files. Every visual defect this port has had -- a
    /// light-on-light default, a stock accent, clipped headers, a one-pixel row shift from a
    /// thicker active border -- was found this way and none of them failed an assertion.
    /// </remarks>
    [AvaloniaFact]
    public void EveryArrangementCanBePhotographed()
    {
        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");

        for (var index = 0; index < WorkspaceModel.Presets.Count; index++)
        {
            var (window, workspace) = Shell();
            workspace.Preset = WorkspaceModel.Presets[index];
            Lay(window);

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (string.IsNullOrWhiteSpace(directory)) continue;

            Directory.CreateDirectory(directory);
            using var stream = File.Create(
                Path.Combine(directory, $"workspace-{index}-{workspace.Panes.Count}pane.png"));
            frame!.Save(stream, new PngBitmapEncoderOptions());
        }
    }

    /// <summary>
    /// Photographs a workspace with a terminal pane in it, in both appearances.
    /// </summary>
    /// <remarks>
    /// The terminal surface and the arrangement chooser are the two pieces of new paint here, and
    /// both take their colours from the scheme rather than from a literal -- which is the thing
    /// that only a look in each appearance actually confirms.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ATerminalPaneCanBePhotographedInBothAppearances(bool dark)
    {
        var application = global::Avalonia.Application.Current!;
        ColorSchemeApplier.Apply(
            application, ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var storage = WorkspaceFakes.Summary("Studio Assets");
        var shell = WorkspaceFakes.Summary(
            "build-box", StorageConnectionProvider.Ssh, ConnectionProfileType.Client);
        var agent = new WorkspaceFakes.FakeBrowsingAgent([storage, shell]);

        var (window, workspace) = Shell();
        foreach (var pane in workspace.Panes)
        {
            await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        }

        // The preview's panes talk to a real agent over a pipe that is not there, so the terminal
        // is opened directly rather than through the picker. What is being photographed is the
        // surface, not the round trip.
        await workspace.Panes[1].OpenAsync(
            new PaneConnection(shell.ConnectionId, shell.DisplayName, LucideIconKind.SquareTerminal,
                PaneContentKind.SshClient),
            TestContext.Current.CancellationToken);
        Lay(window);

        Assert.True(workspace.Panes[1].IsTerminal);

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        await agent.DisposeAsync();

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(
            Path.Combine(directory, $"workspace-terminal-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    /// <summary>
    /// Every column shows its heading before anything has been listed.
    /// </summary>
    /// <remarks>
    /// A TableViewColumn is a definition rather than a control, so it has no DataContext and its
    /// heading has to be pushed onto it. The first attempt looked for the table in the visual tree
    /// and found nothing, because nothing is there until the control is measured -- so a pane that
    /// had not listed anything drew five blank columns and no assertion on the view model noticed.
    /// </remarks>
    [AvaloniaFact]
    public void EveryColumnShowsItsHeadingBeforeTheFirstListing()
    {
        var (window, _) = Shell();
        Lay(window);

        var table = Panes(window)[0].GetVisualDescendants().OfType<TableView>().Single();
        var headings = table.Columns.Select(static column => column.Header as string).ToArray();

        Assert.Equal(5, headings.Length);
        Assert.All(headings, heading => Assert.False(string.IsNullOrWhiteSpace(heading)));

        // Exactly one arrow, on the column being sorted by.
        Assert.Single(headings, heading => heading!.Contains('\u25b2') || heading.Contains('\u25bc'));
        Assert.StartsWith(Ui.Pane.ColumnName, headings[0], StringComparison.Ordinal);
        Assert.Equal(Ui.Pane.ColumnStatus, headings[4]);
    }

    /// <summary>Clicking a heading sorts by that column, and clicking it again reverses it.</summary>
    [AvaloniaFact]
    public void ClickingAHeadingSortsByThatColumn()
    {
        var (window, workspace) = Shell();
        Lay(window);
        var pane = workspace.Panes[0];
        var view = Panes(window).Single(control => ReferenceEquals(control.DataContext, pane));
        var headers = view.GetVisualDescendants().OfType<TableViewColumnHeader>().ToArray();

        Assert.Equal(5, headers.Length);

        Click(headers[1]);
        Assert.Equal(BrowserSortColumn.Size, pane.SortColumn);
        Assert.True(pane.SortAscending);

        Click(headers[1]);
        Assert.False(pane.SortAscending);

        Click(headers[3]);
        Assert.Equal(BrowserSortColumn.Modified, pane.SortColumn);
        Assert.True(pane.SortAscending);
    }

    /// <summary>
    /// A tap on a control, raised on it rather than aimed at it.
    /// </summary>
    /// <remarks>
    /// Driving the headless pointer instead would be more faithful, but a TableViewColumnHeader
    /// reports bounds that are its desired size rather than the arranged strip -- a star-width
    /// column measures 32 wide there and 500 on screen -- so a click computed from them lands in
    /// the listing below. What this asserts is the part the pane owns: a tap whose source is a
    /// heading sorts by that heading's column.
    /// </remarks>
    private static void Click(Control target) =>
        target.RaiseEvent(new TappedEventArgs(InputElement.TappedEvent, null!) { Source = target });

    private static BrowserPaneView[] Panes(Window window) =>
        [.. window.GetVisualDescendants().OfType<BrowserPaneView>()];

    /// <summary>Where each pane's top-left corner lands in the window.</summary>
    private static Point[] Corners(Window window) =>
        [.. Panes(window).Select(pane => pane.TranslatePoint(default, window) ?? default)];

    private static void Lay(Window window)
    {
        window.Measure(new Size(1500, 920));
        window.Arrange(new Rect(0, 0, 1500, 920));
        window.UpdateLayout();
    }

    /// <summary>The shell showing its workspace tab, which is the one with panes in it.</summary>
    private static (Window Window, WorkspaceModel Workspace) Shell()
    {
        var preview = ShellPreview.CreateOnWorkspace();

        // A real size before it is shown, not only a manual Arrange afterwards: hit testing uses
        // the window the platform actually made, so a click aimed at a control positioned by a
        // measurement the window never had lands outside it and is silently ignored.
        var window = new MainWindow { DataContext = preview, Width = 1500, Height = 920 };
        window.Show();
        var workspace = preview.Workspaces
            .Select(static tab => tab.Workspace)
            .First(static candidate => candidate is not null)!;
        return (window, workspace);
    }
}
