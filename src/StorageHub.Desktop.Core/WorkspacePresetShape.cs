namespace StorageHub.Desktop;

/// <summary>One pane's rectangle in a layout thumbnail, in the thumbnail's own units.</summary>
internal readonly record struct PresetCell(double X, double Y, double Width, double Height);

/// <summary>
/// Where the panes sit in a picture of an arrangement.
/// </summary>
/// <remarks>
/// <para>
/// The geometry of the six thumbnails in the New Workspace chooser, worked out from the same
/// <see cref="WorkspaceLayoutModel"/> the workspace itself is built from. The WinForms shell drew
/// this walk straight into a GDI+ bitmap; here it comes out as plain rectangles and the Avalonia
/// control only fills them, which is what lets a test say "the four-pane thumbnail is four equal
/// quarters" without rendering anything.
/// </para>
/// <para>
/// It matters that this is the layout model rather than a hand-drawn picture per preset: a
/// thumbnail that disagreed with what pressing it produces is a worse guide than no thumbnail.
/// </para>
/// </remarks>
internal static class WorkspacePresetShape
{
    /// <summary>The gap drawn between two panes, in the same units as the size asked for.</summary>
    internal const double Gap = 2;

    /// <summary>The rectangles for a preset, in reading order: left to right, top to bottom.</summary>
    internal static IReadOnlyList<PresetCell> Cells(WorkspacePreset preset, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);

        var cells = new List<PresetCell>();
        Walk(
            WorkspaceLayoutModel.CreatePreset(preset.PaneCount, preset.Layout).Root,
            new PresetCell(0, 0, width, height),
            cells);
        return cells;
    }

    private static void Walk(WorkspaceLayoutNode node, PresetCell area, List<PresetCell> cells)
    {
        if (node is WorkspaceSplitNode split)
        {
            if (split.Orientation == WorkspaceSplitOrientation.Vertical)
            {
                var first = (area.Width - Gap) * split.Ratio;
                Walk(split.First, area with { Width = first }, cells);
                Walk(
                    split.Second,
                    area with { X = area.X + first + Gap, Width = area.Width - first - Gap },
                    cells);
            }
            else
            {
                var first = (area.Height - Gap) * split.Ratio;
                Walk(split.First, area with { Height = first }, cells);
                Walk(
                    split.Second,
                    area with { Y = area.Y + first + Gap, Height = area.Height - first - Gap },
                    cells);
            }

            return;
        }

        // A thumbnail small enough to leave no room is drawn as nothing rather than as a rectangle
        // with a negative side, which is what a very small preview would otherwise ask for.
        if (area is { Width: > 0, Height: > 0 }) cells.Add(area);
    }
}
