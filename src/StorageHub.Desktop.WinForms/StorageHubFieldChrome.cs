using System.Drawing.Drawing2D;

namespace StorageHub.Desktop;

/// <summary>
/// The measurements and the painting every custom input in the app shares: one height, one corner
/// radius, one focus ring, one set of edge paddings.
///
/// It exists because the stock controls could not agree on any of that. A <see cref="TextBox"/>
/// cannot grow past its font, a <see cref="NumericUpDown"/> settled a few pixels shorter, a
/// <see cref="ComboBox"/> a few taller, and each drew a square one-pixel border in a colour the
/// palette does not own -- so a row of three inputs read as three different controls that happened
/// to sit near each other. Everything here is a logical unit scaled at the moment it is measured,
/// exactly as <see cref="StorageHubButton"/> does it, so a field and the button beside it are the
/// same height at any display scaling rather than at one the numbers were chosen on.
/// </summary>
internal static class StorageHubFieldChrome
{
    /// <summary>Room above and below the text, before scaling. Sets the height of every input.</summary>
    internal const int VerticalPadding = 8;

    /// <summary>Room between the border and the text, before scaling.</summary>
    internal const int HorizontalPadding = 10;

    /// <summary>Corner radius of an input, before scaling.</summary>
    internal const int CornerRadius = 5;

    /// <summary>
    /// The same input, tightened for a strip rather than a form.
    /// </summary>
    /// <remarks>
    /// A toolbar is a row of flat text and glyphs, and a full-height bordered field dropped into
    /// one reads as the heaviest thing on the strip -- it stops being an input among commands and
    /// starts being a slab. Dense keeps the shape and the palette and takes the weight out: less
    /// room around the text, a tighter corner and a narrower trailing zone.
    /// </remarks>
    internal const int DenseVerticalPadding = 3;
    internal const int DenseHorizontalPadding = 8;
    internal const int DenseCornerRadius = 4;
    internal const int DenseTrailingZoneWidth = 18;

    /// <summary>Corner radius of a card, before scaling. Slightly softer than an input's.</summary>
    internal const int CardRadius = 7;

    /// <summary>Width of the trailing zone that carries a chevron or a stepper, before scaling.</summary>
    internal const int TrailingZoneWidth = 24;

    /// <summary>
    /// The height a single-line input settles at for a control's font and scaling. Deriving it
    /// from the font is what keeps an input, a button and a dropdown on one line.
    /// </summary>
    internal static int MeasureHeight(Control control, bool dense = false)
    {
        ArgumentNullException.ThrowIfNull(control);
        var padding = dense ? DenseVerticalPadding : VerticalPadding;
        return control.Font.Height + (control.LogicalToDeviceUnits(padding) * 2);
    }

    /// <summary>The room inside an input before its text starts, at the size it is drawn.</summary>
    internal static int TextInset(Control control, bool dense = false)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.LogicalToDeviceUnits(dense ? DenseHorizontalPadding : HorizontalPadding);
    }

    /// <summary>The width of the zone carrying a chevron or a stepper, at the size it is drawn.</summary>
    internal static int TrailingZone(Control control, bool dense = false)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.LogicalToDeviceUnits(dense ? DenseTrailingZoneWidth : TrailingZoneWidth);
    }

    /// <summary>
    /// Paints an input's background, border and focus ring.
    ///
    /// The ring is drawn as a thicker border in the accent colour rather than as a halo outside
    /// the control: a halo needs room the layout has not reserved, so it would be clipped by the
    /// neighbour above.
    /// </summary>
    internal static void PaintField(Graphics graphics, Control control, bool focused, bool dense = false)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(control);
        var bounds = control.ClientRectangle;
        if (bounds.Width <= 2 || bounds.Height <= 2)
        {
            return;
        }

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var edge = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
        var radius = control.LogicalToDeviceUnits(dense ? DenseCornerRadius : CornerRadius);
        using (var path = RoundedRectangle(edge, radius))
        {
            using var fill = new SolidBrush(control.Enabled
                ? StorageHubTheme.Input
                : StorageHubTheme.SurfaceMuted);
            graphics.FillPath(fill, path);
            using var pen = new Pen(
                focused ? StorageHubTheme.Primary : StorageHubTheme.Border,
                focused && !dense ? 1.6F : 1F);
            graphics.DrawPath(pen, path);
        }

        graphics.SmoothingMode = previous;
    }

    /// <summary>Paints a card: the surface a group of rows sits on.</summary>
    internal static void PaintCard(Graphics graphics, Control control)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(control);
        var bounds = control.ClientRectangle;
        if (bounds.Width <= 2 || bounds.Height <= 2)
        {
            return;
        }

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var edge = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
        using (var path = RoundedRectangle(edge, control.LogicalToDeviceUnits(CardRadius)))
        {
            using var fill = new SolidBrush(StorageHubTheme.SurfaceMuted);
            graphics.FillPath(fill, path);
            using var pen = new Pen(StorageHubTheme.Border);
            graphics.DrawPath(pen, path);
        }

        graphics.SmoothingMode = previous;
    }

    /// <summary>
    /// Paints a chevron pointing up or down, centred in the given box. Drawn rather than taken
    /// from the icon set because it has to follow the text colour of whatever it sits in, and at
    /// this size an outline stroke reads better than a rasterised glyph.
    /// </summary>
    internal static void PaintChevron(Graphics graphics, Rectangle box, Color color, bool pointingUp)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var centerX = box.Left + (box.Width / 2F);
        var centerY = box.Top + (box.Height / 2F);
        var reach = Math.Max(3F, box.Height / 5F);
        var drop = reach * 0.62F;
        using var pen = new Pen(color, 1.4F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        if (pointingUp)
        {
            graphics.DrawLines(pen,
            [
                new PointF(centerX - reach, centerY + (drop / 2F)),
                new PointF(centerX, centerY - (drop / 2F)),
                new PointF(centerX + reach, centerY + (drop / 2F))
            ]);
        }
        else
        {
            graphics.DrawLines(pen,
            [
                new PointF(centerX - reach, centerY - (drop / 2F)),
                new PointF(centerX, centerY + (drop / 2F)),
                new PointF(centerX + reach, centerY - (drop / 2F))
            ]);
        }

        graphics.SmoothingMode = previous;
    }

    /// <summary>A rounded rectangle path. The caller owns it.</summary>
    internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, radius * 2);
        if (bounds.Width <= diameter || bounds.Height <= diameter)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Whatever paints behind a control with rounded corners, so those corners can show it. Rows
    /// and flow panels are themselves transparent, so the search walks up to the first ancestor
    /// that actually carries a colour.
    /// </summary>
    internal static Color Backdrop(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        for (var ancestor = control.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.BackColor.A == 255)
            {
                return ancestor.BackColor;
            }
        }

        return StorageHubTheme.Surface;
    }
}
