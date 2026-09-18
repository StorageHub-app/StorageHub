using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace StorageHub.Desktop;

/// <summary>Which palette role a button takes, and how much weight it carries in a row.</summary>
public enum StorageHubButtonVariant
{
    /// <summary>The one action a surface is steering towards. Filled.</summary>
    Primary,

    /// <summary>Every other action. Outlined.</summary>
    Secondary,

    /// <summary>An action that destroys data, so it reads as dangerous at rest.</summary>
    Danger
}

/// <summary>
/// How much room a button takes. Three sizes, each one matched to an input it can sit beside.
/// </summary>
public enum StorageHubButtonSize
{
    /// <summary>
    /// For a strip or any row already set tight: the height a dense input settles at, so the two
    /// line up. See <see cref="StorageHubFieldChrome.DenseVerticalPadding"/>.
    /// </summary>
    Small,

    /// <summary>The ordinary button, matching a form's inputs. Everything is this unless it says otherwise.</summary>
    Medium,

    /// <summary>
    /// For an action a whole window is about -- a wizard's Continue, a dialog's one real verb.
    /// Taller and wider than the inputs around it, which is the point.
    /// </summary>
    Large
}

/// <summary>
/// A button that measures itself instead of carrying fixed pixel sizes, and paints itself instead
/// of asking the system renderer to.
///
/// The styling this replaces pinned a button to 34 pixels tall whatever the display scaling was.
/// A text box cannot grow past its font, so at 100% the two happened to line up and at 125% the
/// field settled near 27 while the button stayed at 34 and towered over it. Fixed widths went the
/// same way: a 90-pixel minimum silently won over anything a caller assigned.
///
/// So every metric here is a logical unit scaled through <see cref="Control.LogicalToDeviceUnits(int)"/>
/// at the moment it is measured, and the height derives from the font exactly as an input's does.
/// A button lines up with the field beside it at any scaling because both are sized from the same
/// thing, rather than because a constant was chosen on one display. WinForms re-measures through
/// <see cref="GetPreferredSize"/> whenever the font or DPI changes, so nothing has to be recomputed
/// by hand afterwards.
///
/// The painting is ours for one reason: <see cref="FlatStyle.Flat"/> draws square corners, and a
/// square button beside a rounded input is the one thing left that looked bolted on.
/// </summary>
public sealed class StorageHubButton : Button
{
    /// <summary>Space between a glyph and the caption it labels, before scaling.</summary>
    private const int GlyphGap = 6;

    private StorageHubButtonVariant _variant = StorageHubButtonVariant.Secondary;
    private StorageHubButtonSize _size = StorageHubButtonSize.Medium;
    private UiGlyph? _glyph;
    private UiIconTone? _glyphTone;
    private bool _hot;
    private bool _pressed;

