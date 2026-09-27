using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Lucide.Avalonia;
using StorageHub.Desktop.Themes;

namespace StorageHub.Desktop.Views;

public partial class MainWindow : Window
{
    /// <summary>
    /// The narrowest 1.x let the workspace beside the connections panel become, in device pixels
    /// (MainForm's WorkspaceMinimum). The splitter stops there, and a narrow window takes the room
    /// from the panel rather than push the workspace out of it.
    /// </summary>
    private const int WorkspaceMinimumWidth = 560;

    private ConnectionsPanelLayout? _panelLayout;
    private ToolbarOverflowPanel? _toolbarRow;
    private bool _opened;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // The Workspace menu's pinned and recent workspaces sit under bold headings, and one whose
        // file has gone is dimmed, as 1.x drew them. A style cannot see an entry's model, so each
        // entry is marked as its container is made; the style in ControlThemes does the rest.
        if (this.FindControl<Menu>("PART_Menu") is { } menu)
        {
            menu.ContainerPrepared += (_, e) =>
            {
                if (e.Container is not MenuItem root) return;
                root.ContainerPrepared -= MarkEntry;
                root.ContainerPrepared += MarkEntry;
            };
        }

        // The chevron shows while a toolbar button does not fit, and its menu is made as it opens
        // from whichever ones those are then, as 1.x's ToolStrip filled its overflow drop-down.
        var chevron = this.GetControl<Button>("PART_ToolbarOverflow");
        var toolbar = this.GetControl<ItemsControl>("PART_Toolbar");
        toolbar.AddHandler(ToolbarOverflowPanel.OverflowChangedEvent, (_, e) =>
        {
            if (e.Source is not ToolbarOverflowPanel row) return;
            _toolbarRow = row;
            chevron.IsVisible = row.Overflow.Count > 0;
        });
        if (chevron.Flyout is MenuFlyout overflow)
        {
            overflow.Opening += (_, _) => overflow.ItemsSource = ToolbarOverflowEntries();
        }

        // And the menu is drawn again as it opens, which is when 1.x read the lists: a file can go
        // missing, or another tab be chosen, without either list changing.
        AddHandler(MenuItem.SubmenuOpenedEvent, (_, e) =>
        {
            if (e.Source is MenuItem { DataContext: MenuSection { Menu: UiMenuId.Workspace } } &&
                DataContext is ShellPreviewModel model)
            {
                model.WorkspaceMenuOpening();
            }
        });

