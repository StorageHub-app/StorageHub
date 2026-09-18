using System.ComponentModel;
using System.Globalization;

namespace StorageHub.Desktop;

/// <summary>
/// A time of day, typed as <c>HH:mm</c> with a stepper for the part the caret is in.
///
/// It replaces the one <see cref="DateTimePicker"/> in the app. That control draws itself through
/// the system renderer -- a white box with its own spin buttons that no palette entry reaches --
/// so a schedule's time was the single pale rectangle in an otherwise dark dialog.
///
/// Only a time is offered, because a time is all the schedule editor ever asked for: the date part
/// of the value it hands back is today, exactly as the picker's was.
/// </summary>
public sealed class StorageHubTimeField : Control
{
    private readonly TextBox _editor = new()
    {
        BorderStyle = BorderStyle.None,
        AutoSize = false
    };

    private readonly System.Windows.Forms.Timer _repeat = new() { Interval = 140 };
    private DateTime _value = DateTime.Today;
    private bool _updatingText;
    private int _repeatDirection;
    private int _hotDirection;
    private bool _dense;

    public StorageHubTimeField()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        _editor.BackColor = StorageHubTheme.Input;
        _editor.ForeColor = StorageHubTheme.Text;
        _editor.TextChanged += EditorTextChanged;
        _editor.LostFocus += EditorLostFocus;
        _editor.GotFocus += FocusChanged;
        _editor.KeyDown += EditorKeyDown;
        Controls.Add(_editor);
        _repeat.Tick += (_, _) => Step(_repeatDirection);
        Height = StorageHubFieldChrome.MeasureHeight(this, _dense);
        Width = LogicalToDeviceUnits(116);
        WriteEditorText();
    }


    /// <summary>
    /// Tightens the input for a toolbar: the same shape and palette, less weight. See
    /// <see cref="StorageHubFieldChrome.DenseVerticalPadding"/>.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dense
    {
        get => _dense;
        set
        {
            if (_dense == value)
            {
                return;
            }

            _dense = value;
            Height = StorageHubFieldChrome.MeasureHeight(this, _dense);
            PerformLayout();
            Invalidate();
        }
    }
    /// <summary>Raised when the time changes, whether typed, stepped or assigned.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>
    /// The chosen time, on today's date. Assigning a value keeps only its hours and minutes, which
    /// is what every caller of this field means by it.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTime Value
    {
        get => _value;
        set
        {
            var normalised = DateTime.Today.AddHours(value.Hour).AddMinutes(value.Minute);
            if (_value == normalised)
            {
                return;
            }

            _value = normalised;
            WriteEditorText();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override Size GetPreferredSize(Size proposedSize) => new(
        Width,
        StorageHubFieldChrome.MeasureHeight(this, _dense));

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _editor.Font = Font;
        Height = StorageHubFieldChrome.MeasureHeight(this, _dense);
        PerformLayout();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _editor.Enabled = Enabled;
        _editor.BackColor = Enabled ? StorageHubTheme.Input : StorageHubTheme.SurfaceMuted;
        _editor.ForeColor = Enabled ? StorageHubTheme.Text : StorageHubTheme.DisabledText;
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = StorageHubFieldChrome.MeasureHeight(this, _dense);
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var editorHeight = Math.Min(Font.Height + 2, Math.Max(1, Height - 2));
        var left = StorageHubFieldChrome.TextInset(this, _dense);
        _editor.SetBounds(
            left,
            (Height - editorHeight) / 2,
            Math.Max(1, StepperBounds().Left - left - LogicalToDeviceUnits(4)),
            editorHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using (var backdrop = new SolidBrush(StorageHubFieldChrome.Backdrop(this)))
        {
            e.Graphics.FillRectangle(backdrop, ClientRectangle);
        }

        StorageHubFieldChrome.PaintField(e.Graphics, this, _editor.Focused, _dense);
        var stepper = StepperBounds();
        using (var clip = StorageHubFieldChrome.RoundedRectangle(
            Rectangle.FromLTRB(0, 0, Width - 1, Height - 1),
            LogicalToDeviceUnits(_dense ? StorageHubFieldChrome.DenseCornerRadius : StorageHubFieldChrome.CornerRadius)))
        {
            var saved = e.Graphics.Save();
            e.Graphics.SetClip(clip);
            using (var fill = new SolidBrush(StorageHubTheme.Elevated))
            {
                e.Graphics.FillRectangle(fill, stepper);
            }

            if (_hotDirection != 0 && Enabled)
            {
                var half = stepper.Height / 2;
                var hot = _hotDirection > 0
                    ? new Rectangle(stepper.Left, stepper.Top, stepper.Width, half)
                    : new Rectangle(stepper.Left, stepper.Top + half, stepper.Width, stepper.Height - half);
                using var brush = new SolidBrush(StorageHubTheme.Selection);
                e.Graphics.FillRectangle(brush, hot);
            }

            using (var pen = new Pen(StorageHubTheme.Border))
            {
                e.Graphics.DrawLine(pen, stepper.Left, stepper.Top, stepper.Left, stepper.Bottom);
                var middle = stepper.Top + (stepper.Height / 2);
                e.Graphics.DrawLine(pen, stepper.Left, middle, stepper.Right, middle);
            }

            e.Graphics.Restore(saved);
        }

        var arrow = Enabled ? StorageHubTheme.TextMuted : StorageHubTheme.DisabledText;
        StorageHubFieldChrome.PaintChevron(
            e.Graphics,
            new Rectangle(stepper.Left, stepper.Top, stepper.Width, stepper.Height / 2),
            arrow,
            pointingUp: true);
        StorageHubFieldChrome.PaintChevron(
            e.Graphics,
            new Rectangle(
                stepper.Left,
                stepper.Top + (stepper.Height / 2),
                stepper.Width,
                stepper.Height - (stepper.Height / 2)),
            arrow,
            pointingUp: false);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled)
        {
            return;
        }

        var direction = DirectionAt(e.Location);
        _editor.Focus();
        if (direction == 0)
        {
            return;
        }

        Step(direction);
        _repeatDirection = direction;
        _repeat.Start();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _repeat.Stop();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        var direction = DirectionAt(e.Location);
        if (direction != _hotDirection)
        {
            _hotDirection = direction;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _repeat.Stop();
        if (_hotDirection != 0)
        {
            _hotDirection = 0;
            Invalidate();
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _editor.Focus();
    }

    private Rectangle StepperBounds()
    {
        var width = StorageHubFieldChrome.TrailingZone(this, _dense);
        return Rectangle.FromLTRB(Width - width - 1, 1, Width - 1, Height - 1);
    }

    private int DirectionAt(Point point)
    {
        var stepper = StepperBounds();
        if (!stepper.Contains(point))
        {
            return 0;
        }

        return point.Y < stepper.Top + (stepper.Height / 2) ? 1 : -1;
    }

    /// <summary>
    /// Steps whichever part the caret sits in: hours before the colon, minutes after it. That is
    /// what the picker this replaces did, and it is the reason the stepper is worth having at all.
    /// </summary>
    private void Step(int direction)
    {
        if (direction == 0)
        {
            return;
        }

        var minutes = _editor.SelectionStart > 2 ? direction : direction * 60;
        Value = _value.AddMinutes(minutes);
        WriteEditorText();
    }

    private void WriteEditorText()
    {
        _updatingText = true;
        try
        {
            var caret = _editor.SelectionStart;
            _editor.Text = _value.ToString("HH:mm", CultureInfo.CurrentCulture);
            _editor.SelectionStart = Math.Min(caret, _editor.TextLength);
        }
        finally
        {
            _updatingText = false;
        }
    }

    private void EditorTextChanged(object? sender, EventArgs e)
    {
        if (_updatingText)
        {
            return;
        }

        if (DateTime.TryParseExact(
            _editor.Text,
            ["HH:mm", "H:mm", "HHmm"],
            CultureInfo.CurrentCulture,
            DateTimeStyles.None,
            out var typed))
        {
            Value = typed;
        }
    }

    /// <summary>A half-typed time is put back to the last good one when the field is left.</summary>
    private void EditorLostFocus(object? sender, EventArgs e)
    {
        WriteEditorText();
        Invalidate();
    }

    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        switch (e.KeyCode)
        {
            case Keys.Up:
                Step(1);
                e.Handled = true;
                break;
            case Keys.Down:
                Step(-1);
                e.Handled = true;
                break;
        }
    }

    private void FocusChanged(object? sender, EventArgs e) => Invalidate();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor.TextChanged -= EditorTextChanged;
            _editor.LostFocus -= EditorLostFocus;
            _editor.GotFocus -= FocusChanged;
            _editor.KeyDown -= EditorKeyDown;
            _repeat.Stop();
            _repeat.Dispose();
        }

        base.Dispose(disposing);
    }
}
