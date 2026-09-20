using System.Drawing.Drawing2D;

namespace StorageHub.Desktop;

/// <summary>
/// The GDI+ thumbnail for a layout preset.
/// </summary>
/// <remarks>
/// All that did not move to Desktop.Core with <see cref="WorkspacePreset"/>. It dies with this
/// shell; the Avalonia chooser draws the same walk into a DrawingContext instead of a Bitmap.
/// </remarks>
internal static class WorkspacePresetPreview
{
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
