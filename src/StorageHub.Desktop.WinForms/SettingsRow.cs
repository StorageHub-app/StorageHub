using System.ComponentModel;

namespace StorageHub.Desktop;

/// <summary>
/// One setting: its name, a line or two explaining it, and the control that changes it.
///
/// Every settings page is built from this one shape. Before it, a page could put a label above its
/// field, another beside it, and a third inside a check box's own caption, so no two pages -- and
/// sometimes no two fields on one page -- agreed on where to look for anything. Here the words are
/// always on the left and the control is always on the right, right-aligned against a single edge
/// the whole card shares, which is what turns a page into a column you can read straight down.
/// </summary>
internal sealed class SettingsRow : Control
{
    /// <summary>Room inside the row, before scaling. The card adds nothing on top of it.</summary>
    internal const int VerticalPadding = 13;
    internal const int HorizontalPadding = 16;

    /// <summary>Space between the text column and the control column, before scaling.</summary>
    private const int ColumnGap = 24;

    /// <summary>Space between the title and its description, before scaling.</summary>
    private const int TitleGap = 3;

    /// <summary>
    /// Below this much room for words, the control moves under them instead of squeezing the text
    /// into a gutter. The dialog cannot currently get this narrow, but a longer translation and a
    /// wider control can reach it between them.
    /// </summary>
    private const int MinimumTextWidth = 220;

    private readonly Font _titleFont = StorageHubTheme.CreateSectionFont();
    private readonly Control? _control;
    private readonly Control? _accessory;
    private string _title;
    private string? _description;

    internal SettingsRow(string title, string? description, Control? control, Control? accessory = null)
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        _title = title ?? string.Empty;
        _description = description;
        _control = control;
        _accessory = accessory;
        if (control is not null)
        {
            Controls.Add(control);
            // The control carries the setting's name for assistive technology, because the title
            // beside it is painted text with nothing to associate it with.
            if (string.IsNullOrEmpty(control.AccessibleName))
            {
                control.AccessibleName = _title;
            }

            if (string.IsNullOrEmpty(control.AccessibleDescription) && description is not null)
            {
                control.AccessibleDescription = description;
            }
        }

        if (accessory is not null)
        {
            Controls.Add(accessory);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string Title
    {
        get => _title;
        set
        {
            _title = value ?? string.Empty;
            PerformLayout();
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string? Description
    {
        get => _description;
        set
        {
            _description = value;
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>Paints the description in the warning colour, for a limit worth noticing.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool DescriptionIsWarning { get; set; }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        var pad = LogicalToDeviceUnits(HorizontalPadding);
        var controlWidth = ControlColumnWidth();
        var textWidth = width - (pad * 2) - (controlWidth > 0 ? controlWidth + LogicalToDeviceUnits(ColumnGap) : 0);
        var stacked = textWidth < LogicalToDeviceUnits(MinimumTextWidth);
        if (stacked)
        {
            textWidth = width - (pad * 2);
        }

        var textHeight = MeasureText(Math.Max(1, textWidth));
        var controlHeight = ControlColumnHeight();
        var content = stacked
            ? textHeight + (controlHeight > 0 ? LogicalToDeviceUnits(8) + controlHeight : 0)
            : Math.Max(textHeight, controlHeight);
        return new Size(width, content + (LogicalToDeviceUnits(VerticalPadding) * 2));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_control is null)
        {
            return;
        }

        var pad = LogicalToDeviceUnits(HorizontalPadding);
        var controlWidth = ControlColumnWidth();
        var textWidth = Width - (pad * 2) - controlWidth - LogicalToDeviceUnits(ColumnGap);
        if (textWidth < LogicalToDeviceUnits(MinimumTextWidth))
        {
            var top = LogicalToDeviceUnits(VerticalPadding)
                + MeasureText(Math.Max(1, Width - (pad * 2)))
                + LogicalToDeviceUnits(8);
            PlaceControls(pad, top);
            return;
        }

        var height = ControlColumnHeight();
        PlaceControls(Width - pad - controlWidth, (Height - height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var pad = LogicalToDeviceUnits(HorizontalPadding);
        var controlWidth = ControlColumnWidth();
        var textWidth = Width - (pad * 2) - (controlWidth > 0 ? controlWidth + LogicalToDeviceUnits(ColumnGap) : 0);
        if (textWidth < LogicalToDeviceUnits(MinimumTextWidth))
        {
            textWidth = Width - (pad * 2);
        }

        textWidth = Math.Max(1, textWidth);
        var top = LogicalToDeviceUnits(VerticalPadding);
        // A row with no title is a standing note rather than a setting, so its words start at the
        // top instead of leaving a blank line where a title would have been.
        var titleHeight = string.IsNullOrEmpty(_title)
            ? 0
            : TextRenderer.MeasureText(
                e.Graphics,
                _title,
                _titleFont,
                new Size(textWidth, int.MaxValue),
                TextFlags).Height;
        if (titleHeight > 0)
        {
            TextRenderer.DrawText(
                e.Graphics,
                _title,
                _titleFont,
                new Rectangle(pad, top, textWidth, titleHeight),
                Enabled ? StorageHubTheme.Text : StorageHubTheme.DisabledText,
                TextFlags);
        }

        if (string.IsNullOrEmpty(_description))
        {
            return;
        }

        var descriptionTop = top + titleHeight + (titleHeight > 0 ? LogicalToDeviceUnits(TitleGap) : 0);
        var descriptionHeight = TextRenderer.MeasureText(
            e.Graphics,
            _description,
            Font,
            new Size(textWidth, int.MaxValue),
            TextFlags).Height;
        TextRenderer.DrawText(
            e.Graphics,
            _description,
            Font,
            new Rectangle(pad, descriptionTop, textWidth, descriptionHeight),
            !Enabled ? StorageHubTheme.DisabledText
                : DescriptionIsWarning ? StorageHubTheme.Warning : StorageHubTheme.TextMuted,
            TextFlags);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    private void PlaceControls(int left, int top)
    {
        if (_control is null)
        {
            return;
        }

        var height = ControlColumnHeight();
        _control.SetBounds(
            left,
            top + ((height - _control.Height) / 2),
            _control.Width,
            _control.Height);
        if (_accessory is not null)
        {
            _accessory.SetBounds(
                _control.Right + LogicalToDeviceUnits(8),
                top + ((height - _accessory.Height) / 2),
                _accessory.Width,
                _accessory.Height);
        }
    }

    private int ControlColumnWidth()
    {
        if (_control is null)
        {
            return 0;
        }

        return _accessory is null
            ? _control.Width
            : _control.Width + LogicalToDeviceUnits(8) + _accessory.Width;
    }

    private int ControlColumnHeight() => _control is null
        ? 0
        : Math.Max(_control.Height, _accessory?.Height ?? 0);

    private int MeasureText(int width)
    {
        var height = string.IsNullOrEmpty(_title)
            ? 0
            : TextRenderer.MeasureText(
                _title,
                _titleFont,
                new Size(width, int.MaxValue),
                TextFlags).Height;
        if (!string.IsNullOrEmpty(_description))
        {
            height += (height > 0 ? LogicalToDeviceUnits(TitleGap) : 0) + TextRenderer.MeasureText(
                _description,
                Font,
                new Size(width, int.MaxValue),
                TextFlags).Height;
        }

        return height;
    }

    /// <summary>
    /// NoPrefix because these are sentences, not menu captions: an ampersand in a path or a
    /// description is an ampersand, not a mnemonic.
    /// </summary>
    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
