namespace StorageHub.Desktop;

/// <summary>
/// The small heading above a card: the group's name, set in capitals and letter-spaced.
///
/// It is painted rather than set on a <see cref="Label"/> because WinForms has no letter spacing,
/// and without it a short upper-case word reads as shouting instead of as a quiet divider. Each
/// character is drawn at its own offset, which is cheap for the two or three words a caption holds.
/// </summary>
internal sealed class SettingsCaption : Control
{
    /// <summary>Extra space between characters, before scaling.</summary>
    private const int Tracking = 1;

    private readonly Font _font = new("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point);

    internal SettingsCaption(string text)
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Text = text ?? string.Empty;
        Height = _font.Height + LogicalToDeviceUnits(4);
        // A caption names the card under it; assistive technology reads that card's rows, so the
        // caption is decoration and stays out of the tab order and the tree.
        AccessibleRole = AccessibleRole.StaticText;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using (var backdrop = new SolidBrush(StorageHubFieldChrome.Backdrop(this)))
        {
            e.Graphics.FillRectangle(backdrop, ClientRectangle);
        }

        var text = Text.ToUpperInvariant();
        var tracking = LogicalToDeviceUnits(Tracking);
        var x = 0;
        foreach (var character in text)
        {
            var glyph = character.ToString();
            TextRenderer.DrawText(
                e.Graphics,
                glyph,
                _font,
                new Point(x, 0),
                StorageHubTheme.TextMuted,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            x += TextRenderer.MeasureText(
                e.Graphics,
                glyph,
                _font,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + tracking;
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font.Dispose();
        }

        base.Dispose(disposing);
    }
}
