using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

/// <summary>
/// One browser pane.
/// </summary>
/// <remarks>
/// The code here is the two things a listing needs that are input rather than state: opening a row
/// on a double-click or Enter, and claiming the active pane when it is clicked. Everything else is
/// in <see cref="BrowserPaneModel"/>, which is why that can be driven through a whole navigation by
/// a headless test.
/// </remarks>
public partial class BrowserPaneView : UserControl
{
    /// <summary>
    /// The five columns, in the order they are declared, and what each one sorts by.
    /// </summary>
    /// <remarks>
    /// By position rather than by heading text, because the heading is translated and carries a
    /// sort arrow. Matching on what it says would make the sort stop working in Danish.
    /// </remarks>
    private static readonly BrowserSortColumn[] ColumnOrder =
    [
        BrowserSortColumn.Name,
        BrowserSortColumn.Size,
        BrowserSortColumn.Type,
        BrowserSortColumn.Modified,
        BrowserSortColumn.Status
    ];

    private BrowserPaneModel? _bound;

    public BrowserPaneView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        // handledEventsToo, because a column heading handles its own tap for resizing and
        // reordering. Without it the sort click is swallowed by the control it is aimed at.
        AddHandler(TappedEvent, OnTapped, RoutingStrategies.Bubble, handledEventsToo: true);
        DataContextChanged += (_, _) => Bind(Model);
        PaneDragHandler.Attach(this, () => Model);
        RefreshHeadings();
    }

    private BrowserPaneModel? Model => DataContext as BrowserPaneModel;

    /// <summary>
    /// The listing, found by name rather than by walking the visual tree.
    /// </summary>
    /// <remarks>
    /// A visual-tree search finds nothing until the control has been measured, so the headings
    /// were still being written to a table that did not exist yet and five blank columns were what
    /// a pane showed before its first listing. FindControl reads the logical tree, which XAML has
    /// already built by the time the constructor returns.
    /// </remarks>
    private TableView? Table => this.FindControl<TableView>("PART_Rows");

    /// <summary>
    /// Follows the model's sort, because a column heading cannot bind to it.
    /// </summary>
    /// <remarks>
    /// A TableViewColumn is a definition, not a control: it never enters the visual tree and so
    /// has no DataContext for a binding to walk up from. The headings are therefore pushed here
    /// rather than pulled there, and they are still the model's strings -- the arrow and the
    /// translation are both decided in <see cref="BrowserPaneModel"/>, which a headless test can
    /// read without a window.
    /// </remarks>
    private void Bind(BrowserPaneModel? model)
    {
        if (ReferenceEquals(_bound, model)) return;
        if (_bound is not null) _bound.PropertyChanged -= OnModelChanged;
        _bound = model;
        if (_bound is not null) _bound.PropertyChanged += OnModelChanged;
        RefreshHeadings();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName?.EndsWith("Header", StringComparison.Ordinal) == true) RefreshHeadings();
    }

    private void RefreshHeadings()
    {
        if (Model is not { } model || Table is not { } table) return;

        string[] headings =
            [model.NameHeader, model.SizeHeader, model.TypeHeader, model.ModifiedHeader, model.StatusHeader];
        for (var index = 0; index < table.Columns.Count && index < headings.Length; index++)
        {
            table.Columns[index].Header = headings[index];
        }
    }

    /// <summary>Clicking a column heading sorts by it, or reverses it when it already is.</summary>
    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Visual source ||
            source.FindAncestorOfType<TableViewColumnHeader>(includeSelf: true)
                is not { Column: { } column } ||
            Table is not { } table)
        {
            return;
        }

        var index = table.Columns.IndexOf(column);
        if (index < 0 || index >= ColumnOrder.Length) return;

        Model?.SortBy(ColumnOrder[index]);
        e.Handled = true;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => Open(e);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Open(e);
        else if (e.Key == Key.Back && Model?.UpCommand.CanExecute(null) == true)
        {
            Model.UpCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Clicking anywhere in a pane makes it the active one.
    /// </summary>
    /// <remarks>
    /// Tunnelling, so it happens before the click is handled by whatever was under it. A pane
    /// command has to act on the pane somebody just clicked in, including when the click landed on
    /// a row that was already selected and nothing else changed.
    /// </remarks>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Model is { IsActive: false } model) model.IsActive = true;
    }

    private void Open(RoutedEventArgs e)
    {
        // Only from the listing: a double-click on the connection box or the address bar means
        // something else there, and opening a folder from it would be a surprise.
        if (e.Source is not Visual source || source.FindAncestorOfType<TableView>() is null)
        {
            return;
        }

        if (Model?.OpenCommand.CanExecute(null) == true)
        {
            Model.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }
}
