using Avalonia;
using Avalonia.Controls;
using StorageHub.Desktop.Themes;

namespace StorageHub.Desktop.Views;

/// <summary>
/// How small each part of the shell may become: a pane, a split of two, the transfer queue.
/// </summary>
/// <remarks>
/// <para>
/// The rule is in docs/ui-rules.md (Layout): no splitter can be dragged, and no window made, so
/// small that a pane loses its own chrome or slides under the queue. Each piece says what it needs
/// from what it is actually drawn with (<see cref="BrowserPaneView.MinimumSize"/>,
/// <see cref="TransferQueueView.MinimumHeight"/>), a split adds its two sides up along its own
/// direction and takes the larger across it (<see cref="Split"/>), and the grids those numbers
/// become the limits of are what stop the splitters (<see cref="WorkspaceView"/>,
/// <see cref="MainWindow"/>).
/// </para>
/// <para>
/// Measured rather than written down, because a translated label, a bar hidden from a pane's
/// menu or a different font all change the answer, and a number typed in here would be right
/// for one of them.
/// </para>
/// </remarks>
internal static class LayoutLimits
{
    /// <summary>
    /// What a split of two needs: side by side the widths add up and the taller sets the height;
    /// stacked the heights add up and the wider sets the width. The splitter is in between.
    /// </summary>
    internal static Size Split(bool sideBySide, Size first, Size second, double splitter) => sideBySide
        ? new Size(first.Width + splitter + second.Width, Math.Max(first.Height, second.Height))
        : new Size(Math.Max(first.Width, second.Width), first.Height + splitter + second.Height);

    /// <summary>
    /// The size a row of chrome takes when nothing squeezes it, margin included.
    /// </summary>
    /// <remarks>
    /// Its laid-out size will not do: a DockPanel gives each row only what the rows above it left,
    /// so in a pane already too small the last rows measure as nothing, and a limit read from them
    /// would let the pane shrink further. Measured afresh with all the room in the world, then
    /// marked for measuring again so the next layout pass gives it back its real constraint. Given
    /// a width, only the height is unconstrained: for a row that wraps, such as a strip of tabs.
    /// </remarks>
    internal static Size Natural(Control control, double width = double.PositiveInfinity)
    {
        control.Measure(new Size(width > 0 ? width : double.PositiveInfinity, double.PositiveInfinity));
        var size = control.DesiredSize;
        control.InvalidateMeasure();
        return size;
    }

    /// <summary>
    /// A list's heading band and the given number of rows under it, from the shell's row height.
    /// </summary>
    internal static double ListBand(string rowsToken) =>
        (DesignTokens.Get<double>(rowsToken) + 1) * DesignTokens.Get<double>("ListRowHeight");

    /// <summary>
    /// How much room a splitter takes across: its thickness, or the theme's minimum where that is
    /// more, as Fluent's 6 is more than the shell's 4.
    /// </summary>
    internal static double Thickness(GridSplitter splitter, bool sideBySide) => sideBySide
        ? Math.Max(double.IsNaN(splitter.Width) ? 0 : splitter.Width, splitter.MinWidth)
        : Math.Max(double.IsNaN(splitter.Height) ? 0 : splitter.Height, splitter.MinHeight);
}
