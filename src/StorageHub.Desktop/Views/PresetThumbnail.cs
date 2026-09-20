using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StorageHub.Desktop.Views;

/// <summary>
/// A small picture of an arrangement: one rounded rectangle per pane.
/// </summary>
/// <remarks>
/// The rectangles come from <see cref="WorkspacePresetShape"/>, which walks the same
/// <see cref="WorkspaceLayoutModel"/> the workspace is built from -- so a thumbnail cannot drift
/// from what pressing it produces. Only the painting is here, and it is painting rather than nested
/// borders because six of these in a dialog would otherwise be six throwaway visual trees.
/// </remarks>
internal sealed class PresetThumbnail : Control
{
    public static readonly StyledProperty<WorkspacePreset?> PresetProperty =
        AvaloniaProperty.Register<PresetThumbnail, WorkspacePreset?>(nameof(Preset));

    static PresetThumbnail()
    {
        AffectsRender<PresetThumbnail>(PresetProperty);
        AffectsMeasure<PresetThumbnail>(PresetProperty);
    }

    public WorkspacePreset? Preset
    {
        get => GetValue(PresetProperty);
        set => SetValue(PresetProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Preset is not { } preset) return;

        var width = Bounds.Width - 2;
        var height = Bounds.Height - 2;
        if (width <= 0 || height <= 0) return;

        // The accent at low alpha for the fill and at full strength for the edge, so a thumbnail
        // reads in both appearances without a second colour token for either.
        var accent = this.TryFindResource("PrimaryColor", out var value) && value is Color colour
            ? colour
            : Colors.SteelBlue;
        var fill = new SolidColorBrush(accent, 0.22);
        var edge = new Pen(new SolidColorBrush(accent), 1.4);

        foreach (var cell in WorkspacePresetShape.Cells(preset, width, height))
        {
            context.DrawRectangle(
                fill,
                edge,
                new RoundedRect(new Rect(cell.X + 1, cell.Y + 1, cell.Width, cell.Height), 2));
        }
    }
}
