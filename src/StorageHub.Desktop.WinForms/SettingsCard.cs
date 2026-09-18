namespace StorageHub.Desktop;

/// <summary>
/// The surface a group of settings sits on: one rounded card, its rows stacked inside it and
/// separated by hairlines rather than by gaps.
///
/// Grouping this way is what lets the rows share an edge. The pages used to nest a
/// <see cref="TableLayoutPanel"/> inside a <see cref="FlowLayoutPanel"/> and set a height by hand
/// afterwards, which is why sections ended at a different distance below their last control on
/// every page. A card measures itself from its rows, so it cannot disagree with them.
/// </summary>
internal sealed class SettingsCard : Control, StorageHubFieldChrome.IPaintedBackdrop
{
    private readonly List<SettingsRow> _rows = [];

    internal SettingsCard()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    internal IReadOnlyList<SettingsRow> Rows => _rows;

    /// <summary>Appends a row and returns it, so a caller can keep a handle on the ones it updates.</summary>
    internal SettingsRow Add(SettingsRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _rows.Add(row);
        Controls.Add(row);
        PerformLayout();
        return row;
    }

    internal SettingsRow Add(string title, string? description, Control? control, Control? accessory = null) =>
        Add(new SettingsRow(title, description, control, accessory));

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        var height = 0;
        foreach (var row in _rows)
        {
            if (!row.Visible)
            {
                continue;
            }

            height += row.GetPreferredSize(new Size(width, 0)).Height;
        }

        return new Size(width, height);
    }

    /// <summary>
    /// What a control sitting on this card is actually sitting on. The card's own BackColor is
    /// Transparent so that its rounded corners show the page behind them, which makes BackColor
    /// the wrong thing for anything on top to read.
    /// </summary>
    public Color PaintedBackdrop => StorageHubFieldChrome.CardFill;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var top = 0;
        foreach (var row in _rows)
        {
            if (!row.Visible)
            {
                continue;
            }

            var height = row.GetPreferredSize(new Size(Width, 0)).Height;
            row.SetBounds(0, top, Width, height);
            top += height;
        }

        if (Height != top && top > 0)
        {
            Height = top;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using (var backdrop = new SolidBrush(StorageHubFieldChrome.Backdrop(this)))
        {
            e.Graphics.FillRectangle(backdrop, ClientRectangle);
        }

        StorageHubFieldChrome.PaintCard(e.Graphics, this);

        // Indented to the rows' own text, so the line separates settings rather than cutting the
        // card in half.
        var inset = LogicalToDeviceUnits(SettingsRow.HorizontalPadding);
        using var pen = new Pen(StorageHubTheme.Border);
        var visible = _rows.Where(static row => row.Visible).ToArray();
        for (var index = 1; index < visible.Length; index++)
        {
            var y = visible[index].Top;
            e.Graphics.DrawLine(pen, inset, y, Width - inset, y);
        }
    }
}
