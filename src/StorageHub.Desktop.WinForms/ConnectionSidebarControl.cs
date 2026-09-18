using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>A per-row affordance on a saved connection.</summary>
internal enum ConnectionRowAction
{
    Edit,
    Delete
}

internal sealed record ConnectionRowMenuEventArgs(ConnectionCardModel Connection, Point ScreenLocation);

internal sealed class ConnectionSidebarControl : UserControl
{
    private readonly FlowLayoutPanel _content;
    /// <summary>
    /// Every live row for a connection, keyed by id. A list rather than a single row because a
    /// favourite can now appear both under Favourites and in its own folder, and both copies have
    /// to select and repaint together or the duplicate would look dead.
    /// </summary>
    private readonly Dictionary<Guid, List<ConnectionSidebarItem>> _items = [];
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The folder a connection lands in when it has no folder of its own.
    /// </summary>
    /// <remarks>
    /// Deliberately not translated. It becomes part of the group key that collapse state is
    /// tracked by, and it is compared when ordering folders, so it has to be stable. Only
    /// <see cref="DisplayFolderLabel"/> turns it into something to read.
    /// </remarks>
    private const string UnsortedFolderKey = "Unsorted";

    /// <summary>Translates the placeholder folder; every other folder is a user's own name.</summary>
    private static string DisplayFolderLabel(string label) =>
        string.Equals(label, UnsortedFolderKey, StringComparison.Ordinal)
            ? Ui.Connections.GroupUnsorted
            : label;

    // Registered once for the whole list rather than per row. SetConnections rebuilds every row on
    // every refresh, so per-row tracking would pile up registrations that only a theme change
    // prunes. The theme owns these bitmaps; rows borrow them and must not dispose them.
    private Image _editIcon = null!;
    private Image _deleteIcon = null!;

