using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace StorageHub.Desktop;

/// <summary>
/// A single-line text input with the app's own chrome: a rounded border the palette owns, real
/// padding inside it, an optional leading glyph, and an optional clear button.
///
/// The editing itself is still a <see cref="TextBox"/>, parented inside and stripped of its
/// border. That is deliberate: caret placement, selection, IME composition, undo, the context menu
/// and every accessibility affordance are worth more than the few pixels a fully hand-drawn field
/// would win, and a borderless text box hands all of it over while leaving the surface to us.
/// </summary>
public sealed class StorageHubTextField : Control
{
    private readonly TextBox _editor = new()
    {
        BorderStyle = BorderStyle.None,
        AutoSize = false
    };

    private UiGlyph? _glyph;
    private bool _showClearButton;
    private bool _clearHot;
    private bool _dense;

    public StorageHubTextField()
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
        _editor.GotFocus += FocusChanged;
        _editor.LostFocus += FocusChanged;
        _editor.TextChanged += EditorTextChanged;
        _editor.KeyDown += EditorKeyDown;
        _editor.KeyUp += EditorKeyUp;
        _editor.KeyPress += EditorKeyPress;
        Controls.Add(_editor);
        Height = StorageHubFieldChrome.MeasureHeight(this, _dense);
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
    /// <summary>The text box inside, for the few callers that need the editing surface itself.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal TextBox Editor => _editor;

    [DefaultValue(null)]
    public string? PlaceholderText
    {
        get => _editor.PlaceholderText;
        set => _editor.PlaceholderText = value ?? string.Empty;
    }

    [DefaultValue(0)]
    public int MaxLength
    {
        get => _editor.MaxLength;
        set => _editor.MaxLength = value;
    }

    [DefaultValue(false)]
    public bool ReadOnly
    {
        get => _editor.ReadOnly;
        set => _editor.ReadOnly = value;
    }

    [DefaultValue(false)]
    public bool UseSystemPasswordChar
    {
        get => _editor.UseSystemPasswordChar;
        set => _editor.UseSystemPasswordChar = value;
    }

    /// <summary>An optional glyph before the text: a magnifier on a search box, for instance.</summary>
    [DefaultValue(null)]
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
            PerformLayout();
            Invalidate();
        }
    }

    /// <summary>Shows an X at the trailing edge while there is something to clear.</summary>
    [DefaultValue(false)]
    public bool ShowClearButton
    {
        get => _showClearButton;
        set
        {
            if (_showClearButton == value)
            {
                return;
            }

            _showClearButton = value;
            PerformLayout();
            Invalidate();
        }
    }

    [Browsable(true)]
    [AllowNull]
    public override string Text
    {
        get => _editor.Text;
        set => _editor.Text = value ?? string.Empty;
    }

    /// <summary>Empties the field, as <see cref="TextBoxBase.Clear"/> does.</summary>
    public void Clear() => _editor.Clear();

    /// <summary>Selects everything in the field, for a dialog that opens on an editable value.</summary>
    public void SelectAll() => _editor.SelectAll();

    /// <summary>Selects a range, as a text box does.</summary>
    public void Select(int start, int length) => _editor.Select(start, length);

    public override Size GetPreferredSize(Size proposedSize) => new(
        proposedSize.Width > 0 ? proposedSize.Width : Width,
        StorageHubFieldChrome.MeasureHeight(this, _dense));

    /// <summary>Clicking anywhere in the chrome puts the caret in the text, as a native field does.</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (ClearButtonBounds() is { } clear && clear.Contains(e.Location))
        {
            _editor.Clear();
            _editor.Focus();
            return;
        }

        _editor.Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        var hot = ClearButtonBounds() is { } clear && clear.Contains(e.Location);
        if (hot != _clearHot)
        {
            _clearHot = hot;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_clearHot)
        {
            _clearHot = false;
            Invalidate();
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _editor.Focus();
    }

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
        var padding = StorageHubFieldChrome.TextInset(this, _dense);
        var left = padding;
        if (_glyph is not null)
        {
            left += LogicalToDeviceUnits(16) + LogicalToDeviceUnits(8);
        }

        var right = Width - padding;
        if (ClearButtonBounds() is { } clear)
        {
            right = clear.Left - LogicalToDeviceUnits(4);
        }

        var editorHeight = Math.Min(Font.Height + 2, Math.Max(1, Height - 2));
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

        if (_glyph is { } glyph)
        {
            var size = LogicalToDeviceUnits(16);
            using var image = UiIconFactory.Create(
                glyph,
                Enabled ? StorageHubTheme.TextMuted : StorageHubTheme.DisabledText,
                16,
                DeviceDpi / 96F);
            e.Graphics.DrawImage(
                image,
                new Rectangle(
                    StorageHubFieldChrome.TextInset(this, _dense),
                    (Height - size) / 2,
                    size,
                    size));
        }

        if (ClearButtonBounds() is { } clear)
        {
            var color = _clearHot ? StorageHubTheme.Text : StorageHubTheme.TextMuted;
            var size = LogicalToDeviceUnits(10);
            var box = new Rectangle(
                clear.Left + ((clear.Width - size) / 2),
                clear.Top + ((clear.Height - size) / 2),
                size,
                size);
            using var pen = new Pen(color, 1.4F);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLine(pen, box.Left, box.Top, box.Right, box.Bottom);
            e.Graphics.DrawLine(pen, box.Right, box.Top, box.Left, box.Bottom);
        }
    }

    private Rectangle? ClearButtonBounds()
    {
        if (!_showClearButton || _editor.TextLength == 0)
        {
            return null;
        }

        var zone = StorageHubFieldChrome.TrailingZone(this, _dense);
        return new Rectangle(Width - zone - LogicalToDeviceUnits(4), 0, zone, Height);
    }

    private void EditorTextChanged(object? sender, EventArgs e)
    {
        if (_showClearButton)
        {
            PerformLayout();
            Invalidate();
        }

        OnTextChanged(e);
    }

    private void EditorKeyDown(object? sender, KeyEventArgs e) => OnKeyDown(e);

    private void EditorKeyUp(object? sender, KeyEventArgs e) => OnKeyUp(e);

    private void EditorKeyPress(object? sender, KeyPressEventArgs e) => OnKeyPress(e);

    private void FocusChanged(object? sender, EventArgs e) => Invalidate();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor.GotFocus -= FocusChanged;
            _editor.LostFocus -= FocusChanged;
            _editor.TextChanged -= EditorTextChanged;
            _editor.KeyDown -= EditorKeyDown;
            _editor.KeyUp -= EditorKeyUp;
            _editor.KeyPress -= EditorKeyPress;
        }

        base.Dispose(disposing);
    }
}
