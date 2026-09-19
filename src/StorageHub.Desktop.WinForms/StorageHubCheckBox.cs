using System.Drawing.Drawing2D;

namespace StorageHub.Desktop;

/// <summary>
/// A check box that paints its own box: the app's border colour, its corner radius, and a tick
/// drawn rather than taken from the system renderer.
///
/// <see cref="StorageHubToggle"/> is the control a settings row uses, because there the row owns
/// the words. A dialog's "Remember this choice" owns its own caption, and rewriting every such
/// dialog into rows would be a lot of churn for no gain -- so this stays a <see cref="CheckBox"/>
/// and every caller keeps its layout. Only the square the system drew in its own colours changes.
/// </summary>
public sealed class StorageHubCheckBox : CheckBox
{
    /// <summary>The box, before scaling. Sized to sit on the cap height of the caption beside it.</summary>
    private const int BoxSize = 16;

    /// <summary>Space between the box and the caption, before scaling.</summary>
    private const int Gap = 8;

    public StorageHubCheckBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        AutoSize = true;
        FlatStyle = FlatStyle.Flat;
        TextAlign = ContentAlignment.MiddleLeft;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var box = LogicalToDeviceUnits(BoxSize);
        var gap = LogicalToDeviceUnits(Gap);
        var width = MaximumSize.Width > 0
            ? MaximumSize.Width - box - gap
            : int.MaxValue;
        var caption = TextRenderer.MeasureText(
            Text,
            Font,
            new Size(width, int.MaxValue),
            TextFlags);
        return new Size(
            box + gap + caption.Width,
            Math.Max(box, caption.Height) + LogicalToDeviceUnits(4));
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        ArgumentNullException.ThrowIfNull(pevent);
        var graphics = pevent.Graphics;
        using (var backdrop = new SolidBrush(StorageHubFieldChrome.Backdrop(this)))
        {
            graphics.FillRectangle(backdrop, ClientRectangle);
        }

        var box = LogicalToDeviceUnits(BoxSize);
        var gap = LogicalToDeviceUnits(Gap);
        var bounds = new Rectangle(0, (Height - box) / 2, box - 1, box - 1);
        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = StorageHubFieldChrome.RoundedRectangle(bounds, LogicalToDeviceUnits(3)))
        {
            using var fill = new SolidBrush(!Enabled
                ? StorageHubTheme.SurfaceMuted
                : Checked ? StorageHubTheme.Primary : StorageHubTheme.Input);
            graphics.FillPath(fill, path);
            using var pen = new Pen(!Enabled
                ? StorageHubTheme.Border
                : Checked ? StorageHubTheme.Primary : StorageHubTheme.TextMuted);
            graphics.DrawPath(pen, path);
        }

        if (Checked)
        {
            using var tick = new Pen(
                Enabled ? StorageHubTheme.ContrastText(StorageHubTheme.Primary) : StorageHubTheme.DisabledText,
                1.8F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            graphics.DrawLines(tick,
            [
                new PointF(bounds.Left + (bounds.Width * 0.24F), bounds.Top + (bounds.Height * 0.52F)),
                new PointF(bounds.Left + (bounds.Width * 0.44F), bounds.Top + (bounds.Height * 0.72F)),
                new PointF(bounds.Left + (bounds.Width * 0.78F), bounds.Top + (bounds.Height * 0.30F))
            ]);
        }

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(StorageHubTheme.Primary) { DashStyle = DashStyle.Dot };
            using var path = StorageHubFieldChrome.RoundedRectangle(
                Rectangle.Inflate(bounds, LogicalToDeviceUnits(2), LogicalToDeviceUnits(2)),
                LogicalToDeviceUnits(4));
            graphics.DrawPath(focus, path);
        }

        graphics.SmoothingMode = previous;
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            Rectangle.FromLTRB(box + gap, 0, Width, Height),
            Enabled ? ForeColor : StorageHubTheme.DisabledText,
            TextFlags);
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    /// <summary>
    /// WordBreak so a long caption wraps inside a <see cref="Control.MaximumSize"/> the caller set,
    /// and NoPrefix because these captions are sentences rather than menu entries.
    /// </summary>
    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak |
        TextFormatFlags.NoPrefix;
}
