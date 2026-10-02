using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Draws a workspace's layout tree as nested grids.
/// </summary>
/// <remarks>
/// <para>
/// One to four panes in any of six arrangements, built by walking
/// <see cref="WorkspaceLayoutModel"/>: a split becomes a grid of two proportional cells with a
/// splitter between them, a leaf becomes a <see cref="BrowserPaneView"/>. Recursion is why this is
/// code rather than markup -- a three-pane arrangement is a split inside a split, and a
/// <c>DataTemplate</c> cannot nest itself to an unknown depth without becoming harder to read than
/// the fifty lines below.
/// </para>
/// <para>
/// The tree is the one Desktop.Core already owns, so what is drawn here, what a <c>.shw</c> file
/// saves and what the preset buttons produce are the same object. The alternative -- a layout the
/// view invents from a pane count -- is how a three-pane workspace ends up reopening as something
/// else.
/// </para>
/// </remarks>
public partial class WorkspaceView : UserControl
{
    /// <summary>
    /// Raised, bubbling, when <see cref="Minimum"/> has changed, so the window can give the
    /// workspace that much room and stop its own splitters short of it.
    /// </summary>
    internal static readonly RoutedEvent<RoutedEventArgs> MinimumChangedEvent =
        RoutedEvent.Register<WorkspaceView, RoutedEventArgs>("MinimumChanged", RoutingStrategies.Bubble);

    /// <summary>
    /// What changes about a pane that changes how small it can be: a bar shown or hidden, a
    /// listing turned terminal, a banner come or gone.
    /// </summary>
    private static readonly HashSet<string> ChromeProperties =
    [
        nameof(BrowserPaneModel.ShowConnectionBar), nameof(BrowserPaneModel.ShowFilesBar),
        nameof(BrowserPaneModel.IsListing), nameof(BrowserPaneModel.IsTerminal),
        nameof(BrowserPaneModel.HasStatus), nameof(BrowserPaneModel.HasBadges)
    ];

    private readonly List<BrowserPaneModel> _watched = [];
    private WorkspaceModel? _bound;
    private Func<Size>? _minimum;
    private bool _evaluating;

