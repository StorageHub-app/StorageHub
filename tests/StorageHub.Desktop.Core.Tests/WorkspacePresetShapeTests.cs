using StorageHub.Desktop;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The pictures in the New Workspace chooser.
/// </summary>
/// <remarks>
/// They are worked out from the same <see cref="WorkspaceLayoutModel"/> the workspace is built
/// from, so a thumbnail cannot show one arrangement and produce another. That is the whole reason
/// the geometry is here rather than in the control that paints it: this can be asserted, and a
/// painted bitmap can only be looked at.
/// </remarks>
public class WorkspacePresetShapeTests
{
    [Fact]
    public void EveryPresetDrawsOneCellPerPane()
    {
        foreach (var preset in WorkspacePreset.All)
        {
            var cells = WorkspacePresetShape.Cells(preset, 120, 80);

            Assert.Equal(preset.PaneCount, cells.Count);
            Assert.All(cells, cell =>
            {
                Assert.True(cell.Width > 0, $"{preset.Description} has a cell with no width");
                Assert.True(cell.Height > 0, $"{preset.Description} has a cell with no height");
            });
        }
    }

    /// <summary>One pane fills the thumbnail, because there is nothing to divide it with.</summary>
    [Fact]
    public void TheSinglePresetIsOneFullCell()
    {
        var cell = Assert.Single(WorkspacePresetShape.Cells(WorkspacePreset.All[0], 120, 80));

        Assert.Equal(new PresetCell(0, 0, 120, 80), cell);
    }

    /// <summary>The grid is four equal quarters, less the gaps between them.</summary>
    [Fact]
    public void TheGridIsFourEqualQuarters()
    {
        var grid = WorkspacePreset.All[5];

        var cells = WorkspacePresetShape.Cells(grid, 120, 80);

        Assert.Equal(4, cells.Count);
        Assert.Single(cells.Select(static cell => (cell.Width, cell.Height)).Distinct());
        Assert.Equal((120 - WorkspacePresetShape.Gap) / 2, cells[0].Width);
        Assert.Equal((80 - WorkspacePresetShape.Gap) / 2, cells[0].Height);
    }

    /// <summary>
    /// Side by side puts its panes beside each other, and top and bottom stacks them.
    /// </summary>
    /// <remarks>
    /// The two presets with the same pane count, which is the pair a picture has to tell apart. A
    /// thumbnail that drew both the same would make the chooser a coin toss.
    /// </remarks>
    [Fact]
    public void TheTwoPaneArrangementsAreDrawnDifferently()
    {
        var beside = WorkspacePresetShape.Cells(WorkspacePreset.All[1], 120, 80);
        var stacked = WorkspacePresetShape.Cells(WorkspacePreset.All[2], 120, 80);

        Assert.Equal(beside[0].Y, beside[1].Y);
        Assert.True(beside[1].X > beside[0].X);
        Assert.Equal(stacked[0].X, stacked[1].X);
        Assert.True(stacked[1].Y > stacked[0].Y);
    }

    /// <summary>A thumbnail too small to divide draws nothing rather than a negative rectangle.</summary>
    [Fact]
    public void AThumbnailWithNoRoomDrawsNothing()
    {
        var cells = WorkspacePresetShape.Cells(WorkspacePreset.All[5], 1, 1);

        Assert.Empty(cells);
    }
}
