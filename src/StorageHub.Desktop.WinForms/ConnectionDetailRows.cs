using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// A collapsible category bar in the connection detail grid.
///
/// Drawn rather than composed from a button and a label so the chevron, the band and the rule
/// underneath line up at every DPI, and so the whole bar is one click target.
/// </summary>
internal sealed class DetailCategoryHeader : Control
{
    private readonly Font _font;
    private bool _collapsed;
    private bool _hovered;

    internal DetailCategoryHeader(string title, Font font)
    {
        Text = title;
        _font = font;
        Height = this.TextBoxHeight(_font, 2);
        Cursor = Cursors.Hand;
        TabStop = true;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.ButtonDropDownGrid;
        AccessibleName = title;
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
            true);
    }

    internal event EventHandler<bool>? CollapsedChanged;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Collapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value)
            {
                return;
            }

            _collapsed = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
        Toggle();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            e.Handled = true;
            Toggle();
        }
    }

    private void Toggle()
    {
        Collapsed = !Collapsed;
        CollapsedChanged?.Invoke(this, Collapsed);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaint(e);
        using (var band = new SolidBrush(_hovered || Focused ? StorageHubTheme.Elevated : StorageHubTheme.SurfaceMuted))
        {
            e.Graphics.FillRectangle(band, ClientRectangle);
        }

        // The chevron points down when the category is open, which is the direction its rows lie.
        var centre = Height / 2;
        using (var chevron = new Pen(StorageHubTheme.TextMuted, 1.5F))
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (_collapsed)
            {
                e.Graphics.DrawLines(chevron,
                [
                    new PointF(7, centre - 4), new PointF(11, centre), new PointF(7, centre + 4)
                ]);
            }
            else
            {
                e.Graphics.DrawLines(chevron,
                [
                    new PointF(6, centre - 2), new PointF(10, centre + 2), new PointF(14, centre - 2)
                ]);
            }

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
        }

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            _font,
            new Rectangle(
                LogicalToDeviceUnits(20), 0, Math.Max(1, Width - LogicalToDeviceUnits(24)), Height),
            StorageHubTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        using var rule = new Pen(StorageHubTheme.Border);
        e.Graphics.DrawLine(rule, 0, Height - 1, Width, Height - 1);
    }
}

/// <summary>
/// One key/value row of the detail grid: a banded background, a column divider, and the value
/// selectable for copying while staying read-only.
/// </summary>
internal sealed class DetailFactRow : Control
{
    private const int KeyColumnWidth = 118;

    private readonly string _key;
    private readonly string _value;
    private readonly bool _muted;
    private readonly bool _odd;
    private bool _hovered;

    internal DetailFactRow(string key, string value, bool muted, int index)
    {
        _key = key;
        _value = value;
        _muted = muted;
        _odd = index % 2 == 1;
        Height = this.TextBoxHeight(2);
        TabStop = true;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.Row;

        // Announced as one line: the key alone tells a screen reader nothing, and the two labels
        // used to be siblings it read as unrelated.
        AccessibleName = Ui.Format(Ui.Connections.DetailFactAccessibleFormat, key, value);
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer,
            true);
    }

    /// <summary>The row's label, as shown in the left column.</summary>
    internal string Key => _key;

    /// <summary>The row's value, in full rather than as the column ellipsizes it.</summary>
    internal string Value => _value;

    /// <summary>Raised when this row becomes the one the description pane should describe.</summary>
    internal event EventHandler? Describe;

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Describe?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        Describe?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
    }

    /// <summary>
    /// Ctrl+C copies the value. The grid is read-only, but the reason to look at a fingerprint or
    /// an endpoint is usually to paste it somewhere.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Control && e.KeyCode == Keys.C)
        {
            e.Handled = true;
            try
            {
                Clipboard.SetText(_value);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Another process can hold the clipboard; a failed copy is not worth a dialog.
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaint(e);
        var background = _hovered || Focused
            ? StorageHubTheme.CurrentPalette.Selection
            : _odd ? StorageHubTheme.Elevated : StorageHubTheme.Surface;
        using (var fill = new SolidBrush(background))
        {
            e.Graphics.FillRectangle(fill, ClientRectangle);
        }

        TextRenderer.DrawText(
            e.Graphics,
            _key,
            Font,
            new Rectangle(
                LogicalToDeviceUnits(8), 0, LogicalToDeviceUnits(KeyColumnWidth - 12), Height),
            StorageHubTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // The divider is what makes this read as two columns rather than as indented text.
        using (var divider = new Pen(StorageHubTheme.Border))
        {
            var keyColumn = LogicalToDeviceUnits(KeyColumnWidth);
            e.Graphics.DrawLine(divider, keyColumn, 0, keyColumn, Height);
            e.Graphics.DrawLine(divider, 0, Height - 1, Width, Height - 1);
        }

        TextRenderer.DrawText(
            e.Graphics,
            _value,
            Font,
            new Rectangle(
                LogicalToDeviceUnits(KeyColumnWidth + 8),
                0,
                Math.Max(1, Width - LogicalToDeviceUnits(KeyColumnWidth + 12)),
                Height),
            _muted ? StorageHubTheme.TextMuted : StorageHubTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
