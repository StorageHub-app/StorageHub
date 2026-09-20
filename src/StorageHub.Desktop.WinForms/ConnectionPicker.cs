using System.Drawing.Drawing2D;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// A searchable connection chooser shown under the pane's connection button.
///
/// A plain drop-down list stops scaling once a user has more than a handful of saved connections:
/// it can only be navigated by scrolling or by first-letter matching. This keeps the existing card
/// design and grouping, marks the active connection, and adds a search box that filters as you
/// type, with the keyboard driving the list from the search field.
/// </summary>
internal sealed class ConnectionPickerPopup : ToolStripDropDown
{
    private const int HeaderHeight = 28;
    private const int CardHeight = 58;

    private readonly ListBox _list;
    private readonly StorageHubTextField _search;
    private readonly Label _empty;
    private readonly IReadOnlyList<ConnectionCardModel> _cards;
    private readonly Func<ConnectionCardModel, string> _groupLabel;
    private readonly Guid? _activeConnectionId;
    private readonly string? _activeName;
    private IReadOnlyList<ConnectionPickerRow> _rows = [];

    public ConnectionPickerPopup(
        IReadOnlyList<ConnectionCardModel> cards,
        ConnectionCardModel? active,
        Func<ConnectionCardModel, string> groupLabel,
        int width)
    {
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
        _groupLabel = groupLabel ?? throw new ArgumentNullException(nameof(groupLabel));
        _activeConnectionId = active?.ConnectionId;
        _activeName = active?.Name;

        AutoClose = true;
        DropShadowEnabled = true;
        Padding = Padding.Empty;
        BackColor = StorageHubTheme.Surface;

        _search = new StorageHubTextField
        {
            Dock = DockStyle.Top,
            Glyph = UiGlyph.Search,
            ShowClearButton = true,
            PlaceholderText = Ui.Connections.PickerSearchPlaceholder,
            Margin = Padding.Empty,
            AccessibleName = Ui.Connections.PickerSearchPlaceholder
        };
        _search.TextChanged += (_, _) => Rebuild(preserveSelection: false);
        _search.KeyDown += SearchKeyDown;

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            DrawMode = DrawMode.OwnerDrawVariable,
            BorderStyle = BorderStyle.None,
            BackColor = StorageHubTheme.Surface,
            ForeColor = StorageHubTheme.Text,
            IntegralHeight = false,
            AccessibleName = Ui.Connections.PickerListAccessibleName
        };
        _list.MeasureItem += MeasureRow;
        _list.DrawItem += DrawRow;
        _list.SelectedIndexChanged += SkipHeaders;
        _list.MouseMove += HighlightUnderCursor;
        _list.Click += (_, _) => CommitSelection();
        _list.KeyDown += ListKeyDown;

