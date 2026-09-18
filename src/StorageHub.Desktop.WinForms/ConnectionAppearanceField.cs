using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>
/// The icon and colour a connection is shown with, as one editor row.
///
/// It lives in the connection editor rather than behind a right-click on the list, because
/// everything else that describes a connection -- its name, its folder, its labels -- is edited
/// here, and a person looking for "how do I change how this looks" opens the editor.
/// </summary>
internal sealed class ConnectionAppearanceField : Panel
{
    private static readonly string[] Swatches =
    [
        "#2563EB", "#7C3AED", "#DB2777", "#DC2626", "#EA580C", "#CA8A04",
        "#16A34A", "#0891B2", "#0EA5E9", "#64748B", "#475569", "#0F172A"
    ];

    private readonly Button _iconButton;
    private readonly FlowLayoutPanel _swatches = new();
    private string? _iconKey;
    private string _accentColor = "#2563EB";

    internal ConnectionAppearanceField()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.Transparent;

        _iconButton = new StorageHubButton
        {
            Width = 92,
            Height = 34,
            Text = Ui.Connections.ChooseIcon,
            TextAlign = ContentAlignment.MiddleRight,
            ImageAlign = ContentAlignment.MiddleLeft,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Margin = new Padding(0, 0, 10, 0)
        };
        _iconButton.Click += (_, _) => ChooseIcon();

        _swatches.FlowDirection = FlowDirection.LeftToRight;
        _swatches.WrapContents = true;
        _swatches.AutoSize = true;
        _swatches.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _swatches.MaximumSize = new Size(300, 0);
        _swatches.Margin = Padding.Empty;
        foreach (var swatch in Swatches)
        {
            _swatches.Controls.Add(CreateSwatch(swatch));
        }

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        row.Controls.Add(_iconButton);
        row.Controls.Add(_swatches);
        Controls.Add(row);
        RefreshIcon();
    }

    /// <summary>Raised whenever the icon or the colour changes, so the editor can mark itself dirty.</summary>
    internal event EventHandler? AppearanceChanged;

    /// <summary>The chosen icon key, empty while the provider's own is being used.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string IconKey
    {
        get => _iconKey ?? string.Empty;
        set
        {
            _iconKey = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            RefreshIcon();
        }
    }

    /// <summary>The chosen accent as <c>#RRGGBB</c>; the profile rejects any other shape.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string AccentColor
    {
        get => _accentColor;
        set
        {
            _accentColor = StorageHubTheme.ParseAccent(value) is var parsed && !string.IsNullOrWhiteSpace(value)
                ? $"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}"
                : _accentColor;
            RefreshIcon();
            foreach (var control in _swatches.Controls.OfType<Control>())
            {
                control.Invalidate();
            }
        }
    }

    private Color Accent => StorageHubTheme.ParseAccent(_accentColor);

    private void ChooseIcon()
    {
        using var picker = new IconPickerForm(
            _iconKey,
            Accent,
            Ui.Connections.IconPickerUse);
        if (picker.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        _iconKey = picker.SelectedKey;
        RefreshIcon();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshIcon()
    {
        var glyph = ConnectionIconCatalog.Resolve(_iconKey) ?? UiGlyph.Cloud;
        var previous = _iconButton.Image;
        _iconButton.Image = UiIconFactory.Create(glyph, Accent, 18, DeviceDpi / 96F);
        previous?.Dispose();
    }

    private Panel CreateSwatch(string hex)
    {
        var swatch = new Panel
        {
            Width = 22,
            Height = 22,
            Margin = new Padding(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            BackColor = Color.Transparent,
            AccessibleRole = AccessibleRole.RadioButton,
            AccessibleName = hex
        };
        swatch.Paint += (sender, args) =>
        {
            if (sender is not Control painted)
            {
                return;
            }

            args.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var bounds = new RectangleF(1.5F, 1.5F, painted.Width - 3F, painted.Height - 3F);
            using var shape = UiShapes.RoundedRectangle(bounds, bounds.Height / 2F);
            using var fill = new SolidBrush(StorageHubTheme.ParseAccent(hex));
            args.Graphics.FillPath(fill, shape);

            // The chosen colour gets a ring rather than a tick: a tick in a contrasting colour is
            // unreadable on half of these swatches.
            if (string.Equals(hex, _accentColor, StringComparison.OrdinalIgnoreCase))
            {
                using var ring = new Pen(StorageHubTheme.Text, 2F);
                args.Graphics.DrawEllipse(ring, 0.5F, 0.5F, painted.Width - 1.5F, painted.Height - 1.5F);
            }
        };
        swatch.Click += (_, _) =>
        {
            AccentColor = hex;
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        };
        return swatch;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconButton.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
