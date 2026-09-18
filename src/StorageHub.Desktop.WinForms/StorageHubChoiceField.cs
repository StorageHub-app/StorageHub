using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace StorageHub.Desktop;

/// <summary>
/// A drop-down list wearing the app's field chrome, with a themed popup instead of the system one.
///
/// A <see cref="ComboBox"/> could not be brought this far. Owner-drawing reaches its list but not
/// its closed face: the border and the arrow button come from the system renderer, which is why
/// every combo in the app used to sit in a pale square box that no palette entry controlled, a few
/// pixels taller than the text field beside it. Here the closed face is painted like every other
/// input, and the list is an ordinary popup window we own.
///
/// <see cref="Editable"/> turns it into the combo's other mode -- type or pick -- which the
/// terminal-type and font-family settings need.
/// </summary>
public sealed class StorageHubChoiceField : Control
{
    private readonly List<object> _items = [];
    private readonly TextBox _editor = new()
    {
        BorderStyle = BorderStyle.None,
        AutoSize = false,
        Visible = false
    };

    private ToolStripDropDown? _popup;
    private ListBox? _list;
    private int _selectedIndex = -1;
    private bool _editable;
    private bool _chevronHot;
    private bool _syncingEditor;
    private bool _dense;

    public StorageHubChoiceField()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.Selectable,
            true);
        BackColor = Color.Transparent;
        TabStop = true;
        Cursor = Cursors.Hand;
        _editor.BackColor = StorageHubTheme.Input;
        _editor.ForeColor = StorageHubTheme.Text;
        _editor.TextChanged += EditorTextChanged;
        _editor.GotFocus += FocusChanged;
        _editor.LostFocus += FocusChanged;
        Controls.Add(_editor);
        Items = new ItemCollection(this);
        Height = StorageHubFieldChrome.MeasureHeight(this, _dense);
        Width = LogicalToDeviceUnits(260);
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
    /// <summary>Raised when the selection changes, however it changed.</summary>
    public event EventHandler? SelectedIndexChanged;

    /// <summary>The entries, in the order they are offered.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ItemCollection Items { get; }

    /// <summary>
    /// How an entry is shown. It is a function rather than a display-member name because the
    /// captions here are localized strings chosen per entry, not a property on a data object.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<object, string>? DisplayText { get; set; }

    [DefaultValue(-1)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = value < -1 || value >= _items.Count ? -1 : value;
            if (_selectedIndex == clamped)
            {
                return;
            }

            _selectedIndex = clamped;
            SyncEditorFromSelection();
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object? SelectedItem
    {
        get => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;
        set => SelectedIndex = value is null ? -1 : _items.FindIndex(item => Equals(item, value));
    }

    /// <summary>Whether a value the list does not offer can be typed in.</summary>
    [DefaultValue(false)]
    public bool Editable
    {
        get => _editable;
        set
        {
            if (_editable == value)
            {
                return;
            }

            _editable = value;
            _editor.Visible = value;
            Cursor = value ? Cursors.IBeam : Cursors.Hand;
            SyncEditorFromSelection();
            PerformLayout();
            Invalidate();
        }
    }

    [Browsable(true)]
    [AllowNull]
    public override string Text
    {
        get => _editable ? _editor.Text : Caption(SelectedItem);
        set
        {
            if (_editable)
            {
                _editor.Text = value ?? string.Empty;
            }
        }
    }

    [DefaultValue(0)]
    public int MaxLength
    {
        get => _editor.MaxLength;
        set => _editor.MaxLength = value;
    }

    /// <summary>
    /// What an entry shows as. Named after <see cref="ComboBox.GetItemText(object)"/> because it
    /// answers the same question, and because asking the control is the only honest way to test
    /// what a reader actually sees.
    /// </summary>
    public string GetItemText(object? item) => Caption(item);

    public override Size GetPreferredSize(Size proposedSize) => new(
        Width,
        StorageHubFieldChrome.MeasureHeight(this, _dense));

    protected override AccessibleObject CreateAccessibilityInstance() => new ChoiceAccessibleObject(this);

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
        var editorHeight = Math.Min(Font.Height + 2, Math.Max(1, Height - 2));
        _editor.SetBounds(
            padding,
            (Height - editorHeight) / 2,
            Math.Max(1, ChevronBounds().Left - padding),
            editorHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using (var backdrop = new SolidBrush(StorageHubFieldChrome.Backdrop(this)))
        {
            e.Graphics.FillRectangle(backdrop, ClientRectangle);
        }

        StorageHubFieldChrome.PaintField(e.Graphics, this, Focused || _editor.Focused || _popup?.Visible == true, _dense);

        var chevron = ChevronBounds();
        if (!_editable)
        {
            var padding = StorageHubFieldChrome.TextInset(this, _dense);
            TextRenderer.DrawText(
                e.Graphics,
                Caption(SelectedItem),
                Font,
                Rectangle.FromLTRB(padding, 0, chevron.Left, Height),
                Enabled ? StorageHubTheme.Text : StorageHubTheme.DisabledText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        StorageHubFieldChrome.PaintChevron(
            e.Graphics,
            chevron,
            !Enabled ? StorageHubTheme.DisabledText
                : _chevronHot ? StorageHubTheme.Text : StorageHubTheme.TextMuted,
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

        // In editable mode only the chevron opens the list, so clicking the text still places the
        // caret; a list-only field opens wherever it is clicked, as a combo box does.
        if (_editable && !ChevronBounds().Contains(e.Location))
        {
            _editor.Focus();
            return;
        }

        Focus();
        Toggle();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        var hot = ChevronBounds().Contains(e.Location);
        if (hot != _chevronHot)
        {
            _chevronHot = hot;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_chevronHot)
        {
            _chevronHot = false;
            Invalidate();
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (!Enabled || _items.Count == 0)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Down when e.Alt:
            case Keys.F4:
                Toggle();
                e.Handled = true;
                break;
            case Keys.Down:
                SelectedIndex = Math.Min(_items.Count - 1, _selectedIndex + 1);
                e.Handled = true;
                break;
            case Keys.Up:
                SelectedIndex = Math.Max(0, _selectedIndex - 1);
                e.Handled = true;
                break;
            case Keys.Home:
                SelectedIndex = 0;
                e.Handled = true;
                break;
            case Keys.End:
                SelectedIndex = _items.Count - 1;
                e.Handled = true;
                break;
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

    private Rectangle ChevronBounds()
    {
        var width = StorageHubFieldChrome.TrailingZone(this, _dense);
        return Rectangle.FromLTRB(Width - width - LogicalToDeviceUnits(4), 0, Width, Height);
    }

    private string Caption(object? item) => item is null
        ? string.Empty
        : DisplayText?.Invoke(item) ?? item.ToString() ?? string.Empty;

    private void Toggle()
    {
        if (_popup?.Visible == true)
        {
            _popup.Close();
            return;
        }

        Open();
    }

    /// <summary>
    /// The list is a <see cref="ToolStripDropDown"/> so that it behaves like a real drop-down --
    /// above every other control, dismissed by a click anywhere else or by Escape -- rather than
    /// like a panel that has to guess when it stopped being wanted.
    /// </summary>
    private void Open()
    {
        if (_items.Count == 0)
        {
            return;
        }

        if (_popup is null)
        {
            _list = new ListBox
            {
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                BackColor = StorageHubTheme.Elevated,
                ForeColor = StorageHubTheme.Text,
                DrawMode = DrawMode.OwnerDrawFixed,
                Font = Font
            };
            _list.DrawItem += DrawListItem;
            _list.Click += ListClicked;
            _list.KeyDown += ListKeyDown;
            _popup = new ToolStripDropDown
            {
                AutoSize = false,
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                DropShadowEnabled = true,
                BackColor = StorageHubTheme.Elevated
            };
            _popup.Items.Add(new ToolStripControlHost(_list)
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false
            });
            _popup.Closed += (_, _) => Invalidate();
        }

        _list!.BackColor = StorageHubTheme.Elevated;
        _list.ForeColor = StorageHubTheme.Text;
        _list.ItemHeight = Font.Height + LogicalToDeviceUnits(10);
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in _items)
        {
            _list.Items.Add(Caption(item));
        }

        _list.EndUpdate();
        _list.SelectedIndex = _selectedIndex;

        var rows = Math.Min(_items.Count, 12);
        var size = new Size(Width, (rows * _list.ItemHeight) + 2);
        _list.Size = size;
        _popup!.Items[0].Size = size;
        _popup.Size = size;
        _popup.Show(this, new Point(0, Height + 2));
        _list.Focus();
    }

    private void DrawListItem(object? sender, DrawItemEventArgs e)
    {
        if (_list is null || e.Index < 0)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using (var fill = new SolidBrush(selected ? StorageHubTheme.Selection : StorageHubTheme.Elevated))
        {
            e.Graphics.FillRectangle(fill, e.Bounds);
        }

        TextRenderer.DrawText(
            e.Graphics,
            _list.Items[e.Index].ToString(),
            Font,
            Rectangle.FromLTRB(
                e.Bounds.Left + StorageHubFieldChrome.TextInset(this, _dense),
                e.Bounds.Top,
                e.Bounds.Right - LogicalToDeviceUnits(6),
                e.Bounds.Bottom),
            StorageHubTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }

    private void ListClicked(object? sender, EventArgs e) => Commit();

    private void ListKeyDown(object? sender, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        switch (e.KeyCode)
        {
            case Keys.Enter:
                Commit();
                e.Handled = true;
                break;
            case Keys.Escape:
                _popup?.Close();
                Focus();
                e.Handled = true;
                break;
        }
    }

    private void Commit()
    {
        if (_list is { SelectedIndex: >= 0 } list)
        {
            SelectedIndex = list.SelectedIndex;
        }

        _popup?.Close();
        Focus();
    }

    private void SyncEditorFromSelection()
    {
        if (!_editable || SelectedItem is null)
        {
            return;
        }

        _syncingEditor = true;
        try
        {
            _editor.Text = Caption(SelectedItem);
        }
        finally
        {
            _syncingEditor = false;
        }
    }

    private void EditorTextChanged(object? sender, EventArgs e)
    {
        if (!_syncingEditor)
        {
            OnTextChanged(e);
        }
    }

    private void FocusChanged(object? sender, EventArgs e) => Invalidate();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor.TextChanged -= EditorTextChanged;
            _editor.GotFocus -= FocusChanged;
            _editor.LostFocus -= FocusChanged;
            if (_list is not null)
            {
                _list.DrawItem -= DrawListItem;
                _list.Click -= ListClicked;
                _list.KeyDown -= ListKeyDown;
            }

            _popup?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// A thin <see cref="IList"/> over the field's own list, so callers can keep writing
    /// <c>Items.Add(...)</c> and <c>Items.AddRange(...)</c> as they did with a combo box.
    /// </summary>
    public sealed class ItemCollection(StorageHubChoiceField owner) : IList, IList<object>
    {
        // The generic half is here only because a publicly visible collection is expected to carry
        // it; every caller uses the non-generic members, which is what a combo box's own
        // collection offers.
        void ICollection<object>.Add(object item) => _ = Add(item);

        bool ICollection<object>.Remove(object item)
        {
            var found = owner._items.Remove(item);
            owner.Invalidate();
            return found;
        }

        public void CopyTo(object[] array, int arrayIndex) => owner._items.CopyTo(array, arrayIndex);

        IEnumerator<object> IEnumerable<object>.GetEnumerator() => owner._items.GetEnumerator();

        /// <summary>Appends several entries at once, as a combo box's collection does.</summary>
        public void AddRange(params object[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            owner._items.AddRange(values);
            owner.Invalidate();
        }

        public object this[int index]
        {
            get => owner._items[index];
            set
            {
                owner._items[index] = value;
                owner.Invalidate();
            }
        }

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = value!;
        }

        public bool IsFixedSize => false;

        public bool IsReadOnly => false;

        public int Count => owner._items.Count;

        public bool IsSynchronized => false;

        public object SyncRoot => owner._items;

        public int Add(object? value)
        {
            owner._items.Add(value!);
            owner.Invalidate();
            return owner._items.Count - 1;
        }

        public void Clear()
        {
            owner._items.Clear();
            owner.SelectedIndex = -1;
            owner.Invalidate();
        }

        public bool Contains(object? value) => owner._items.Contains(value!);

        public void CopyTo(Array array, int index) => ((ICollection)owner._items).CopyTo(array, index);

        public IEnumerator GetEnumerator() => owner._items.GetEnumerator();

        public int IndexOf(object? value) => owner._items.IndexOf(value!);

        public void Insert(int index, object? value)
        {
            owner._items.Insert(index, value!);
            owner.Invalidate();
        }

        public void Remove(object? value)
        {
            _ = owner._items.Remove(value!);
            owner.Invalidate();
        }

        public void RemoveAt(int index)
        {
            owner._items.RemoveAt(index);
            owner.Invalidate();
        }
    }

    private sealed class ChoiceAccessibleObject(StorageHubChoiceField owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.ComboBox;

        public override string? Value => owner.Text;
    }
}
