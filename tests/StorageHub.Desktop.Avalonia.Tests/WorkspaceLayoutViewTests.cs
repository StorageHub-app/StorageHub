using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using StorageHub.Contracts.Ipc;
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
        var preview = ShellPreview.SampleOnWorkspace;
        var window = new MainWindow { DataContext = preview };
        window.Show();
        var workspace = preview.Workspaces
            .Select(static tab => tab.Workspace)
            .First(static candidate => candidate is not null)!;
        return (window, workspace);
    }
}