    internal ConnectionSidebarControl()
    {
        Dock = DockStyle.Fill;
        BackColor = StorageHubTheme.Surface;
        AccessibleName = Ui.Connections.SidebarAccessibleName;
        _content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(8, 4, 8, 12),
            BackColor = StorageHubTheme.Surface
        };
        _content.ClientSizeChanged += (_, _) => ResizeRows();
        Controls.Add(_content);
        _editIcon = StorageHubTheme.TrackIcon(
            this, image => ReplaceRowIcon(ref _editIcon, image), UiGlyph.Rename, 16, UiIconTone.Text, DeviceDpi / 96F);
        _deleteIcon = StorageHubTheme.TrackIcon(
            this, image => ReplaceRowIcon(ref _deleteIcon, image), UiGlyph.Delete, 16, UiIconTone.Danger, DeviceDpi / 96F);
    }

    internal event EventHandler<ConnectionCardModel>? ConnectionSelected;

    /// <summary>Raised when a folder header is right-clicked, to offer changing its icon.</summary>
    internal event EventHandler<FolderIconRequest>? FolderIconRequested;

    /// <summary>
    /// Icons chosen per folder, keyed by group key. Supplied by the owner because folders are
    /// derived from connection paths and have nowhere of their own to store anything.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal IReadOnlyDictionary<string, string> FolderIcons { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Raised on double click or Enter: the caller decides what "open" means.</summary>
    internal event EventHandler<ConnectionCardModel>? ConnectionActivated;

    internal event EventHandler<ConnectionCardModel>? ConnectionEditRequested;

    internal event EventHandler<ConnectionCardModel>? ConnectionDeleteRequested;

    /// <summary>Raised with the screen location to pop a row's context menu at.</summary>
    internal event EventHandler<ConnectionRowMenuEventArgs>? ConnectionMenuRequested;

    /// <summary>
    /// Whether rows carry inline edit and delete affordances. Off by default so the control keeps
    /// behaving as a plain picker wherever it is used only to choose a connection.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool ShowRowActions { get; set; }

    internal Guid? SelectedConnectionId { get; private set; }

    /// <summary>
    /// Whether a favourite also appears in its own folder. With many folders and connections the
    /// repetition can be more noise than help, so it can be turned off in Settings.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool ShowFavoritesInTheirFolders { get; set; } = true;

    internal void SetConnections(
        IEnumerable<ConnectionCardModel> connections,
        string? searchText,
        Guid? selectedConnectionId)
    {
        ArgumentNullException.ThrowIfNull(connections);
        var query = searchText?.Trim() ?? string.Empty;
        var matching = connections
            .Where(card => ConnectionPickerFilter.Matches(card, query))
            .OrderBy(static card => card.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _content.SuspendLayout();
        try
        {
            foreach (var control in _content.Controls.Cast<Control>().ToArray())
            {
                control.Dispose();
            }

            _content.Controls.Clear();
            _items.Clear();

            // A favourite is listed under Favourites and, unless that is turned off, again in the
            // folder it actually lives in: losing it from its folder is confusing when the folder
            // is how you think about it. Both copies share an id and select together.
            AddFlatSection(
                Ui.Connections.GroupFavorites,
                "favorites",
                matching.Where(static card => card.IsEnabled && card.IsFavorite),
                UiGlyph.Favorite);
            AddSection(
                Ui.Connections.GroupStorage,
                matching.Where(card =>
                    card.IsEnabled && (ShowFavoritesInTheirFolders || !card.IsFavorite)
                    && card.Type == ConnectionProfileType.Storage),
                UiGlyph.Cloud);
            AddSection(
                Ui.Connections.GroupRemoteClients,
                matching.Where(card =>
                    card.IsEnabled && (ShowFavoritesInTheirFolders || !card.IsFavorite)
                    && card.Type == ConnectionProfileType.Client),
                UiGlyph.Terminal);
            AddFlatSection(
                Ui.Connections.StateDisabled,
                "disabled",
                matching.Where(static card => !card.IsEnabled),
                UiGlyph.Hidden);
            if (_content.Controls.Count == 0)
            {
                _content.Controls.Add(new Label
                {
                    AutoSize = false,
                    Height = 64,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = StorageHubTheme.TextMuted,
                    Text = connections.Any() ? Ui.Connections.SidebarNoMatches : Ui.Connections.SidebarEmpty
                });
            }

            SelectConnection(selectedConnectionId, raiseEvent: false);
            ResizeRows();
        }
        finally
        {
            _content.ResumeLayout(true);
        }
    }

    internal void ClearSelection() => SelectConnection(null, raiseEvent: false);

    /// <summary>
    /// A section whose membership is a state rather than a place, so it is listed flat: nesting
    /// favourites under their folders would bury the shortcut the favourite exists to provide.
    /// </summary>
    private void AddFlatSection(string title, string key, IEnumerable<ConnectionCardModel> connections, UiGlyph glyph)
    {
        var cards = connections.ToArray();
        if (cards.Length == 0)
        {
            return;
        }

        _content.Controls.Add(new ConnectionSidebarSectionHeader(title, glyph));
        var folder = new FolderBuilder(key, title);
        folder.Connections.AddRange(cards);
        _content.Controls.Add(CreateGroup(folder, depth: 0));
    }

    private void AddSection(string title, IEnumerable<ConnectionCardModel> connections, UiGlyph glyph)
    {
        var cards = connections.ToArray();
        if (cards.Length == 0)
        {
            return;
        }

        _content.Controls.Add(new ConnectionSidebarSectionHeader(title, glyph));
        var root = new FolderBuilder(string.Empty, string.Empty);
        foreach (var card in cards)
        {
            var segments = SplitFolder(card.FolderPath);
            if (segments.Length == 0)
            {
                segments = [UnsortedFolderKey];
            }

            var folder = root;
            var key = card.Type == ConnectionProfileType.Client ? "clients" : "storage";
            foreach (var segment in segments)
            {
                key = $"{key}/{segment}";
                if (!folder.Children.TryGetValue(segment, out var child))
                {
                    child = new FolderBuilder(key, segment);
                    folder.Children.Add(segment, child);
                }

                folder = child;
            }

            folder.Connections.Add(card);
        }

        foreach (var folder in root.Children.Values.OrderBy(static folder =>
                     string.Equals(folder.Label, UnsortedFolderKey, StringComparison.OrdinalIgnoreCase) ? string.Empty : folder.Label,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            _content.Controls.Add(CreateGroup(folder, depth: 0));
        }
    }

    private ConnectionSidebarGroup CreateGroup(FolderBuilder folder, int depth)
    {
        var group = new ConnectionSidebarGroup(
            folder.Key,
            DisplayFolderLabel(folder.Label),
            folder.TotalConnections,
            depth,
            expanded: !_collapsedGroups.Contains(folder.Key),
            FolderIcons.GetValueOrDefault(folder.Key));
        group.IconMenuRequested += (_, location) =>
            FolderIconRequested?.Invoke(this, new FolderIconRequest(folder.Key, DisplayFolderLabel(folder.Label), location));
        group.ExpandedChanged += (_, expanded) =>
        {
            if (expanded)
            {
                _collapsedGroups.Remove(folder.Key);
            }
            else
            {
                _collapsedGroups.Add(folder.Key);
            }
        };
        foreach (var child in folder.Children.Values.OrderBy(static value => value.Label, StringComparer.CurrentCultureIgnoreCase))
        {
            group.AddChild(CreateGroup(child, depth + 1));
        }

        foreach (var card in folder.Connections.OrderBy(static value => value.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new ConnectionSidebarItem(card)
            {
                ShowActions = ShowRowActions,
                EditIcon = _editIcon,
                DeleteIcon = _deleteIcon
            };
            item.Click += (_, _) => SelectConnection(card.ConnectionId, raiseEvent: true);
            item.Activated += (_, _) =>
            {
                SelectConnection(card.ConnectionId, raiseEvent: true);
                ConnectionActivated?.Invoke(this, card);
            };
            item.ActionInvoked += (_, action) =>
            {
                SelectConnection(card.ConnectionId, raiseEvent: true);
                if (action == ConnectionRowAction.Edit)
                {
                    ConnectionEditRequested?.Invoke(this, card);
                }
                else
                {
                    ConnectionDeleteRequested?.Invoke(this, card);
                }
            };
            item.MenuRequested += (_, location) =>
            {
                SelectConnection(card.ConnectionId, raiseEvent: true);
                ConnectionMenuRequested?.Invoke(this, new ConnectionRowMenuEventArgs(card, location));
            };
            group.AddChild(item);
            if (card.ConnectionId is { } id)
            {
                if (!_items.TryGetValue(id, out var rows))
                {
                    rows = [];
                    _items[id] = rows;
                }

                rows.Add(item);
            }
        }

        return group;
    }

    private void SelectConnection(Guid? connectionId, bool raiseEvent)
    {
        if (SelectedConnectionId is { } previous && _items.TryGetValue(previous, out var previousRows))
        {
            foreach (var previousItem in previousRows)
            {
                previousItem.Selected = false;
            }
        }

        SelectedConnectionId = connectionId;
        if (connectionId is not { } selected || !_items.TryGetValue(selected, out var rows) || rows.Count == 0)
        {
            SelectedConnectionId = null;
            return;
        }

        // Both copies of a favourite highlight together, so the row in the folder does not look
        // unselected while its twin under Favourites is lit.
        foreach (var item in rows)
        {
            item.Selected = true;
        }

        if (raiseEvent)
        {
            ConnectionSelected?.Invoke(this, rows[0].Connection);
        }
    }

    private bool _resizingRows;

    private void ResizeRows()
    {
        // Widening a row can change its height, which can add or remove the scrollbar, which
        // changes the client size that brought us here. One pass, with layout held until every
        // row has its width, instead of a layout per row and a re-entry per scrollbar flip.
        if (_resizingRows)
        {
            return;
        }

        _resizingRows = true;
        try
        {
            // Bounded: a pass whose layout flipped the scrollbar is measured again, and a layout
            // that flips it back is accepted rather than chased.
            for (var pass = 0; pass < 3; pass++)
            {
                var measured = _content.ClientSize.Width;
                var width = Math.Max(120, measured - _content.Padding.Horizontal -
                    (_content.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
                _content.SuspendLayout();
                try
                {
                    foreach (Control control in _content.Controls)
                    {
                        if (control.Width != width)
                        {
                            control.Width = width;
                        }
                    }
                }
                finally
                {
                    _content.ResumeLayout(true);
                }

                if (_content.ClientSize.Width == measured)
                {
                    break;
                }
            }
        }
        finally
        {
            _resizingRows = false;
        }
    }

    /// <summary>
    /// Re-points every live row at the repainted icon after an appearance change. The rows hold a
    /// borrowed reference, so they have to be told rather than left pointing at the stale bitmap.
    /// </summary>
    private void ReplaceRowIcon(ref Image field, Image image)
    {
        field = image;
        foreach (var item in _items.Values.SelectMany(static rows => rows))
        {
            item.EditIcon = _editIcon;
            item.DeleteIcon = _deleteIcon;
            item.Invalidate();
        }
    }

    private static string[] SplitFolder(string? folder) => string.IsNullOrWhiteSpace(folder)
        ? []
        : folder.Split(['/', '\\'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private sealed class FolderBuilder(string key, string label)
    {
        internal string Key { get; } = key;
        internal string Label { get; } = label;
        internal Dictionary<string, FolderBuilder> Children { get; } = new(StringComparer.CurrentCultureIgnoreCase);
        internal List<ConnectionCardModel> Connections { get; } = [];
        internal int TotalConnections => Connections.Count + Children.Values.Sum(static child => child.TotalConnections);
    }
}

internal sealed class ConnectionSidebarSectionHeader : Control
{
    private const int IconSize = 16;
    private Image? _icon;

    internal ConnectionSidebarSectionHeader(string title, UiGlyph? glyph = null)
    {
        Text = title;
        Height = 38;
        Margin = new Padding(0, 8, 0, 2);
        Font = StorageHubTheme.CreateSectionFont();
        ForeColor = StorageHubTheme.Text;
        BackColor = StorageHubTheme.Surface;
        TabStop = false;

        // Fixed per section rather than chosen: these four are states, not places, so the icon is
        // part of what the section means. Tracked so it follows a theme change like every other.
        if (glyph is { } value)
        {
            _icon = StorageHubTheme.TrackIcon(
                this,
                image =>
                {
                    _icon = image;
                    Invalidate();
                },
                value,
                IconSize,
                UiIconTone.Muted,
                DeviceDpi / 96F);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var left = 5;
        if (_icon is not null)
        {
            e.Graphics.DrawImage(_icon, left, (Height - IconSize) / 2, IconSize, IconSize);
            left += IconSize + 7;
        }

        var textSize = TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        var y = Height / 2;
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(left, 0, Width - left - 5, Height), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var line = new Pen(StorageHubTheme.Border);
        e.Graphics.DrawLine(line, Math.Min(Width - 8, left + textSize.Width + 13), y, Width - 8, y);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Font.Dispose();
            _icon?.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class ConnectionSidebarGroup : Panel
{
    private readonly FlowLayoutPanel _body;
    private readonly StorageHubButton _header;
    private readonly string _label;
    private readonly int _count;
    private bool _expanded;
    private bool _arranging;

    /// <summary>The icon chosen for this folder, or null while it has none.</summary>
    internal string? IconKey { get; private init; }

    /// <summary>Raised on a right click, so the owner can offer to change the folder's icon.</summary>
    internal event EventHandler<Point>? IconMenuRequested;

    internal ConnectionSidebarGroup(string key, string label, int count, int depth, bool expanded, string? iconKey = null)
    {
        Name = key;
        IconKey = iconKey;
        _label = label;
        _count = count;
        _expanded = expanded;
        AutoSize = false;
        Padding = new Padding(8, 7, 8, 9);
        Margin = new Padding(depth * 8, 2, 0, 10);
        BackColor = StorageHubTheme.SurfaceMuted;
        DoubleBuffered = true;
        _header = new StorageHubButton
        {
            Height = 30,
            Dock = DockStyle.Top,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = StorageHubTheme.CreateSectionFont(),
            ForeColor = StorageHubTheme.Text,
            BackColor = StorageHubTheme.SurfaceMuted,
            TabStop = true,
            AccessibleName = Ui.Format(Ui.Connections.GroupAccessibleFormat, label)
        };
        _header.FlatAppearance.BorderSize = 0;
        _header.Click += (_, _) => Expanded = !Expanded;
        _header.MouseUp += (_, args) =>
        {
            if (args.Button == MouseButtons.Right)
            {
                IconMenuRequested?.Invoke(this, _header.PointToScreen(args.Location));
            }
        };

        // Only an icon the user actually chose: a folder with none stays as plain text rather than
        // every folder acquiring a generic folder glyph that carries no information.
        if (ConnectionIconCatalog.Resolve(iconKey) is { } glyph)
        {
            _header.Image = StorageHubTheme.TrackIcon(
                _header,
                glyph,
                16,
                UiIconTone.Text,
                DeviceDpi / 96F);
            _header.ImageAlign = ContentAlignment.MiddleLeft;
            _header.TextImageRelation = TextImageRelation.ImageBeforeText;
            _header.Padding = new Padding(4, 0, 0, 0);
        }
        _body = new FlowLayoutPanel
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 0),
            Margin = Padding.Empty,
            BackColor = StorageHubTheme.SurfaceMuted,
            Visible = expanded
        };
        Controls.Add(_body);
        Controls.Add(_header);
        UpdateHeader();
        ArrangeChildren();
    }

    internal event EventHandler<bool>? ExpandedChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Expanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
            {
                return;
            }

            _expanded = value;
            _body.Visible = value;
            UpdateHeader();
            ArrangeChildren();
            ExpandedChanged?.Invoke(this, value);
        }
    }

    internal void AddChild(Control child)
    {
        _body.Controls.Add(child);
        child.SizeChanged += ChildSizeChanged;
        ArrangeChildren();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ArrangeChildren();
    }

    private void ChildSizeChanged(object? sender, EventArgs e) => ArrangeChildren();

    private void ArrangeChildren()
    {
        if (_arranging || IsDisposed)
        {
            return;
        }

        _arranging = true;
        try
        {
            var innerWidth = Math.Max(80, ClientSize.Width - Padding.Horizontal);
            _header.Width = innerWidth;
            _body.Width = innerWidth;
            foreach (Control child in _body.Controls)
            {
                child.Width = Math.Max(72, innerWidth - child.Margin.Horizontal);
            }

            var bodyHeight = _body.Padding.Vertical + _body.Controls
                .Cast<Control>()
                .Where(static child => child.Visible)
                .Sum(static child => child.Height + child.Margin.Vertical);
            _body.Height = bodyHeight;
            Height = Padding.Top + _header.Height + (_expanded ? bodyHeight : 0) + Padding.Bottom;
            _body.Visible = _expanded;
            PerformLayout();

            using var path = RoundedPath(
                new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)),
                12);
            var previousRegion = Region;
            Region = new Region(path);
            previousRegion?.Dispose();
        }
        finally
        {
            _arranging = false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 12);
        using var border = new Pen(StorageHubTheme.Border);
        e.Graphics.DrawPath(border, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _header.Font.Dispose();
        }

        base.Dispose(disposing);
    }

    private void UpdateHeader() => _header.Text = $"{(_expanded ? "▾" : "▸")}  {_label}  ·  {_count}";

    private static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ConnectionSidebarItem : Control
{
    private const int ActionSize = 22;
    private const int ActionGap = 4;
    private const int ActionInset = 8;
    private const int BadgeIconSize = 22;
    private const int BadgeSize = 40;
    private const int BadgeLeft = 11;
    private const int TextLeft = BadgeLeft + BadgeSize + 12;

    private bool _selected;
    private bool _hovered;
    private ConnectionRowAction? _hotAction;

    internal ConnectionSidebarItem(ConnectionCardModel connection)
    {
        Connection = connection;
        Height = 76;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleName = connection.Name;
        AccessibleDescription =
            Ui.Format(
                Ui.Connections.ConnectionAccessibleFormat,
                connection.Descriptor.DisplayName,
                connection.State) +
            Ui.Connections.SidebarKeyboardHint;
        AccessibleRole = AccessibleRole.ListItem;
        DoubleBuffered = true;
        // Off by default on a raw Control, so without this the row never sees a double click.
        SetStyle(ControlStyles.StandardDoubleClick, true);
    }

    /// <summary>Raised when the row is opened: double click, or Enter.</summary>
    internal event EventHandler? Activated;

    internal event EventHandler<ConnectionRowAction>? ActionInvoked;

    internal event EventHandler<Point>? MenuRequested;

    internal ConnectionCardModel Connection { get; }

    /// <summary>Whether to draw the inline edit and delete affordances.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool ShowActions { get; init; }

    /// <summary>Borrowed from the list, which keeps them in step with the palette.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Image? EditIcon { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Image? DeleteIcon { get; set; }

    /// <summary>Internal so the hit targets can be driven directly from tests.</summary>
    internal Rectangle EditBounds => new(
        Width - (ActionSize * 2) - ActionGap - ActionInset, (Height - ActionSize) / 2, ActionSize, ActionSize);

    internal Rectangle DeleteBounds => new(
        Width - ActionSize - ActionInset, (Height - ActionSize) / 2, ActionSize, ActionSize);

    /// <summary>
    /// Reserved whether or not the icons are currently painted, so the name does not reflow under
    /// the pointer as the row is hovered.
    /// </summary>
    private int ActionStripWidth => ShowActions ? (ActionSize * 2) + ActionGap + ActionInset + 4 : 0;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Selected
    {
        get => _selected;
        set
        {
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
        _hotAction = null;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = HitAction(e.Location);
        if (hot != _hotAction)
        {
            _hotAction = hot;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right)
        {
            Focus();
            MenuRequested?.Invoke(this, PointToScreen(e.Location));
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        Focus();

        // The second click of a double click lands here too; letting it re-fire an action would
        // turn an over-eager open into a delete prompt.
        if (e.Clicks == 1 && HitAction(e.Location) is { } action)
        {
            ActionInvoked?.Invoke(this, action);
            return;
        }

        OnClick(EventArgs.Empty);
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        Activated?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Enter:
                e.Handled = true;
                OnClick(EventArgs.Empty);
                Activated?.Invoke(this, EventArgs.Empty);
                break;
            case Keys.Space:
                e.Handled = true;
                OnClick(EventArgs.Empty);
                break;
            case Keys.F2 when ShowActions:
                e.Handled = true;
                ActionInvoked?.Invoke(this, ConnectionRowAction.Edit);
                break;
            case Keys.Delete when ShowActions:
                e.Handled = true;
                ActionInvoked?.Invoke(this, ConnectionRowAction.Delete);
                break;
            case Keys.Apps:
            case Keys.F10 when e.Shift:
                e.Handled = true;
                MenuRequested?.Invoke(this, PointToScreen(new Point(Width / 2, Height / 2)));
                break;
        }
    }

    /// <summary>
    /// Draws as many tag pills as fit, and a "+n" when they do not. Truncating with a count keeps
    /// the row's height fixed however many labels a connection carries.
    /// </summary>
    private static void DrawTags(Graphics graphics, IReadOnlyList<string> tags, Rectangle bounds, Color accent)
    {
        using var tagFont = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point);
        var left = bounds.Left;
        var shown = 0;
        foreach (var tag in tags)
        {
            var size = TextRenderer.MeasureText(tag, tagFont, Size.Empty, TextFormatFlags.NoPadding);
            var width = size.Width + 14;
            var remaining = tags.Count - shown;

            // Leave room for the overflow marker unless this is the last tag anyway.
            var reserve = remaining > 1 ? 34 : 0;
            if (left + width + reserve > bounds.Right)
            {
                break;
            }

            var pill = new Rectangle(left, bounds.Top, width, bounds.Height);
            using (var shape = UiShapes.RoundedRectangle(pill, bounds.Height / 2F))
            using (var fill = new SolidBrush(Color.FromArgb(34, accent)))
            {
                graphics.FillPath(fill, shape);
            }

            TextRenderer.DrawText(graphics, tag, tagFont, pill, StorageHubTheme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            left += width + 5;
            shown++;
        }

        if (shown < tags.Count)
        {
            var more = $"+{tags.Count - shown}";
            TextRenderer.DrawText(
                graphics,
                more,
                tagFont,
                new Rectangle(left, bounds.Top, Math.Max(1, bounds.Right - left), bounds.Height),
                StorageHubTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private ConnectionRowAction? HitAction(Point location)
    {
        if (!ShowActions)
        {
            return null;
        }

        if (EditBounds.Contains(location)) return ConnectionRowAction.Edit;
        return DeleteBounds.Contains(location) ? ConnectionRowAction.Delete : null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        using var path = CreatePath(bounds, 9);
        using var fill = new SolidBrush(_selected ? StorageHubTheme.CurrentPalette.Selection : StorageHubTheme.Surface);
        using var outline = new Pen(_selected ? StorageHubTheme.ParseAccent(Connection.AccentHex) : StorageHubTheme.Border,
            _selected ? 1.8F : 1F);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(outline, path);
        var accent = StorageHubTheme.ParseAccent(Connection.AccentHex);
        var badgeTop = (Height - BadgeSize) / 2;
        using var badge = new SolidBrush(accent);
        using (var badgePath = CreatePath(new Rectangle(BadgeLeft, badgeTop, BadgeSize, BadgeSize), 10))
        {
            e.Graphics.FillPath(badge, badgePath);
        }

        // An icon rather than the provider's short name, which arrived truncated to "SF..." and
        // "LO..." at this size and told the reader nothing the row's own text did not.
        var glyph = ConnectionIconCatalog.ResolveForConnection(
            Connection.IconKey,
            Connection.Provider,
            Connection.Type);
        using (var icon = UiIconFactory.Create(
                   glyph,
                   StorageHubTheme.ContrastText(accent),
                   BadgeIconSize,
                   DeviceDpi / 96F))
        {
            e.Graphics.DrawImage(
                icon,
                BadgeLeft + ((BadgeSize - BadgeIconSize) / 2),
                badgeTop + ((BadgeSize - BadgeIconSize) / 2),
                BadgeIconSize,
                BadgeIconSize);
        }
        var textWidth = Math.Max(20, Width - TextLeft - 10 - ActionStripWidth);
        var tags = Connection.DisplayTags;

        // The name and endpoint sit as a block, with the tag row below when there is one, so a
        // connection without tags does not leave a gap where they would have been.
        var blockHeight = tags.Count > 0 ? 62 : 42;
        var top = Math.Max(6, (Height - blockHeight) / 2);
        TextRenderer.DrawText(e.Graphics, Connection.Name, Font, new Rectangle(TextLeft, top, textWidth, 21),
            StorageHubTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        var detail = Connection.IsEnabled
            ? Ui.Format(Ui.Connections.EndpointStateFormat, Connection.Endpoint, Connection.State)
            : Ui.Format(Ui.Connections.EndpointStateFormat, Connection.Endpoint, Ui.Connections.StateDisabled);
        TextRenderer.DrawText(e.Graphics, detail, Font, new Rectangle(TextLeft, top + 21, textWidth, 20),
            Connection.IsEnabled ? StorageHubTheme.TextMuted : StorageHubTheme.Warning,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (tags.Count > 0)
        {
            DrawTags(e.Graphics, tags, new Rectangle(TextLeft, top + 44, textWidth, 18), accent);
        }

        // Revealed on hover, selection or focus: drawing them on every row at rest turns a long
        // list into a wall of icons, but a keyboard user never hovers.
        if (ShowActions && (_hovered || _selected || Focused))
        {
            DrawAction(e.Graphics, EditIcon, EditBounds, _hotAction == ConnectionRowAction.Edit, danger: false);
            DrawAction(e.Graphics, DeleteIcon, DeleteBounds, _hotAction == ConnectionRowAction.Delete, danger: true);
        }

        if (Focused)
        {
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4));
        }
    }

    private static void DrawAction(Graphics graphics, Image? icon, Rectangle bounds, bool hot, bool danger)
    {
        if (hot)
        {
            using var path = CreatePath(bounds, 6);
            using var fill = new SolidBrush(danger
                ? StorageHubTheme.CurrentPalette.DangerTint
                : StorageHubTheme.CurrentPalette.Elevated);
            graphics.FillPath(fill, path);
        }

        if (icon is null)
        {
            return;
        }

        graphics.DrawImage(
            icon,
            new Rectangle(
                bounds.X + ((bounds.Width - 16) / 2),
                bounds.Y + ((bounds.Height - 16) / 2),
                16,
                16));
    }

    /// <summary>
    /// The row is one owner-drawn control, so the inline icons have no windows of their own and are
    /// invisible to assistive technology. Publishing them as accessible children is what makes them
    /// reachable; the key bindings in <see cref="OnKeyDown"/> are what makes them operable.
    /// </summary>
    protected override AccessibleObject CreateAccessibilityInstance() => new RowAccessibleObject(this);

    private sealed class RowAccessibleObject(ConnectionSidebarItem owner)
        : Control.ControlAccessibleObject(owner)
    {
        public override int GetChildCount() => owner.ShowActions ? 2 : 0;

        public override AccessibleObject? GetChild(int index) => (owner.ShowActions, index) switch
        {
            (true, 0) => new RowActionAccessibleObject(owner, ConnectionRowAction.Edit),
            (true, 1) => new RowActionAccessibleObject(owner, ConnectionRowAction.Delete),
            _ => null
        };
    }

    private sealed class RowActionAccessibleObject(ConnectionSidebarItem owner, ConnectionRowAction action)
        : AccessibleObject
    {
        public override string Name => action == ConnectionRowAction.Edit
            ? Ui.Format(Ui.Connections.EditConnectionAccessibleFormat, owner.Connection.Name)
            : Ui.Format(Ui.Connections.DeleteConnectionAccessibleFormat, owner.Connection.Name);

        public override AccessibleRole Role => AccessibleRole.PushButton;

        public override AccessibleObject Parent => owner.AccessibilityObject;

        public override Rectangle Bounds => owner.RectangleToScreen(
            action == ConnectionRowAction.Edit ? owner.EditBounds : owner.DeleteBounds);

        public override string DefaultAction => action == ConnectionRowAction.Edit ? Ui.Connections.RowEdit : Ui.Connections.RowDelete;

        public override void DoDefaultAction() => owner.ActionInvoked?.Invoke(owner, action);
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreatePath(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