        _empty = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = StorageHubTheme.TextMuted,
            BackColor = StorageHubTheme.Surface,
            Text = Ui.Connections.PickerNoMatch,
            Visible = false
        };

        var host = new Panel
        {
            BackColor = StorageHubTheme.Surface,
            Padding = this.LogicalToDeviceUnits(new Padding(6)),
            Size = new Size(
                Math.Max(LogicalToDeviceUnits(280), Math.Max(width, MeasureRequiredWidth(cards))),
                LogicalToDeviceUnits(360))
        };
        host.Controls.Add(_empty);
        host.Controls.Add(_list);
        host.Controls.Add(_search);

        Items.Add(new ToolStripControlHost(host)
        {
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            AutoSize = false,
            Size = host.Size
        });

        Rebuild(preserveSelection: false);
    }

    /// <summary>Raised when a connection is chosen. The popup closes itself first.</summary>
    public event EventHandler<ConnectionCardModel>? ConnectionChosen;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Typing should filter immediately, without first having to click the search box.
        _search.Focus();
    }

    private void Rebuild(bool preserveSelection)
    {
        var previous = preserveSelection && _list.SelectedIndex >= 0 && _list.SelectedIndex < _rows.Count
            ? _rows[_list.SelectedIndex].Card
            : null;

        _rows = ConnectionPickerFilter.BuildRows(_cards, _search.Text, _groupLabel);
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var row in _rows)
            {
                _list.Items.Add(row);
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        _empty.Visible = _rows.Count == 0;
        _list.Visible = _rows.Count > 0;
        if (_rows.Count == 0)
        {
            return;
        }

        var target = previous is not null ? IndexOfCard(previous) : IndexOfActive();
        _list.SelectedIndex = target >= 0
            ? target
            : ConnectionPickerFilter.NextSelectable(_rows, 0, 1);
    }

    private int IndexOfActive()
    {
        for (var index = 0; index < _rows.Count; index++)
        {
            if (_rows[index].Card is { } card && IsActive(card))
            {
                return index;
            }
        }

        return -1;
    }

    private int IndexOfCard(ConnectionCardModel card)
    {
        for (var index = 0; index < _rows.Count; index++)
        {
            if (ReferenceEquals(_rows[index].Card, card))
            {
                return index;
            }
        }

        return -1;
    }

    private bool IsActive(ConnectionCardModel card) => _activeConnectionId is { } id
        ? card.ConnectionId == id
        : card.ConnectionId is null && string.Equals(card.Name, _activeName, StringComparison.Ordinal);

    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down:
                MoveHighlight(1);
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.Up:
                MoveHighlight(-1);
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.Enter:
                CommitSelection();
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
            case Keys.Escape:
                Close(ToolStripDropDownCloseReason.Keyboard);
                e.Handled = true;
                break;
        }
    }

    private void ListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            CommitSelection();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void MoveHighlight(int direction)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        var start = _list.SelectedIndex < 0 ? (direction > 0 ? 0 : _rows.Count - 1) : _list.SelectedIndex + direction;
        var next = ConnectionPickerFilter.NextSelectable(_rows, start, direction);
        if (next >= 0)
        {
            _list.SelectedIndex = next;
        }
    }

    /// <summary>Keeps the highlight on a real connection when a group heading is landed on.</summary>
    private void SkipHeaders(object? sender, EventArgs e)
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _rows.Count || !_rows[_list.SelectedIndex].IsHeader)
        {
            return;
        }

        var next = ConnectionPickerFilter.NextSelectable(_rows, _list.SelectedIndex + 1, 1);
        if (next < 0)
        {
            next = ConnectionPickerFilter.NextSelectable(_rows, _list.SelectedIndex - 1, -1);
        }

        if (next >= 0)
        {
            _list.SelectedIndex = next;
        }
    }

    private void HighlightUnderCursor(object? sender, MouseEventArgs e)
    {
        var index = _list.IndexFromPoint(e.Location);
        if (index >= 0 && index < _rows.Count && !_rows[index].IsHeader && index != _list.SelectedIndex)
        {
            _list.SelectedIndex = index;
        }
    }

    private void CommitSelection()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _rows.Count ||
            _rows[_list.SelectedIndex].Card is not { } card)
        {
            return;
        }

        Close(ToolStripDropDownCloseReason.ItemClicked);
        ConnectionChosen?.Invoke(this, card);
    }

    /// <summary>
    /// The width the widest row needs to show its name in full alongside its badges. Sizing from
    /// the content rather than from the button means a long connection name is never truncated,
    /// at any font size or DPI -- the name is the one thing the list exists to show.
    /// </summary>
    private int MeasureRequiredWidth(IReadOnlyList<ConnectionCardModel> cards)
    {
        // Mirrors DrawRow's own arithmetic rather than approximating it with different fonts and a
        // smaller allowance. Understating it is what let the active badge be drawn on top of the
        // provider badge: the row ran out of width and the two were laid out over each other.
        using var badgeFont = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
        var activeBadge = TextRenderer.MeasureText(
            Ui.Connections.PickerActiveBadge, badgeFont, Size.Empty, TextFormatFlags.NoPadding).Width
            + LogicalToDeviceUnits(16);
        var widest = 0;
        foreach (var card in cards)
        {
            var badgeText = card.Type == ConnectionProfileType.Client
                ? Ui.Format(Ui.Connections.PickerClientBadgeFormat, card.Provider.ToString().ToUpperInvariant())
                : card.Provider == StorageProviderKind.Local
                    ? Ui.Connections.PickerSystemLocalBadge
                    : Ui.Format(Ui.Connections.PickerStorageBadgeFormat, card.Provider.ToString().ToUpperInvariant());
            var text = Math.Max(
                TextRenderer.MeasureText(card.Name, _list.Font, Size.Empty, TextFormatFlags.NoPadding).Width,
                TextRenderer.MeasureText(card.Endpoint ?? string.Empty, _list.Font, Size.Empty, TextFormatFlags.NoPadding).Width);

            // The provider badge with its 12px of padding, the 8px gap before the active badge,
            // and the active badge itself, so every row can show both at once.
            var badges = TextRenderer.MeasureText(badgeText, badgeFont, Size.Empty, TextFormatFlags.NoPadding).Width
                + 12 + 8 + activeBadge;

            // 33px text inset, 14px right inset, the card's 10px of margin inside the list, the
            // host panel's 6px of padding either side, and room for the scrollbar.
            widest = Math.Max(
                widest,
                text + badges + 33 + 14 + 10 + 12 + SystemInformation.VerticalScrollBarWidth);
        }

        // Capped so one pathological name cannot produce a popup wider than the window.
        return Math.Min(widest, LogicalToDeviceUnits(720));
    }

    private void MeasureRow(object? sender, MeasureItemEventArgs e)
    {
        e.ItemHeight = e.Index >= 0 && e.Index < _rows.Count && _rows[e.Index].IsHeader
            ? LogicalToDeviceUnits(HeaderHeight)
            : LogicalToDeviceUnits(CardHeight);
    }

    private void DrawRow(object? sender, DrawItemEventArgs e)
    {
        using var background = new SolidBrush(StorageHubTheme.Surface);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (e.Index < 0 || e.Index >= _rows.Count)
        {
            return;
        }

        var row = _rows[e.Index];
        if (row.IsHeader)
        {
            using var groupFont = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
            TextRenderer.DrawText(
                e.Graphics,
                (row.GroupLabel ?? string.Empty).ToUpperInvariant(),
                groupFont,
                new Rectangle(
                    e.Bounds.Left + LogicalToDeviceUnits(10),
                    e.Bounds.Top + LogicalToDeviceUnits(2),
                    e.Bounds.Width - LogicalToDeviceUnits(20),
                    LogicalToDeviceUnits(HeaderHeight - 4)),
                StorageHubTheme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        var card = row.Card!;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var highlighted = (e.State & DrawItemState.Selected) != 0;
        var accent = StorageHubTheme.ParseAccent(card.AccentHex);
        var bounds = new Rectangle(
            e.Bounds.Left + LogicalToDeviceUnits(5),
            e.Bounds.Top + LogicalToDeviceUnits(3),
            e.Bounds.Width - LogicalToDeviceUnits(10),
            e.Bounds.Height - LogicalToDeviceUnits(7));

        using (var fill = new SolidBrush(highlighted
                   ? StorageHubTheme.CurrentPalette.Selection
                   : StorageHubTheme.SurfaceMuted))
        using (var border = new Pen(highlighted ? accent : StorageHubTheme.Border, highlighted ? 1.8F : 1F))
        using (var path = RoundedRectangle(bounds, 10))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        using (var accentBrush = new SolidBrush(accent))
        {
            var dot = LogicalToDeviceUnits(9);
            e.Graphics.FillEllipse(
                accentBrush,
                bounds.Left + LogicalToDeviceUnits(14),
                bounds.Top + ((bounds.Height - dot) / 2),
                dot,
                dot);
        }

        var right = bounds.Right - LogicalToDeviceUnits(14);

        // The connection already open in this pane is marked, so the list answers "where am I?"
        // as well as "where do I want to go?".
        if (IsActive(card))
        {
            using var activeFont = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
            var activeText = Ui.Connections.PickerActiveBadge;
            var activeWidth = TextRenderer.MeasureText(
                activeText, activeFont, Size.Empty, TextFormatFlags.NoPadding).Width
                + LogicalToDeviceUnits(16);
            var pillHeight = LogicalToDeviceUnits(20);
            var activeBounds = new Rectangle(
                right - activeWidth,
                bounds.Top + ((bounds.Height - pillHeight) / 2),
                activeWidth,
                pillHeight);
            using (var activeFill = new SolidBrush(Color.FromArgb(46, StorageHubTheme.Success)))
            using (var activePath = RoundedRectangle(activeBounds, 9))
            {
                e.Graphics.FillPath(activeFill, activePath);
            }

            TextRenderer.DrawText(
                e.Graphics,
                activeText,
                activeFont,
                activeBounds,
                StorageHubTheme.Success,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            right = activeBounds.Left - LogicalToDeviceUnits(8);
        }

        var badgeText = card.Type == ConnectionProfileType.Client
            ? Ui.Format(Ui.Connections.PickerClientBadgeFormat, card.Provider.ToString().ToUpperInvariant())
            : card.Provider == StorageProviderKind.Local
                ? Ui.Connections.PickerSystemLocalBadge
                : Ui.Format(Ui.Connections.PickerStorageBadgeFormat, card.Provider.ToString().ToUpperInvariant());
        using (var badgeFont = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold, GraphicsUnit.Point))
        {
            var badgeSize = TextRenderer.MeasureText(badgeText, badgeFont, Size.Empty, TextFormatFlags.NoPadding);
            var badgeBounds = new Rectangle(
                right - badgeSize.Width - LogicalToDeviceUnits(12),
                bounds.Top + ((bounds.Height - LogicalToDeviceUnits(20)) / 2),
                badgeSize.Width + LogicalToDeviceUnits(12),
                20);
            using (var badgeFill = new SolidBrush(Color.FromArgb(highlighted ? 48 : 28, accent)))
            using (var badgePath = RoundedRectangle(badgeBounds, 9))
            {
                e.Graphics.FillPath(badgeFill, badgePath);
            }

            TextRenderer.DrawText(
                e.Graphics,
                badgeText,
                badgeFont,
                badgeBounds,
                highlighted ? StorageHubTheme.Text : accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            right = badgeBounds.Left - LogicalToDeviceUnits(8);
        }

        // The two lines are centred as a block inside the card, so the space above the name
        // matches the space below the endpoint rather than the text hugging the top edge.
        const int TextBlockHeight = 36;
        var textBlockHeight = LogicalToDeviceUnits(TextBlockHeight);
        var textLeft = bounds.Left + LogicalToDeviceUnits(33);
        var textRight = Math.Max(textLeft + LogicalToDeviceUnits(40), right);
        var textTop = bounds.Top + ((bounds.Height - textBlockHeight) / 2);
        TextRenderer.DrawText(
            e.Graphics,
            card.Name,
            _list.Font,
            Rectangle.FromLTRB(textLeft, textTop, textRight, textTop + LogicalToDeviceUnits(19)),
            card.IsEnabled ? StorageHubTheme.Text : StorageHubTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            e.Graphics,
            card.Endpoint,
            _list.Font,
            Rectangle.FromLTRB(
                textLeft, textTop + LogicalToDeviceUnits(18), textRight, textTop + textBlockHeight),
            StorageHubTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        if (diameter <= 1)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
