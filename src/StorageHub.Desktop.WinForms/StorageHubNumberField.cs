using System.ComponentModel;
using System.Globalization;

namespace StorageHub.Desktop;

/// <summary>
/// A number input: the value right-aligned against its own column, an optional unit after it, and
/// a stepper in a trailing zone of its own.
///
/// It replaces <see cref="NumericUpDown"/>, whose spin buttons are a separate child window that
/// keeps painting on the system background -- a bright block on a dark input that no amount of
/// theming reached -- and which measures a couple of pixels shorter than every control beside it.
/// Right-aligning the value is the point of the layout: a column of settings then has one straight
/// edge of numbers to read down, instead of values that start wherever their digits happen to.
/// </summary>
public sealed class StorageHubNumberField : Control
{
    private readonly TextBox _editor = new()
    {
        BorderStyle = BorderStyle.None,
        AutoSize = false,
        TextAlign = HorizontalAlignment.Right
    };

    private readonly System.Windows.Forms.Timer _repeat = new() { Interval = 120 };
    private decimal _value;
    private decimal _minimum;
    private decimal _maximum = 100;
    private decimal _increment = 1;
    private int _decimalPlaces;
    private bool _thousandsSeparator;
    private string? _unit;
    private bool _updatingText;
    private int _repeatDirection;
    private int _hotDirection;
    private bool _dense;

    public StorageHubNumberField()
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
    /// <summary>Raised when the value changes, whether typed, stepped or assigned.</summary>
    public event EventHandler? ValueChanged;

    [DefaultValue(typeof(decimal), "0")]
    public decimal Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _minimum, _maximum);
            if (_value == clamped)
            {
                return;
            }

            _value = clamped;
            WriteEditorText();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum < _minimum)
            {
                _maximum = _minimum;
            }

            Value = Math.Clamp(_value, _minimum, _maximum);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value;
            if (_minimum > _maximum)
            {
                _minimum = _maximum;
            }

            Value = Math.Clamp(_value, _minimum, _maximum);
        }
    }

    [DefaultValue(typeof(decimal), "1")]
    public decimal Increment
    {
        get => _increment;
        set => _increment = value <= 0 ? 1 : value;
    }

    [DefaultValue(0)]
    public int DecimalPlaces
    {
        get => _decimalPlaces;
        set
        {
            _decimalPlaces = Math.Clamp(value, 0, 4);
            WriteEditorText();
        }
    }

    [DefaultValue(false)]
    public bool ThousandsSeparator
    {
        get => _thousandsSeparator;
        set
        {
            _thousandsSeparator = value;
            WriteEditorText();
        }
    }

    /// <summary>
    /// A short unit shown after the value -- "job", "KiB", "ms". It lives in the field rather than
    /// in the label so the row's title can be the setting's name alone.
    /// </summary>
    [DefaultValue(null)]
    public string? Unit
    {
        get => _unit;
        set
        {
            if (string.Equals(_unit, value, StringComparison.Ordinal))
            {
                return;
            }

            _unit = string.IsNullOrWhiteSpace(value) ? null : value;
            PerformLayout();
            Invalidate();
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
        var inset = LogicalToDeviceUnits(2);
        var editorHeight = Math.Min(Font.Height + inset, Math.Max(1, Height - inset));
        var left = StorageHubFieldChrome.TextInset(this, _dense);
        var right = StepperBounds().Left - UnitWidth() - LogicalToDeviceUnits(4);
        _editor.SetBounds(
            left,
            (Height - editorHeight) / 2,
            Math.Max(1, right - left),
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
        if (_unit is { } unit)
        {
            TextRenderer.DrawText(
                e.Graphics,
                unit,
                Font,
                Rectangle.FromLTRB(_editor.Right, 0, stepper.Left, Height),
                Enabled ? StorageHubTheme.TextMuted : StorageHubTheme.DisabledText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        // The stepper reads as a zone of the field rather than as two buttons dropped on it, so it
        // carries one dividing line and the same corner radius as the field it sits in.
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
                var hot = _hotDirection > 0
                    ? new Rectangle(stepper.Left, stepper.Top, stepper.Width, stepper.Height / 2)
                    : new Rectangle(
                        stepper.Left,
                        stepper.Top + (stepper.Height / 2),
                        stepper.Width,
                        stepper.Height - (stepper.Height / 2));
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
        if (direction == 0)
        {
            _editor.Focus();
            return;
        }

        _editor.Focus();
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

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseWheel(e);
        if (_editor.Focused && e.Delta != 0)
        {
            Step(Math.Sign(e.Delta));
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

    private int UnitWidth() => _unit is null
        ? 0
        : TextRenderer.MeasureText(_unit, Font).Width + LogicalToDeviceUnits(6);

    private int DirectionAt(Point point)
    {
        var stepper = StepperBounds();
        if (!stepper.Contains(point))
        {
            return 0;
        }

        return point.Y < stepper.Top + (stepper.Height / 2) ? 1 : -1;
    }

    private void Step(int direction)
    {
        if (direction == 0)
        {
            return;
        }

        Value = Math.Clamp(_value + (_increment * direction), _minimum, _maximum);
        // Assigning an already-clamped value raises nothing, so the text is refreshed either way:
        // stepping at the limit must not leave a half-typed number on screen.
        WriteEditorText();
    }

    private void WriteEditorText()
    {
        _updatingText = true;
        try
        {
            var format = _thousandsSeparator ? "N" : "F";
            _editor.Text = _value.ToString(
                format + _decimalPlaces.ToString(CultureInfo.InvariantCulture),
                CultureInfo.CurrentCulture);
        }
        finally
        {
            _updatingText = false;
        }
    }

    /// <summary>
    /// Typing is accepted as it happens but never rewritten mid-edit: a field that reformats on
    /// every keystroke moves the caret out from under the typist. Out-of-range text is clamped
    /// when the field is left.
    /// </summary>
    private void EditorTextChanged(object? sender, EventArgs e)
    {
        if (_updatingText)
        {
            return;
        }

        if (TryParse(_editor.Text, out var typed))
        {
            var clamped = Math.Clamp(typed, _minimum, _maximum);
            if (_value != clamped)
            {
                _value = clamped;
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

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
            case Keys.PageUp:
                Step(10);
                e.Handled = true;
                break;
            case Keys.PageDown:
                Step(-10);
                e.Handled = true;
                break;
        }
    }

    private static bool TryParse(string text, out decimal value) => decimal.TryParse(
        text,
        NumberStyles.Number,
        CultureInfo.CurrentCulture,
        out value);

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
