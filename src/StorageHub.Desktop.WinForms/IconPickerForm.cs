using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Chooses one of the built-in icons, or clears the choice so the default is used again.
///
/// A grid of drawn glyphs rather than a drop-down of names: an icon is recognised by sight, and a
/// list of words like "layers" and "checksum" makes the reader translate each one back into a
/// picture before deciding.
/// </summary>
internal sealed class IconPickerForm : Form
{
    private const int CellSize = 52;
    private const int IconSize = 24;
    private const int Columns = 7;

    private readonly Color _accent;
    private readonly List<IconCell> _cells = [];
    private string? _selectedKey;

    internal IconPickerForm(string? currentKey, Color accent, string title)
    {
        _selectedKey = currentKey;
        _accent = accent;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = StorageHubTheme.Surface;
        StorageHubTheme.Register(this);
        AutoScaleMode = AutoScaleMode.Dpi;

        var grid = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            AutoScroll = true,
            BackColor = StorageHubTheme.Surface
        };

        foreach (var (key, glyph) in ConnectionIconCatalog.Choices)
        {
            var cell = new IconCell(key, glyph, accent)
            {
                Selected = string.Equals(key, currentKey, StringComparison.OrdinalIgnoreCase)
            };
            cell.Chosen += (_, _) => Choose(cell);
            _cells.Add(cell);
            grid.Controls.Add(cell);
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(12, 8, 12, 12),
            BackColor = StorageHubTheme.Surface
        };

        var ok = new StorageHubButton
        {
            Text = Ui.Connections.IconPickerUse,
            Variant = StorageHubButtonVariant.Primary,
            DialogResult = DialogResult.OK,
            Margin = new Padding(6, 0, 0, 0)
        };
        var cancel = new StorageHubButton
        {
            Text = Ui.Connections.IconPickerCancel,
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(6, 0, 0, 0)
        };

        // Clearing is the way back to the provider's own icon, which is what every profile shows
        // until somebody chooses otherwise.
        var clear = new StorageHubButton
        {
            Text = Ui.Connections.IconPickerUseDefault,
            Margin = new Padding(6, 0, 0, 0)
        };
        clear.Click += (_, _) =>
        {
            _selectedKey = null;
            foreach (var cell in _cells)
            {
                cell.Selected = false;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(clear);
        Controls.Add(grid);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(
            (Columns * CellSize) + 40,
            (((ConnectionIconCatalog.Choices.Count + Columns - 1) / Columns) * CellSize) + 90);
        StorageHubTheme.Apply(this);
    }

    /// <summary>The chosen key, or null to fall back to the provider's own icon.</summary>
    internal string? SelectedKey => _selectedKey;

    private void Choose(IconCell chosen)
    {
        _selectedKey = chosen.Key;
        foreach (var cell in _cells)
        {
            cell.Selected = ReferenceEquals(cell, chosen);
        }
    }

    private sealed class IconCell : Control
    {
        private readonly UiGlyph _glyph;
        private readonly Color _accent;
        private bool _hovered;
        private bool _selected;

        internal IconCell(string key, UiGlyph glyph, Color accent)
        {
            Key = key;
            _glyph = glyph;
            _accent = accent;
            Size = new Size(CellSize - 6, CellSize - 6);
            Margin = new Padding(3);
            Cursor = Cursors.Hand;
            TabStop = true;
            DoubleBuffered = true;
            AccessibleRole = AccessibleRole.RadioButton;
            AccessibleName = key;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        internal string Key { get; }

        internal event EventHandler? Chosen;

        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value)
                {
                    return;
                }

                _selected = value;
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
            Chosen?.Invoke(this, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Space or Keys.Enter)
            {
                e.Handled = true;
                Chosen?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            using (var shape = UiShapes.RoundedRectangle(bounds, 8F))
            {
                using var fill = new SolidBrush(_selected
                    ? Color.FromArgb(60, _accent)
                    : _hovered || Focused ? StorageHubTheme.SurfaceMuted : StorageHubTheme.Surface);
                e.Graphics.FillPath(fill, shape);
                if (_selected || _hovered || Focused)
                {
                    using var edge = new Pen(_selected ? _accent : StorageHubTheme.Border, _selected ? 1.8F : 1F);
                    e.Graphics.DrawPath(edge, shape);
                }
            }

            using var icon = UiIconFactory.Create(
                _glyph,
                _selected ? _accent : StorageHubTheme.Text,
                IconSize,
                DeviceDpi / 96F);
            e.Graphics.DrawImage(icon, (Width - IconSize) / 2, (Height - IconSize) / 2, IconSize, IconSize);
        }
    }
}