    public StorageHubButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        // The corners have to show whatever is behind them, so the button never fills its own
        // rectangle with a colour of its own.
        BackColor = Color.Transparent;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        UseVisualStyleBackColor = false;
        TextImageRelation = TextImageRelation.ImageBeforeText;
        ImageAlign = ContentAlignment.MiddleCenter;
        TextAlign = ContentAlignment.MiddleCenter;
        Cursor = Cursors.Hand;
        ApplyGlyph();
    }

    /// <summary>The palette role. Changing it restyles the button in place.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public StorageHubButtonVariant Variant
    {
        get => _variant;
        set
        {
            if (_variant == value)
            {
                return;
            }

            _variant = value;
            ApplyGlyph();
            Invalidate();
        }
    }

    /// <summary>
    /// How much room the button takes. See <see cref="StorageHubButtonSize"/>. Named to stay clear
    /// of <see cref="Control.Size"/>, which is the pixels it ended up occupying.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public StorageHubButtonSize ButtonSize
    {
        get => _size;
        set
        {
            if (_size == value)
            {
                return;
            }

            _size = value;
            ApplyGlyph();
            InvalidateMeasure();
            Invalidate();
        }
    }

    /// <summary>
    /// An optional glyph shown before the caption. Tracked, so it is redrawn in the right tone
    /// when the palette flips underneath it.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public UiGlyph? Glyph
    {
        get => _glyph;
        set
        {
            if (_glyph == value)
            {
                return;
            }

            _glyph = value;
            ApplyGlyph();
        }
    }

    /// <summary>
    /// The glyph's palette role. Defaults to one that reads against the variant's own fill, which
    /// is what a caller wants unless the glyph is carrying its own meaning.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public UiIconTone? GlyphTone
    {
        get => _glyphTone;
        set
        {
            if (_glyphTone == value)
            {
                return;
            }

            _glyphTone = value;
            ApplyGlyph();
        }
    }

    /// <summary>
    /// Drops the caption-width floor, for a button that is only a glyph. Such a button is square
    /// on the caption's own height, so it sits in a row without stretching it.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsIconOnly { get; set; }

    /// <summary>The height a single-line input settles at beside this button, at this scaling.</summary>
    internal int MeasuredHeight => Font.Height + (LogicalToDeviceUnits(VerticalPadding) * 2);

    /// <summary>Room above and below the caption, before scaling.</summary>
    private int VerticalPadding => _size switch
    {
        StorageHubButtonSize.Small => StorageHubFieldChrome.DenseVerticalPadding,
        StorageHubButtonSize.Large => 11,
        _ => StorageHubFieldChrome.VerticalPadding
    };

    /// <summary>
    /// Room left and right of the caption, before scaling. Wider than the vertical room by design:
    /// a button reads as a button because its caption sits in open space, not because of its edge.
    /// </summary>
    private int HorizontalPadding => _size switch
    {
        StorageHubButtonSize.Small => 10,
        StorageHubButtonSize.Large => 20,
        _ => 14
    };

    /// <summary>Corner radius, before scaling. One step softer as the button grows.</summary>
    private int CornerRadius => _size switch
    {
        StorageHubButtonSize.Small => StorageHubFieldChrome.DenseCornerRadius,
        StorageHubButtonSize.Large => 6,
        _ => StorageHubFieldChrome.CornerRadius
    };

    /// <summary>
    /// Enough width that a one-word caption still reads as a button rather than as a chip. It is a
    /// floor, not a size: a longer caption grows past it, and an icon-only button opts out.
    /// </summary>
    private int MinimumCaptionWidth => _size switch
    {
        StorageHubButtonSize.Small => 56,
        StorageHubButtonSize.Large => 96,
        _ => 72
    };

    /// <summary>Sized against the caption so the pair reads as one unit rather than as an icon with a label.</summary>
    private int GlyphSize => _size switch
    {
        StorageHubButtonSize.Small => 14,
        StorageHubButtonSize.Large => 18,
        _ => 16
    };

    public override Size GetPreferredSize(Size proposedSize)
    {
        var height = MeasuredHeight;
        var glyph = Image is { } image && TextImageRelation != TextImageRelation.Overlay
            ? LogicalImageSize(image)
            : System.Drawing.Size.Empty;

        if (IsIconOnly)
        {
            // Square on the row height. Measuring the (empty) caption instead would collapse it
            // to its padding and leave the glyph clipped.
            return new Size(height, height);
        }

        var caption = TextRenderer.MeasureText(Text, Font).Width;
        var glyphWidth = glyph.IsEmpty ? 0 : glyph.Width + LogicalToDeviceUnits(GlyphGap);
        var width = caption + glyphWidth + (LogicalToDeviceUnits(HorizontalPadding) * 2);
        return new Size(Math.Max(width, LogicalToDeviceUnits(MinimumCaptionWidth)), height);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        ArgumentNullException.ThrowIfNull(pevent);
        var graphics = pevent.Graphics;
        using (var backdrop = new SolidBrush(StorageHubFieldChrome.Backdrop(this)))
        {
            graphics.FillRectangle(backdrop, ClientRectangle);
        }

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var edge = Rectangle.FromLTRB(0, 0, Width - 1, Height - 1);
        var surface = ResolveSurface();
        using (var path = StorageHubFieldChrome.RoundedRectangle(edge, LogicalToDeviceUnits(CornerRadius)))
        {
            if (surface.Fill.A > 0)
            {
                using var fill = new SolidBrush(surface.Fill);
                graphics.FillPath(fill, path);
            }

            if (surface.Border.A > 0)
            {
                using var pen = new Pen(surface.Border);
                graphics.DrawPath(pen, path);
            }

            if (Focused && ShowFocusCues)
            {
                using var focus = new Pen(surface.Text) { DashStyle = DashStyle.Dot };
                using var ring = StorageHubFieldChrome.RoundedRectangle(
                    Rectangle.Inflate(edge, -3, -3),
                    Math.Max(1, LogicalToDeviceUnits(CornerRadius) - 2));
                graphics.DrawPath(focus, ring);
            }
        }

        graphics.SmoothingMode = previous;
        PaintContent(graphics, surface.Text);
    }

    /// <summary>
    /// The glyph and the caption, centred as one group. WinForms' own content layout cannot be
    /// reused here: it positions against the control's padding, which this button spends on its
    /// rounded edge instead.
    /// </summary>
    private void PaintContent(Graphics graphics, Color text)
    {
        var glyph = Image is { } image ? LogicalImageSize(image) : System.Drawing.Size.Empty;
        var gap = glyph.IsEmpty || IsIconOnly || string.IsNullOrEmpty(Text)
            ? 0
            : LogicalToDeviceUnits(GlyphGap);
        var caption = IsIconOnly || string.IsNullOrEmpty(Text)
            ? System.Drawing.Size.Empty
            : TextRenderer.MeasureText(graphics, Text, Font);
        var total = glyph.Width + gap + caption.Width;
        var left = (Width - total) / 2;

        if (!glyph.IsEmpty && Image is { } bitmap)
        {
            graphics.DrawImage(
                bitmap,
                new Rectangle(left, (Height - glyph.Height) / 2, glyph.Width, glyph.Height));
            left += glyph.Width + gap;
        }

        if (caption.Width > 0)
        {
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                new Rectangle(left, (Height - caption.Height) / 2, caption.Width, caption.Height),
                text,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// The three colours this button's current state resolves to: what fills it, what edges it,
    /// and what its caption is drawn in. Internal because it is also what a test can ask, now
    /// that the colours live in the painting rather than in <see cref="Control.BackColor"/>.
    /// </summary>
    internal (Color Fill, Color Border, Color Text) ResolveSurface()
    {
        if (!Enabled)
        {
            return (StorageHubTheme.SurfaceMuted, StorageHubTheme.Border, StorageHubTheme.DisabledText);
        }

        return _variant switch
        {
            StorageHubButtonVariant.Primary => (
                _pressed ? StorageHubTheme.PrimaryPressed
                    : _hot ? StorageHubTheme.PrimaryHover : StorageHubTheme.Primary,
                _pressed ? StorageHubTheme.PrimaryPressed
                    : _hot ? StorageHubTheme.PrimaryHover : StorageHubTheme.Primary,
                StorageHubTheme.ContrastText(StorageHubTheme.Primary)),
            StorageHubButtonVariant.Danger => (
                _pressed || _hot ? StorageHubTheme.Danger : StorageHubTheme.DangerTint,
                StorageHubTheme.Danger,
                _pressed || _hot
                    ? StorageHubTheme.ContrastText(StorageHubTheme.Danger)
                    : StorageHubTheme.Danger),
            _ => (
                _pressed ? StorageHubTheme.Elevated
                    : _hot ? StorageHubTheme.SurfaceMuted : StorageHubTheme.Surface,
                StorageHubTheme.Border,
                StorageHubTheme.Text)
        };
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
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        _pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _pressed = false;
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

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        InvalidateMeasure();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        InvalidateMeasure();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        ApplyGlyph();
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        // The glyph is rasterised for one scaling, so it is redrawn rather than stretched.
        ApplyGlyph();
        InvalidateMeasure();
    }

    private void InvalidateMeasure()
    {
        if (IsDisposed)
        {
            return;
        }

        // AutoSize alone does not re-run for a change WinForms does not consider layout-affecting,
        // and a docked button never re-measures at all, so the size is assigned either way.
        var preferred = GetPreferredSize(System.Drawing.Size.Empty);
        if (Dock == DockStyle.None && !AutoSize)
        {
            Size = preferred;
        }
        else if (Dock is DockStyle.Left or DockStyle.Right)
        {
            Width = preferred.Width;
        }
        else if (Dock is DockStyle.Top or DockStyle.Bottom)
        {
            Height = preferred.Height;
        }

        PerformLayout();
        Parent?.PerformLayout();
    }

    private void ApplyGlyph()
    {
        if (_glyph is not { } glyph)
        {
            Image = null;
            return;
        }

        var tone = _glyphTone ?? (_variant switch
        {
            StorageHubButtonVariant.Primary => UiIconTone.OnPrimary,
            StorageHubButtonVariant.Danger => UiIconTone.Danger,
            _ => UiIconTone.Text
        });
        _ = StorageHubTheme.TrackIcon(this, glyph, GlyphSize, tone, DeviceDpi / 96F);
        InvalidateMeasure();
    }

    /// <summary>
    /// The size WinForms lays an image out at. A tracked icon is rasterised for the display's
    /// scaling but carries a matching resolution, so its pixel size overstates the room it needs.
    /// </summary>
    private static Size LogicalImageSize(Image image) => new(
        (int)Math.Round(image.Width * 96D / image.HorizontalResolution),
        (int)Math.Round(image.Height * 96D / image.VerticalResolution));
}
