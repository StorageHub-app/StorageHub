using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;

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
    private WorkspaceModel? _bound;

    public WorkspaceView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => Attach(DataContext as WorkspaceModel);
    }

    private Panel Host => this.FindControl<Panel>("PART_Host")!;

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
        if (_bound is null) return;
        Host.Children.Add(Build(_bound.Layout.Root));
    }

    private Control Build(WorkspaceLayoutNode node) => node switch
    {
        WorkspacePaneLeaf leaf => BuildPane(leaf),
        WorkspaceSplitNode split => BuildSplit(split),
        _ => new Panel()
    };

    private ContentControl BuildPane(WorkspacePaneLeaf leaf) => new()
    {
        Content = _bound?.PaneFor(leaf.PaneId),
        ContentTemplate = PaneTemplate.Instance
    };

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
    /// </remarks>
    private Grid BuildSplit(WorkspaceSplitNode split)
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

        var one = Build(split.First);
        var two = Build(split.Second);
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
        return grid;
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
