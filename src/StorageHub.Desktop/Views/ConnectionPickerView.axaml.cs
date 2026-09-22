using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The connection chooser's input: the keys in the search box, and a click on a connection.
/// </summary>
/// <remarks>
/// The arrow keys are caught before the search box sees them, tunnelling, because a text box
/// otherwise moves its caret on Up and Down and the highlight would never move. Escape is left to
/// the flyout, which closes on it.
/// </remarks>
public partial class ConnectionPickerView : UserControl
{
    public ConnectionPickerView()
    {
        AvaloniaXamlLoader.Load(this);

        this.FindControl<TextBox>("PART_Search")!.AddHandler(
            KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        this.FindControl<ListBox>("PART_List")!.AddHandler(
            TappedEvent, OnListTapped, RoutingStrategies.Bubble);
    }

    /// <summary>Put the caret in the search box, so typing filters at once.</summary>
    internal void FocusSearch() => this.FindControl<TextBox>("PART_Search")?.Focus();

    private ConnectionPickerModel? Model => DataContext as ConnectionPickerModel;

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model) return;
        switch (e.Key)
        {
            case Key.Down:
                model.Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                model.Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                e.Handled = model.Choose();
                break;
        }
    }

    /// <summary>A click on a connection opens it; a click on a heading does nothing.</summary>
    private void OnListTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Avalonia.Visual source ||
            source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: ConnectionPickerRowModel { IsCard: true } row } ||
            Model is not { } model)
        {
            return;
        }

        model.Highlighted = model.Rows.IndexOf(row);
        model.Choose();
        e.Handled = true;
    }
}
