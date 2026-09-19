using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// Arranges the main toolbar: which commands appear, in what order, and whether they are labelled.
///
/// The commands come from <see cref="UiCommandCatalog"/> rather than a list of their own, so every
/// command the menus offer can be put on the toolbar and none can drift out of step with its menu
/// entry's icon or name.
/// </summary>
internal sealed class ToolbarSettingsControl : UserControl
{
    private readonly ListBox _available = new();
    private readonly ListBox _current = new();
    private readonly StorageHubChoiceField _labels = new();
    private readonly List<string> _layout;

    internal ToolbarSettingsControl(IReadOnlyList<string>? items, ToolbarLabelStyle labels)
    {
        _layout = [.. ToolbarLayout.Resolve(items)];
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        // Tall enough for a usable list, short enough that the whole editor -- including the
        // reset buttons under it -- fits the settings page without scrolling at the default size.
        // The card's own padding is part of it, which is why this is not simply the grid's height.
        Height = LogicalToDeviceUnits(500);
        Width = LogicalToDeviceUnits(700);
        Margin = Padding.Empty;
        AccessibleName = Ui.Settings.ToolbarAccessibleName;

        StyleList(_available, "ToolbarAvailableCommands", Ui.Settings.ToolbarAvailableAccessibleName);
        _available.DisplayMember = nameof(CommandChoice.Label);
        _available.DoubleClick += (_, _) => AddSelected();

        StyleList(_current, "ToolbarCurrentItems", Ui.Settings.ToolbarCurrentAccessibleName);
        _current.DoubleClick += (_, _) => RemoveSelected();

        _labels.Name = "ToolbarLabelStyle";
        _labels.Width = LogicalToDeviceUnits(220);
        _labels.AccessibleName = Ui.Settings.ToolbarLabelsAccessibleName;
        _labels.Items.Add(Ui.Settings.ToolbarLabelsIconsOnly);
        _labels.Items.Add(Ui.Settings.ToolbarLabelsIconsAndText);
        _labels.Items.Add(Ui.Settings.ToolbarLabelsTextUnderIcon);
        _labels.SelectedIndex = (int)labels;

        Controls.Add(BuildLayout());
        RefreshAvailable();
        RefreshCurrent();
    }

    /// <summary>Raised whenever the layout or the label style changed, so Settings can enable Apply.</summary>
    internal event EventHandler? Changed;

    internal IReadOnlyList<string> ReadItems() => [.. _layout];

    internal ToolbarLabelStyle ReadLabels() => _labels.SelectedIndex >= 0
        ? (ToolbarLabelStyle)_labels.SelectedIndex
        : ToolbarLabelStyle.IconsOnly;

