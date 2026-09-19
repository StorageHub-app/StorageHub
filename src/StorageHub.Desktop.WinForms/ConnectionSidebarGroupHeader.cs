using System.ComponentModel;

namespace StorageHub.Desktop;

/// <summary>
/// A folder's title row in the connection sidebar. Drawn rather than built from a button on purpose:
/// a button paints a filled, outlined box, which put a second frame inside the group's own and made
/// the heaviest thing in the sidebar the one line carrying the least information. This is a
/// left-aligned line of text that takes a background only under the pointer or focus.
/// </summary>
internal sealed class ConnectionSidebarGroupHeader : Control
{
    private const int EdgeInset = 2;
    private const int ChevronWidth = 14;
    private const int IconGap = 6;

    private const TextFormatFlags Flags =
        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;

    private readonly Font _labelFont;
    private Image? _image;
    private bool _hot;
    private bool _expanded;
    private int _count;

    internal ConnectionSidebarGroupHeader()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        _labelFont = StorageHubTheme.CreateSectionFont();
        ForeColor = StorageHubTheme.Text;
        TabStop = true;
        AccessibleRole = AccessibleRole.ButtonDropDown;
        Cursor = Cursors.Hand;
        Height = MeasuredHeight;
    }

    /// <summary>The folder's own icon, when it has one. Owned by whoever tracked it, not by this control.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Image? Image
    {
        get => _image;
        set
        {
            _image = value;
            Invalidate();
        }
    }

    /// <summary>Which way the chevron points. The group owns the state; this only draws it.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Expanded
    {
        get => _expanded;
        set
        {
            _expanded = value;
            Invalidate();
        }
    }

    /// <summary>How many connections the folder holds, shown after the name.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int Count
    {
        get => _count;
        set
        {
            _count = value;
            Invalidate();
        }
    }

    protected override Size DefaultSize => new(200, 24);

    /// <summary>Tall enough for the label with room around it, derived from the font so it scales.</summary>
    private int MeasuredHeight => _labelFont.Height + (LogicalToDeviceUnits(4) * 2);

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var graphics = e.Graphics;
        using (var backdrop = new SolidBrush(BackColor))
        {
            graphics.FillRectangle(backdrop, ClientRectangle);
        }

        if (_hot || Focused)
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = UiShapes.RoundedRectangle(
                Rectangle.FromLTRB(EdgeInset, 0, Math.Max(EdgeInset + 1, Width - EdgeInset), Height),
                6F);
            using var fill = new SolidBrush(StorageHubTheme.Elevated);
            graphics.FillPath(fill, path);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
        }

        var chevron = new Rectangle(
            EdgeInset + LogicalToDeviceUnits(4),
            0,
            LogicalToDeviceUnits(ChevronWidth),
            Height);
        TextRenderer.DrawText(
            graphics,
            _expanded ? "▾" : "▸",
            Font,
            chevron,
            StorageHubTheme.TextMuted,
            Flags | TextFormatFlags.HorizontalCenter);
        var left = chevron.Right + LogicalToDeviceUnits(2);

        if (_image is { } icon)
        {
            var iconSize = LogicalToDeviceUnits(16);
            graphics.DrawImage(icon, new Rectangle(left, (Height - iconSize) / 2, iconSize, iconSize));
            left += iconSize + LogicalToDeviceUnits(IconGap);
        }

        // The count is measured first so a long folder name gives up room to it rather than
        // eliding into it.
        var tail = $"  ·  {_count}";
        var tailWidth = TextRenderer.MeasureText(graphics, tail, Font, Size.Empty, Flags).Width;
        var right = Width - EdgeInset - LogicalToDeviceUnits(6);
        var label = Math.Min(
            TextRenderer.MeasureText(graphics, Text, _labelFont, Size.Empty, Flags).Width,
            Math.Max(0, right - left - tailWidth));
        TextRenderer.DrawText(
            graphics,
            Text,
            _labelFont,
            new Rectangle(left, 0, label, Height),
            ForeColor,
            Flags | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            graphics,
            tail,
            Font,
            new Rectangle(left + label, 0, tailWidth, Height),
            StorageHubTheme.TextMuted,
            Flags);

        if (Focused)
        {
            ControlPaint.DrawFocusRectangle(
                graphics,
                Rectangle.FromLTRB(
                    EdgeInset + 1,
                    1,
                    Math.Max(EdgeInset + 2, Width - EdgeInset - 1),
                    Height - 1));
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hot = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
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

    // A bare Control forwards neither key to Click, and a folder that only opens under the mouse
    // cannot be reached from the keyboard.
    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            InvokeOnClick(this, EventArgs.Empty);
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = MeasuredHeight;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _labelFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
