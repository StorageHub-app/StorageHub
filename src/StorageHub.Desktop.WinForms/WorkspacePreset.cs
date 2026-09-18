using System.Drawing.Drawing2D;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// One arrangement a new workspace can start in: a pane count paired with the orientation that
/// shapes it. Two panes side by side and two stacked are different presets, not one preset and a
/// setting, so both the chooser and Settings can offer every arrangement in a single list.
/// </summary>
internal sealed record WorkspacePreset(int PaneCount, WorkspaceLayout Layout, string Description)
{
    /// <summary>
    /// Every distinct arrangement, in increasing pane count.
    ///
    /// One pane and four panes appear once each because <see cref="WorkspaceLayoutModel.CreatePreset"/>
    /// ignores the orientation for them: a single pane has nothing to divide, and four panes are
    /// always a 2 x 2 grid. Listing them twice would offer the user a choice that changes nothing.
    /// </summary>
    /// <remarks>
    /// Built on each access rather than cached, because the descriptions follow the current
    /// language and a cached list would pin them to whatever language was active when this type
    /// was first touched. The six presets are compared by pane count and orientation, so a fresh
    /// list still equals the one a combo box is holding.
    /// </remarks>
    internal static IReadOnlyList<WorkspacePreset> All =>
    [
        new(1, WorkspaceLayout.SideBySide, Ui.Shell.PresetSingle),
        new(2, WorkspaceLayout.SideBySide, Ui.Shell.PresetSideBySide),
        new(2, WorkspaceLayout.TopAndBottom, Ui.Shell.PresetTopAndBottom),
        new(3, WorkspaceLayout.SideBySide, Ui.Shell.PresetLargeLeftTwoStacked),
        new(3, WorkspaceLayout.TopAndBottom, Ui.Shell.PresetLargeTopTwoBeside),
        new(4, WorkspaceLayout.SideBySide, Ui.Shell.PresetGrid)
    ];

    /// <summary>Whether the orientation actually changes this pane count's arrangement.</summary>
    internal bool OrientationMatters => PaneCount is 2 or 3;

    internal string Title => Ui.Format(
        PaneCount == 1 ? Ui.Shell.PresetPaneOneFormat : Ui.Shell.PresetPaneManyFormat,
        PaneCount);

    internal string Label => Ui.Format(Ui.Shell.PresetLabelFormat, Title, Description);

    /// <summary>
    /// The preset matching a stored pane count and orientation, or null when the pane count is out
    /// of range. Falls back to the first preset for that count when the orientation does not
    /// change it, so a stored "1 pane, top and bottom" still resolves.
    /// </summary>
    internal static WorkspacePreset? Find(int paneCount, WorkspaceLayout layout) =>
        All.FirstOrDefault(preset => preset.PaneCount == paneCount && preset.Layout == layout) ??
        All.FirstOrDefault(preset => preset.PaneCount == paneCount);

    /// <summary>
    /// Draws the arrangement by walking the layout this preset actually produces, so a thumbnail
    /// can never disagree with the workspace the user gets.
    /// </summary>
    internal static Bitmap CreatePreview(WorkspacePreset preset, int width, int height, Color accent)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 8);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 8);

        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(Color.FromArgb(38, accent));
        using var edge = new Pen(accent, 1.4F);
        DrawNode(
            graphics,
            fill,
            edge,
            WorkspaceLayoutModel.CreatePreset(preset.PaneCount, preset.Layout).Root,
            new RectangleF(0.7F, 0.7F, width - 1.4F, height - 1.4F));
        return bitmap;
    }

    private static void DrawNode(Graphics graphics, Brush fill, Pen edge, WorkspaceLayoutNode node, RectangleF area)
    {
        const float Gap = 2F;
        if (node is WorkspaceSplitNode split)
        {
            var ratio = (float)split.Ratio;
            if (split.Orientation == WorkspaceSplitOrientation.Vertical)
            {
                var left = (area.Width - Gap) * ratio;
                DrawNode(graphics, fill, edge, split.First, area with { Width = left });
                DrawNode(graphics, fill, edge, split.Second, area with
                {
                    X = area.X + left + Gap,
                    Width = area.Width - left - Gap
                });
            }
            else
            {
                var top = (area.Height - Gap) * ratio;
                DrawNode(graphics, fill, edge, split.First, area with { Height = top });
                DrawNode(graphics, fill, edge, split.Second, area with
                {
                    Y = area.Y + top + Gap,
                    Height = area.Height - top - Gap
                });
            }

            return;
        }

        if (area is { Width: > 0, Height: > 0 })
        {
            graphics.FillRectangle(fill, area);
            graphics.DrawRectangle(edge, area.X, area.Y, area.Width, area.Height);
        }
    }
}