    public WorkspaceView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => Attach(DataContext as WorkspaceModel);
        Scroller.SizeChanged += (_, _) => FitHost(Minimum);
        AttachedToVisualTree += (_, _) => RequestMinimum();
    }

    /// <summary>
    /// The smallest the panes can be together, splitters included: each pane at least its
    /// <see cref="BrowserPaneView.MinimumSize"/>, combined up the tree by
    /// <see cref="LayoutLimits.Split"/>. Nothing while there is no workspace to show.
    /// </summary>
    internal Size Minimum { get; private set; }

    private Panel Host => this.FindControl<Panel>("PART_Host")!;

    private ScrollViewer Scroller => this.FindControl<ScrollViewer>("PART_Scroller")!;

    private void Attach(WorkspaceModel? model)
    {
        if (ReferenceEquals(_bound, model)) return;
        if (_bound is not null) _bound.LayoutChanged -= OnLayoutChanged;

        _bound = model;
        if (_bound is not null) _bound.LayoutChanged += OnLayoutChanged;
        Rebuild();
    }

    private void OnLayoutChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        Host.Children.Clear();
        _minimum = null;
        foreach (var pane in _watched) pane.PropertyChanged -= OnPaneChanged;
        _watched.Clear();
        if (_bound is not null)
        {
            foreach (var pane in _bound.Panes)
            {
                pane.PropertyChanged += OnPaneChanged;
                _watched.Add(pane);
            }

            var (root, minimum) = Build(_bound.Layout.Root);
            Host.Children.Add(root);
            _minimum = minimum;
        }

        RequestMinimum();
    }

    private void OnPaneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && ChromeProperties.Contains(name)) RequestMinimum();
    }

    /// <summary>
    /// Works the minimum out again once the layout has settled, when the pane views a rebuild made
    /// exist and have their templates. Several requests in a row are one evaluation.
    /// </summary>
    private void RequestMinimum()
    {
        if (_evaluating) return;
        _evaluating = true;
        Dispatcher.UIThread.Post(EvaluateMinimum, DispatcherPriority.Background);
    }

    /// <summary>
    /// Sets every split's limits from its two sides, and the host's, and tells the window when
    /// the whole has changed.
    /// </summary>
    private void EvaluateMinimum()
    {
        _evaluating = false;
        var minimum = _minimum is { } evaluate && this.IsAttachedToVisualTree() ? evaluate() : default;
        FitHost(minimum);
        if (minimum == Minimum) return;
        Minimum = minimum;
        RaiseEvent(new RoutedEventArgs(MinimumChangedEvent, this));
    }

    /// <summary>
    /// The panes get all the room there is, or their minimum where that is more, and the area
    /// scrolls for the difference.
    /// </summary>
    /// <remarks>
    /// Sized explicitly rather than left to the scroll viewer, which measures its content with
    /// unlimited room: a list measured that way asks for every row it has, and the panes would grow
    /// to the length of their listings.
    /// </remarks>
    private void FitHost(Size minimum)
    {
        var room = Scroller.Bounds.Size;
        if (room.Width <= 0 || room.Height <= 0) return;
        Host.Width = Math.Max(room.Width, minimum.Width);
        Host.Height = Math.Max(room.Height, minimum.Height);
    }

    private (Control View, Func<Size> Minimum) Build(WorkspaceLayoutNode node) => node switch
    {
        WorkspacePaneLeaf leaf => BuildPane(leaf),
        WorkspaceSplitNode split => BuildSplit(split),
        _ => (new Panel(), static () => default)
    };

    private (Control, Func<Size>) BuildPane(WorkspacePaneLeaf leaf)
    {
        var host = new ContentControl
        {
            Content = _bound?.PaneFor(leaf.PaneId),
            ContentTemplate = PaneTemplate.Instance
        };
        return (host, () =>
            host.GetVisualDescendants().OfType<BrowserPaneView>().FirstOrDefault()?.MinimumSize() ?? default);
    }

    /// <summary>
    /// A split: two cells sized by the node's ratio, with a draggable divider between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The splitter is <c>Auto</c> and the two panes are star-sized at the ratio and its
    /// complement, so the divider keeps its thickness while the panes absorb every resize. Giving
    /// the splitter a star share instead is what makes a divider grow on a wide window.
    /// </para>
    /// <para>
    /// A drag writes back through <see cref="WorkspaceModel.SetRatio"/> rather than being read on
    /// demand, because the grid is the thing the person is dragging and the model only needs to
    /// know where they stopped. Reading it live would fight the drag; rebuilding on it would reset
    /// every pane's scroll position mid-gesture.
    /// </para>
    /// <para>
    /// Each side's row or column has that side's minimum as its own, which is what stops the
    /// splitter: a GridSplitter goes no further than leaves both definitions their minimum, at any
    /// depth, because the minimum of a side that is itself split already includes both of its
    /// panes. A saved ratio that would leave a side too small is held at that side's minimum the
    /// same way, without the saved ratio being changed, so a bigger window gives it back.
    /// </para>
    /// </remarks>
    private (Control, Func<Size>) BuildSplit(WorkspaceSplitNode split)
    {
        var vertical = split.Orientation == WorkspaceSplitOrientation.Vertical;
        var grid = new Grid();
        var first = new GridLength(split.Ratio, GridUnitType.Star);
        var second = new GridLength(1 - split.Ratio, GridUnitType.Star);
        var divider = GridLength.Auto;

        if (vertical)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(first));
            grid.ColumnDefinitions.Add(new ColumnDefinition(divider));
            grid.ColumnDefinitions.Add(new ColumnDefinition(second));
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition(first));
            grid.RowDefinitions.Add(new RowDefinition(divider));
            grid.RowDefinitions.Add(new RowDefinition(second));
        }

        var (one, oneMinimum) = Build(split.First);
        var (two, twoMinimum) = Build(split.Second);
        var splitter = new GridSplitter();
        if (vertical)
        {
            splitter.Width = SplitterThickness;
            splitter.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            Grid.SetColumn(one, 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(two, 2);
        }
        else
        {
            splitter.Height = SplitterThickness;
            splitter.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            Grid.SetRow(one, 0);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(two, 2);
        }

        splitter.DragCompleted += (_, _) => Remember(grid, split, vertical);
        grid.Children.Add(one);
        grid.Children.Add(splitter);
        grid.Children.Add(two);

        return (grid, () =>
        {
            var first = oneMinimum();
            var second = twoMinimum();
            if (vertical)
            {
                grid.ColumnDefinitions[0].MinWidth = first.Width;
                grid.ColumnDefinitions[2].MinWidth = second.Width;
            }
            else
            {
                grid.RowDefinitions[0].MinHeight = first.Height;
                grid.RowDefinitions[2].MinHeight = second.Height;
            }

            return LayoutLimits.Split(vertical, first, second, LayoutLimits.Thickness(splitter, vertical));
        });
    }

    /// <summary>Reads the ratio back off the grid the drag just resized.</summary>
    private void Remember(Grid grid, WorkspaceSplitNode split, bool vertical)
    {
        var (before, after) = vertical
            ? (grid.ColumnDefinitions[0].ActualWidth, grid.ColumnDefinitions[2].ActualWidth)
            : (grid.RowDefinitions[0].ActualHeight, grid.RowDefinitions[2].ActualHeight);
        var total = before + after;
        if (total <= 0) return;
        _bound?.SetRatio(split, before / total);
    }

    /// <summary>
    /// The divider's thickness, matched to the one the rest of the shell uses.
    /// </summary>
    /// <remarks>
    /// Read from the resources rather than written twice, so the splitter between two panes and the
    /// splitter above the transfer queue cannot drift apart.
    /// </remarks>
    private double SplitterThickness =>
        this.TryFindResource("SplitterThickness", out var value) && value is double thickness
            ? thickness
            : 4;

    /// <summary>
    /// The one template every leaf uses.
    /// </summary>
    /// <remarks>
    /// Shared rather than built per pane: four panes would otherwise mean four identical templates,
    /// and a template is the kind of object Avalonia caches behind.
    /// </remarks>
    private sealed class PaneTemplate : IDataTemplate
    {
        internal static PaneTemplate Instance { get; } = new();

        public bool Match(object? data) => data is BrowserPaneModel;

        public Control Build(object? data) => new BrowserPaneView();
    }
}
