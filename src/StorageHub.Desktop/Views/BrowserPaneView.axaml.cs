using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using StorageHub.Desktop.Localization;

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
        AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged, RoutingStrategies.Bubble);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);

        // Choosing a folder in the tree goes there. The tree's own model ignores the selection it
        // makes when it follows a listing, so only a person's choice moves the pane.
        if (this.FindControl<TreeView>("PART_Tree") is { } tree)
        {
            tree.SelectionChanged += (_, _) =>
            {
                if (tree.SelectedItem is PaneTreeNode node) Model?.Tree.Chosen(node);
            };
        }
        if (this.FindControl<TextBox>("PART_Address") is { } address)
        {
            // Enter goes there; Escape puts back where the pane is. Handled here so the list's own
            // Enter and Backspace, which open and go up, never see keys meant for the address.
            address.KeyDown += (_, e) =>
            {
                if (Model is not { } model) return;
                if (e.Key == Key.Enter)
                {
                    model.GoToAddressCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    model.ResetAddress();
                    Table?.Focus();
                    e.Handled = true;
                }
            };
            address.LostFocus += (_, _) =>
            {
                // Left without going anywhere: show where the pane actually is again.
                if (Model is { } model && !model.IsBusy) model.ResetAddress();
            };
        }
        PaneDragHandler.Attach(this, () => Model);
        RefreshHeadings();
        AttachConnectionPicker();
    }

    /// <summary>
    /// Reads the next page once the listing is within a screen of its end, as 1.x did.
    /// </summary>
    /// <remarks>
    /// A scroll change is raised when the content grows as well as when it is scrolled, so a first
    /// page too short to fill the pane asks for the next one on its own, and so on until either the
    /// pane is full or the folder is. Only the listing's own scroller counts; the terminal has one too.
    /// </remarks>
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (Model is not { HasMorePages: true, IsLoadingMore: false } model) return;
        if (e.Source is not ScrollViewer scroller || scroller.TemplatedParent != Table) return;

        var remaining = scroller.Extent.Height - scroller.Offset.Y - scroller.Viewport.Height;
        if (remaining <= scroller.Viewport.Height) _ = model.LoadMoreAsync();
    }

    /// <summary>
    /// Gives the connection flyout a fresh picker each time it opens, and closes it on a choice.
    /// </summary>
    /// <remarks>
    /// Fresh, so the search box starts empty and the highlight starts on the connection in use. The
    /// focus goes to the search box once the flyout is on screen -- before then there is nothing to
    /// focus -- so typing filters without a click first.
    /// </remarks>
    private void AttachConnectionPicker()
    {
        if (this.FindControl<Button>("PART_ConnectionButton")?.Flyout is not Flyout flyout ||
            flyout.Content is not ConnectionPickerView view)
        {
            return;
        }

        flyout.Opening += (_, _) =>
        {
            if (Model is not { } model) return;
            var picker = model.CreatePicker();
            picker.Chosen += (_, _) => flyout.Hide();
            view.DataContext = picker;
        };
        flyout.Opened += (_, _) => view.FocusSearch();
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
        if (_bound is not null)
        {
            _bound.PropertyChanged -= OnModelChanged;
            _bound.FocusAddressRequested -= OnFocusAddress;
        }

        _bound = model;
        if (_bound is not null)
        {
            _bound.PropertyChanged += OnModelChanged;
            _bound.FocusAddressRequested += OnFocusAddress;
        }

        RefreshHeadings();
    }

    private void OnFocusAddress(object? sender, EventArgs e)
    {
        if (this.FindControl<TextBox>("PART_Address") is not { } address) return;
        address.Focus();
        address.SelectAll();
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
        if (Model is not { } model) return;
        if (!model.IsActive) model.IsActive = true;

        // A right-click on a row that is not part of the selection makes it the selection, as
        // Explorer and 1.x did, so the menu acts on what was clicked rather than on something
        // selected elsewhere in the list.
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed &&
            e.Source is Visual source &&
            source.FindAncestorOfType<TableView>() is not null &&
            (source as StyledElement ?? source.FindAncestorOfType<StyledElement>())?.DataContext is BrowserListItem row &&
            !model.SelectedRows.Contains(row))
        {
            model.SelectedRows.Clear();
            model.SelectedRows.Add(row);
            model.Selected = row;
        }
    }

    /// <summary>
    /// The list's right-click menu: Open and Edit, which are this pane's, then the shell's own
    /// entries for everything else, with their shortcuts shown.
    /// </summary>
    /// <remarks>
    /// Built when it opens, so Open says "Go up one level" on ".." and each entry's availability is
    /// the one at that moment. The shell's entries route to the active pane, which the right-click
    /// has just made this one.
    /// </remarks>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Model is not { IsListing: true } model || Table is not { } table) return;
        if (e.Source is not Visual source || source.FindAncestorOfType<TableView>() is null && source != table) return;

        var items = new List<Control>
        {
            new MenuItem
            {
                Header = model.Selected is { IsParentNavigation: true } ? Ui.Pane.GoUpOneLevel : Ui.Pane.Open,
                Command = model.OpenCommand,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Icon = new LucideIcon { Kind = LucideIconKind.FolderOpen, Size = 16 },
            },
            new MenuItem
            {
                Header = Ui.Pane.EditInExternalEditor,
                Command = model.EditCommand,
                Icon = new LucideIcon { Kind = LucideIconKind.FilePen, Size = 16 },
            },
            new Separator(),
        };

        if (TopLevel.GetTopLevel(this)?.DataContext is ShellPreviewModel shell)
        {
            foreach (var entry in shell.PaneContextEntries)
            {
                items.Add(entry is CommandEntry command
                    ? new MenuItem
                    {
                        Header = command.Label,
                        Command = command.Command,
                        InputGesture = command.Shortcut,
                        Icon = command.Icon is { } icon ? new LucideIcon { Kind = icon, Size = 16 } : null,
                    }
                    : new Separator());
            }
        }

        table.ContextMenu = new ContextMenu { ItemsSource = items };
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