        // The panel's width is saved when the splitter is let go, as 1.x saved it when its
        // splitter had moved: once per drag, not for every pixel on the way. A focused splitter
        // also moves on the arrow keys, which 1.x's SplitterMoved saved as well.
        var splitter = this.GetControl<GridSplitter>("PART_ConnectionsSplitter");
        splitter.DragCompleted += (_, _) => SaveConnectionsPanelWidth();
        splitter.AddHandler(KeyUpEvent, (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right) SaveConnectionsPanelWidth();
        }, handledEventsToo: true);

        // And laid out again once the window is on its screen, whose scaling the saved pixels are
        // divided by, as 1.x restored its panel when the window was shown; whenever the window's
        // width changes, since the panel gives way to the workspace in a narrow one; and when the
        // window moves to a screen with another scaling.
        Opened += (_, _) =>
        {
            _opened = true;
            ArrangeConnectionsPanel();
        };
        this.GetControl<Grid>("PART_Body").SizeChanged += (_, e) =>
        {
            if (e.WidthChanged) ArrangeConnectionsPanel();
        };
        ScalingChanged += (_, _) => OnScalingChanged();
    }

    /// <summary>
    /// Keeps the window within the screen it opens in the middle of, as 1.x's did.
    /// </summary>
    /// <remarks>
    /// 1.x opened at 1500 by 920 in the middle of the screen and made that smaller where the
    /// screen's working area was (LogicalWindowSize), its minimum too. Centred at full size on a
    /// smaller screen, which a 1080p laptop at 125% already is, the title bar would sit above the
    /// top of the screen where it cannot be dragged. Fitted twice: before it is shown, on the screen
    /// CenterScreen will pick, so it opens at the size it keeps rather than jumping to it; and once
    /// it is open, because only then is its frame's size known rather than allowed for. Called by
    /// the application only: a headless test lays the window out at the size it asks for.
    /// </remarks>
    internal void FitToScreen()
    {
        if (Screens is { } screens && (screens.ScreenFromPoint(Position) ?? screens.Primary) is { } first)
        {
            var client = new Size(Width, Height);
            Fit(first, client + FrameAllowance, client, centre: false);
        }

        Opened += Refit;

        void Refit(object? sender, EventArgs e)
        {
            Opened -= Refit;
            if (Screens?.ScreenFromWindow(this) is { } screen) Fit(screen, FrameSize ?? ClientSize, ClientSize, centre: true);
        }
    }

    /// <summary>
    /// A title bar and borders, which a window has not got until it is shown: enough that the first
    /// fit leaves little or nothing for the second.
    /// </summary>
    private static readonly Thickness FrameAllowance = new(8, 32, 8, 8);

    /// <summary>
    /// Makes the window, frame and all, no bigger than the screen's working area, its minimum too.
    /// </summary>
    private void Fit(Screen screen, Size frame, Size client, bool centre)
    {
        // Some X11 window managers report no working area at all, which would make a window of
        // negative size, and setting one throws.
        var area = screen.WorkingArea;
        if (area.Width <= 0 || area.Height <= 0) return;

        var over = new Size(
            Math.Max(0, frame.Width - area.Width / screen.Scaling),
            Math.Max(0, frame.Height - area.Height / screen.Scaling));
        if (over.Width <= 0 && over.Height <= 0) return;

        var fitted = new Size(client.Width - over.Width, client.Height - over.Height);
        MinWidth = Math.Min(MinWidth, fitted.Width);
        MinHeight = Math.Min(MinHeight, fitted.Height);
        Width = fitted.Width;
        Height = fitted.Height;

        // Before it is shown, CenterScreen does this with the new size.
        if (!centre) return;
        var outside = PixelSize.FromSize(new Size(frame.Width - over.Width, frame.Height - over.Height), screen.Scaling);
        Position = area.CenterRect(new PixelRect(outside)).Position;
    }

    /// <summary>The render scaling, which 1.x's saved pixels are divided by.</summary>
    private double Scaling => RenderScaling > 0 ? RenderScaling : 1;

    private ColumnDefinition PanelColumn(ConnectionsPanelLayout layout) =>
        this.GetControl<Grid>("PART_Body").ColumnDefinitions[layout.Side == ConnectionsPanelSide.Left ? 0 : 2];

    /// <summary>
    /// The panel's width in 1.x's device pixels, read from the column the splitter sized rather
    /// than from the panel, which is not laid out again until later.
    /// </summary>
    private int ConnectionsPanelPixels(ConnectionsPanelLayout layout)
    {
        var column = PanelColumn(layout);
        var width = column.Width.IsAbsolute ? column.Width.Value : column.ActualWidth;
        return (int)Math.Round(width * Scaling);
    }

    private void SaveConnectionsPanelWidth()
    {
        if (_panelLayout is { } layout) layout.Resized(ConnectionsPanelPixels(layout));
    }

    /// <summary>
    /// The window has moved to a screen with another scaling and kept its size on it, panel and
    /// all, so the panel is now another number of pixels. Taken as its width, or the next toggle
    /// or move would lay it out from the old pixels and change its size; and laid out again, since
    /// 1.x's limits are pixels too.
    /// </summary>
    /// <remarks>
    /// Only once the window is open: the scaling a window is created with can arrive after the
    /// panel was first laid out, and that is not a move the panel should follow.
    /// </remarks>
    private void OnScalingChanged()
    {
        if (_opened && _panelLayout is { } layout) layout.Rescaled(ConnectionsPanelPixels(layout));
        ArrangeConnectionsPanel();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Tunnelling, so the shell sees a shortcut before the focused control does - which is what
        // ProcessCmdKey did. ShellCommandRouter explains why that ordering is worth restoring.
        if (DataContext is ShellPreviewModel model) model.Router.Attach(this);

        if (_panelLayout is { } previous) previous.PropertyChanged -= OnPanelLayoutChanged;
        _panelLayout = (DataContext as ShellPreviewModel)?.ConnectionsPanel;
        if (_panelLayout is { } layout) layout.PropertyChanged += OnPanelLayoutChanged;
        ArrangeConnectionsPanel();
    }

    private void OnPanelLayoutChanged(object? sender, PropertyChangedEventArgs e) => ArrangeConnectionsPanel();

    /// <summary>
    /// Puts the connections panel on its side at its width, or hides it with its splitter.
    /// </summary>
    /// <remarks>
    /// What 1.x's ApplyConnectionsPanelSide did to a SplitContainer, done to three columns: the
    /// panel and the work area trade places, the panel's column takes the width and the limits, and
    /// the other one takes what is left. Hidden, the panel's column is sized to nothing rather than
    /// removed, so showing it again is the same arrangement with the width put back.
    /// </remarks>
    private void ArrangeConnectionsPanel()
    {
        if (_panelLayout is not { } layout) return;

        var body = this.GetControl<Grid>("PART_Body");
        var panel = this.GetControl<Border>("PART_ConnectionsPanel");
        var left = layout.Side == ConnectionsPanelSide.Left;
        Grid.SetColumn(panel, left ? 0 : 2);
        Grid.SetColumn(this.GetControl<Grid>("PART_WorkArea"), left ? 2 : 0);
        panel.BorderThickness = left ? new Thickness(0, 0, 1, 0) : new Thickness(1, 0, 0, 0);
        panel.IsVisible = layout.IsVisible;
        this.GetControl<GridSplitter>("PART_ConnectionsSplitter").IsVisible = layout.IsVisible;

        // The saved width is 1.x's device pixels, and so are its limits: the panel between 220 and
        // 640, and the workspace beside it never under 560, so the splitter stops where 1.x's did
        // and every width it can be dragged to is one that saves. The token is a floor under the
        // panel at high scaling, where 220 pixels no longer fits the panel's own header.
        var scaling = Scaling;
        var minimum = Math.Max(
            DesignTokens.Get<double>("SidebarMinWidth"),
            DesktopUpdatePreferences.MinimumConnectionsPanelWidth / scaling);
        var workMinimum = layout.IsVisible ? WorkspaceMinimumWidth / scaling : 0;
        var maximum = DesktopUpdatePreferences.MaximumConnectionsPanelWidth / scaling;

        // In a window too narrow for both, the panel gives way down to its narrowest, as 1.x's
        // SetConnectionsPanelWidth clamped it, and takes its width back when the window widens.
        if (body.Bounds.Width > 0)
        {
            var splitter = DesignTokens.Get<double>("SplitterThickness");
            maximum = Math.Min(maximum, body.Bounds.Width - splitter - workMinimum);
        }

        maximum = Math.Max(minimum, maximum);

        var work = body.ColumnDefinitions[left ? 2 : 0];
        work.Width = GridLength.Star;
        work.MinWidth = workMinimum;
        work.MaxWidth = double.PositiveInfinity;

        var column = PanelColumn(layout);
        column.MinWidth = layout.IsVisible ? minimum : 0;
        column.MaxWidth = layout.IsVisible ? maximum : double.PositiveInfinity;
        column.Width = layout.IsVisible
            ? new GridLength(Math.Clamp(layout.Width / scaling, minimum, maximum))
            : GridLength.Auto;
    }

    /// <summary>
    /// The chevron's menu: the toolbar buttons that did not fit, as menu entries that run the same
    /// commands and show the same keys, with the dividers between them kept.
    /// </summary>
    /// <remarks>
    /// A divider at the head of the list is left out; it was the one that would have ended the row.
    /// An entry that shows or hides something is ticked the way the menu bar ticks it, around its
    /// icon.
    /// </remarks>
    private List<Control> ToolbarOverflowEntries()
    {
        var entries = new List<Control>();
        foreach (var hidden in _toolbarRow?.Overflow ?? [])
        {
            switch (hidden.DataContext)
            {
                case ToolbarSeparator when entries.Count > 0 && entries[^1] is not Separator:
                    entries.Add(new Separator());
                    break;
                case CommandEntry entry:
                    var icon = new Border
                    {
                        Child = entry.Icon is { } kind
                            ? new LucideIcon { Kind = kind, Size = DesignTokens.Get<double>("IconSizeSm") }
                            : null,
                    };
                    icon.Classes.Add("menu-check");
                    icon.Classes.Set("checked", entry.Check.IsChecked);
                    var item = new MenuItem
                    {
                        Header = entry.Label,
                        Command = entry.Command,
                        InputGesture = entry.Shortcut.Gesture,
                        ToggleType = entry.ToggleType,
                        IsChecked = entry.Check.IsChecked,
                        Icon = icon,
                    };
                    item.Classes.Add("framed-check");
                    ToolTip.SetTip(item, entry.ToolTip);
                    entries.Add(item);
                    break;
            }
        }

        return entries;
    }

    private static void MarkEntry(object? sender, ContainerPreparedEventArgs e)
    {
        var entry = (sender as ItemsControl)?.ItemFromContainer(e.Container) as CommandEntry;
        e.Container.Classes.Set("heading", entry?.IsHeading == true);
        e.Container.Classes.Set("missing", entry?.IsMissing == true);
    }
}