    private TableLayoutPanel BuildLayout()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            // The card behind it is painted by this control, so the grid itself carries no colour.
            BackColor = Color.Transparent,
            Padding = this.LogicalToDeviceUnits(new Padding(16, 14, 16, 14))
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(170)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, this.TextBoxHeight(5)));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(96)));

        grid.Controls.Add(Caption(Ui.Settings.ToolbarAvailable), 0, 0);
        grid.Controls.Add(Caption(Ui.Settings.ToolbarCurrent), 2, 0);
        grid.Controls.Add(new StorageHubFieldHost(_available) { Dock = DockStyle.Fill }, 0, 1);
        grid.Controls.Add(new StorageHubFieldHost(_current) { Dock = DockStyle.Fill }, 2, 1);

        var middle = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = this.LogicalToDeviceUnits(new Padding(8, 24, 8, 0))
        };
        middle.Controls.Add(Action("ToolbarAdd", Ui.Settings.ToolbarAdd, AddSelected));
        middle.Controls.Add(Action("ToolbarRemove", Ui.Settings.ToolbarRemove, RemoveSelected));
        middle.Controls.Add(Action("ToolbarAddSeparator", Ui.Settings.ToolbarAddSeparator, AddSeparator));
        middle.Controls.Add(Action("ToolbarMoveUp", Ui.Settings.ToolbarMoveUp, () => MoveSelected(-1)));
        middle.Controls.Add(Action("ToolbarMoveDown", Ui.Settings.ToolbarMoveDown, () => MoveSelected(1)));
        grid.Controls.Add(middle, 1, 1);

        // Two explicit rows rather than one wrapping row: wrapping put the label style and a
        // reset button on the same line in English and on different lines in Danish, which read
        // as an accident rather than a layout.
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = this.LogicalToDeviceUnits(new Padding(0, 10, 0, 0))
        };
        var labelRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        labelRow.Controls.Add(new Label
        {
            AutoSize = true,
            Text = Ui.Settings.ToolbarLabels,
            Padding = this.LogicalToDeviceUnits(new Padding(0, 6, 8, 0))
        });
        labelRow.Controls.Add(_labels);
        footer.Controls.Add(labelRow);

        var resetRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = this.LogicalToDeviceUnits(new Padding(0, 8, 0, 0))
        };
        resetRow.Controls.Add(Action(
            "ToolbarResetEssential", Ui.Settings.ToolbarResetEssential, () => Reset(ToolbarPreset.Essential)));
        resetRow.Controls.Add(Action(
            "ToolbarResetExpanded", Ui.Settings.ToolbarResetExpanded, () => Reset(ToolbarPreset.Expanded)));
        footer.Controls.Add(resetRow);
        grid.Controls.Add(footer, 0, 2);
        grid.SetColumnSpan(footer, 3);

        _labels.SelectedIndexChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        return grid;
    }

    /// <summary>
    /// The same small caption the settings pages put over a card, so the two lists are labelled
    /// the way every other group on a page is.
    /// </summary>
    private static SettingsCaption Caption(string text) => new(text) { Dock = DockStyle.Fill };

    /// <summary>
    /// A list stripped of its own chrome: the host around it paints the border and the padding,
    /// and the rows are drawn here so a selected one uses the palette rather than the system
    /// highlight colour.
    /// </summary>
    private void StyleList(ListBox list, string name, string accessibleName)
    {
        list.Name = name;
        list.Dock = DockStyle.Fill;
        list.IntegralHeight = false;
        list.BorderStyle = BorderStyle.None;
        // Command labels are long once they carry their menu's name, and longer again in German.
        list.HorizontalScrollbar = true;
        list.AccessibleName = accessibleName;
        list.BackColor = StorageHubTheme.Input;
        list.ForeColor = StorageHubTheme.Text;
        list.DrawMode = DrawMode.OwnerDrawFixed;
        list.ItemHeight = Font.Height + LogicalToDeviceUnits(6);
        list.DrawItem += DrawListRow;
    }

    private void DrawListRow(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ListBox list || e.Index < 0)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using (var fill = new SolidBrush(selected ? StorageHubTheme.Selection : StorageHubTheme.Input))
        {
            e.Graphics.FillRectangle(fill, e.Bounds);
        }

        TextRenderer.DrawText(
            e.Graphics,
            list.Items[e.Index].ToString(),
            Font,
            new Rectangle(
                e.Bounds.Left + LogicalToDeviceUnits(6),
                e.Bounds.Top,
                e.Bounds.Width - LogicalToDeviceUnits(8),
                e.Bounds.Height),
            StorageHubTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaint(e);
        StorageHubFieldChrome.PaintCard(e.Graphics, this);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using var backdrop = new SolidBrush(StorageHubFieldChrome.Backdrop(this));
        e.Graphics.FillRectangle(backdrop, ClientRectangle);
    }

    /// <summary>
    /// A button that sizes itself to its own text. A fixed width clipped the Danish and German
    /// labels, which run half again as long as the English ones.
    /// </summary>
    private StorageHubButton Action(string name, string text, Action handler)
    {
        var button = new StorageHubButton
        {
            Name = name,
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(LogicalToDeviceUnits(112), 0),
            Margin = this.LogicalToDeviceUnits(new Padding(0, 0, 8, 6))
        };
        button.AccessibleName = text;
        button.Click += (_, _) => handler();
        return button;
    }

    /// <summary>
    /// Everything not already on the toolbar, grouped by the menu it belongs to so a long list
    /// stays navigable. A command can appear once, which is why the list shrinks as it is used.
    /// </summary>
    private void RefreshAvailable()
    {
        var used = _layout.ToHashSet(StringComparer.Ordinal);
        var previous = _available.SelectedIndex;
        _available.BeginUpdate();
        _available.Items.Clear();
        foreach (var group in UiCommandCatalog.Definitions
            .Where(command => !used.Contains(command.Id))
            .GroupBy(command => command.Menu))
        {
            foreach (var command in group)
            {
                _available.Items.Add(new CommandChoice(
                    command.Id,
                    $"{group.Key}  ·  {StripMnemonics(command.Label)}"));
            }
        }

        _available.EndUpdate();
        if (previous >= 0 && previous < _available.Items.Count)
        {
            _available.SelectedIndex = previous;
        }
    }

    private void RefreshCurrent()
    {
        var previous = _current.SelectedIndex;
        _current.BeginUpdate();
        _current.Items.Clear();
        foreach (var entry in _layout)
        {
            _current.Items.Add(entry == ToolbarLayout.Separator
                ? Ui.Settings.ToolbarSeparator
                : StripMnemonics(UiCommandCatalog.Definitions
                    .First(command => command.Id == entry).Label));
        }

        _current.EndUpdate();
        if (previous >= 0 && previous < _current.Items.Count)
        {
            _current.SelectedIndex = previous;
        }
    }

    private void AddSelected()
    {
        if (_available.SelectedItem is not CommandChoice choice)
        {
            return;
        }

        var at = _current.SelectedIndex;
        _layout.Insert(at >= 0 ? at + 1 : _layout.Count, choice.Id);
        Commit();
    }

    private void AddSeparator()
    {
        var at = _current.SelectedIndex;
        _layout.Insert(at >= 0 ? at + 1 : _layout.Count, ToolbarLayout.Separator);
        Commit();
    }

    private void RemoveSelected()
    {
        var at = _current.SelectedIndex;
        if (at < 0)
        {
            return;
        }

        _layout.RemoveAt(at);
        Commit();
        _current.SelectedIndex = Math.Min(at, _current.Items.Count - 1);
    }

    private void MoveSelected(int delta)
    {
        var at = _current.SelectedIndex;
        var target = at + delta;
        if (at < 0 || target < 0 || target >= _layout.Count)
        {
            return;
        }

        (_layout[at], _layout[target]) = (_layout[target], _layout[at]);
        Commit();
        _current.SelectedIndex = target;
    }

    private void Reset(ToolbarPreset preset)
    {
        _layout.Clear();
        _layout.AddRange(ToolbarLayout.Preset(preset));
        Commit();
    }

    private void Commit()
    {
        RefreshAvailable();
        RefreshCurrent();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Menu mnemonics mean nothing in a list, so the ampersand is dropped.</summary>
    private static string StripMnemonics(string label) =>
        label.Replace("&", string.Empty, StringComparison.Ordinal);

    private sealed record CommandChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
