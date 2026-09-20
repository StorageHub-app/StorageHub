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
    public BrowserPaneView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    private BrowserPaneModel? Model => DataContext as BrowserPaneModel;

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
