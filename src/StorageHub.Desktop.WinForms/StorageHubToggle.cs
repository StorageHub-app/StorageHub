using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace StorageHub.Desktop;

/// <summary>
/// An on/off switch: the control a settings row uses where a <see cref="CheckBox"/> used to sit.
///
/// A check box carries its own caption, which is why the settings pages ended up with two kinds of
/// row -- one where the label was part of the control and one where it was a separate column --
/// and no way to line the two up. A switch has no caption at all: the row owns the words, the
/// switch owns one state, and every row can therefore share a single layout.
///
/// It is a <see cref="Control"/> rather than a restyled <see cref="CheckBox"/> because a check box
/// paints its box through the system renderer at a size the theme picks, so the one thing that had
/// to change could not be changed.
/// </summary>
public sealed class StorageHubToggle : Control
{
    /// <summary>Track size before scaling. A 40x22 track with a 14px knob, as the design sets it.</summary>
    private const int TrackWidth = 40;
    private const int TrackHeight = 22;
    private const int KnobSize = 14;
    private const int KnobInset = 3;

    private bool _checked;

    public StorageHubToggle()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.Selectable,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
        Size = GetPreferredSize(Size.Empty);
    }

    /// <summary>Raised when the switch changes state, whoever changed it.</summary>
    public event EventHandler? CheckedChanged;

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override Size DefaultSize => new(TrackWidth, TrackHeight);

    public override Size GetPreferredSize(Size proposedSize) => new(
        LogicalToDeviceUnits(TrackWidth),
        LogicalToDeviceUnits(TrackHeight));

    /// <summary>
    /// Announced as a check box, because that is the role assistive technology has a concept of:
    /// a "switch" is a visual treatment, not a different thing to a screen reader.
    /// </summary>
    protected override AccessibleObject CreateAccessibilityInstance() => new ToggleAccessibleObject(this);

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var graphics = e.Graphics;
        using (var backdrop = new SolidBrush(StorageHubFieldChrome.Backdrop(this)))
        {
            graphics.FillRectangle(backdrop, ClientRectangle);
        }

        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new Rectangle(
            0,
            0,
            LogicalToDeviceUnits(TrackWidth) - 1,
            LogicalToDeviceUnits(TrackHeight) - 1);
        var knob = LogicalToDeviceUnits(KnobSize);
        var inset = LogicalToDeviceUnits(KnobInset);
        using (var path = StorageHubFieldChrome.RoundedRectangle(track, track.Height / 2))
        {
            var fill = !Enabled
                ? StorageHubTheme.SurfaceMuted
                : _checked ? StorageHubTheme.Primary : Color.Transparent;
            if (fill.A > 0)
            {
                using var brush = new SolidBrush(fill);
                graphics.FillPath(brush, path);
            }

            using var pen = new Pen(!Enabled
                ? StorageHubTheme.Border
                : _checked ? StorageHubTheme.Primary : StorageHubTheme.TextMuted);
            graphics.DrawPath(pen, path);
        }

        var knobColor = !Enabled
            ? StorageHubTheme.DisabledText
            : _checked ? StorageHubTheme.ContrastText(StorageHubTheme.Primary) : StorageHubTheme.TextMuted;
        var knobLeft = _checked
            ? track.Right - inset - knob + 1
            : track.Left + inset;
        using (var knobBrush = new SolidBrush(knobColor))
        {
            graphics.FillEllipse(
                knobBrush,
                knobLeft,
                track.Top + ((track.Height - knob) / 2) + 1,
                knob,
                knob);
        }

        if (Focused)
        {
            // Drawn inside the track: a ring around it would overlap the row's text.
            using var focus = new Pen(StorageHubTheme.Primary) { DashStyle = DashStyle.Dot };
            using var path = StorageHubFieldChrome.RoundedRectangle(
                Rectangle.Inflate(track, -LogicalToDeviceUnits(2), -LogicalToDeviceUnits(2)),
                Math.Max(1, (track.Height - LogicalToDeviceUnits(4)) / 2));
            graphics.DrawPath(focus, path);
        }

        graphics.SmoothingMode = previous;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Enabled)
        {
            Focus();
            Checked = !Checked;
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData == Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space && Enabled)
        {
            Checked = !Checked;
            e.Handled = true;
        }
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

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Size = GetPreferredSize(Size.Empty);
    }

    private sealed class ToggleAccessibleObject(StorageHubToggle owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.CheckButton;

        public override AccessibleStates State => owner.Checked
            ? base.State | AccessibleStates.Checked
            : base.State;

        public override void DoDefaultAction() => owner.Checked = !owner.Checked;
    }
}
