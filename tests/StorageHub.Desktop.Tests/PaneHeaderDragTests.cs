using Avalonia;
using Avalonia.Headless.XUnit;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Dragging a pane by its header onto another, and the strip above the panes that says you can.
/// </summary>
public class PaneHeaderDragTests
{
    [Theory]
    [InlineData(10, 100, "Left")]
    [InlineData(390, 100, "Right")]
    [InlineData(200, 10, "Top")]
    [InlineData(200, 190, "Bottom")]
    [InlineData(200, 100, null)]
    public void TheEdgesDockAndTheMiddleSwaps(double x, double y, string? expected)
    {
        var edge = PaneHeaderDragHandler.EdgeAt(new Point(x, y), new Size(400, 200));
        Assert.Equal(expected, edge?.ToString());
    }

    [AvaloniaFact]
    public void DroppingInTheMiddleSwapsAndOnAnEdgeDocks()
    {
        var workspace = ShellPreview.CreateOnWorkspace().Workspaces.Last(static tab => tab.IsClosable).Workspace!;
        var first = workspace.Panes[0];
        var second = workspace.Panes[1];

        Assert.True(workspace.Rearrange(first, second, edge: null));
        Assert.Same(second, workspace.Panes[0]);
        Assert.Same(first, workspace.Panes[1]);

        Assert.False(workspace.Rearrange(first, first, edge: null));
        Assert.True(workspace.Rearrange(first, second, WorkspaceDockEdge.Top));
        Assert.Equal(2, workspace.Panes.Count);
    }

    [AvaloniaFact]
    public void TheStripSaysEmptyUntilSomethingIsStaged()
    {
        var workspace = ShellPreview.CreateOnWorkspace().Workspaces.Last(static tab => tab.IsClosable).Workspace!;
        Assert.Equal(Ui.Shell.ClipboardEmpty, workspace.ClipboardText);
    }
}
