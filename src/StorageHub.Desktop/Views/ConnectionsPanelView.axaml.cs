using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The connections panel.
/// </summary>
/// <remarks>
/// It owns its own drag and drop, because dragging a connection between groups is the panel's
/// whole way of being organised and nothing above it has anything to do with it.
/// </remarks>
public partial class ConnectionsPanelView : UserControl
{
    public ConnectionsPanelView()
    {
        AvaloniaXamlLoader.Load(this);
        ConnectionDragHandler.Attach(this, () => DataContext as ConnectionsSidebar);
        AddHandler(DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble);
        AddHandler(TappedEvent, OnTapped, RoutingStrategies.Bubble);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
    }

    /// <summary>Right-clicking a card selects it and offers what can be done with it.</summary>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not ConnectionsSidebar sidebar || e.Source is not Visual source) return;

        for (Visual? step = source; step is not null; step = step.GetVisualParent())
        {
            if (step is not Control { DataContext: ConnectionRowModel row } card) continue;

            sidebar.Select(row);
            card.ContextMenu = new ContextMenu
            {
                ItemsSource = sidebar.ContextEntriesFor(row)
                    .Select(static entry =>
                    {
                        var item = new MenuItem { Header = entry.Label, IsEnabled = entry.Enabled };
                        item.Click += (_, _) => entry.Run();
                        return item;
                    })
                    .ToList()
            };
            return;
        }
    }

    /// <summary>A single click selects a card and fills the details panel; a double click opens it.</summary>
    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not ConnectionsSidebar sidebar) return;
        if (e.Source is not Visual source) return;

        for (Visual? step = source; step is not null; step = step.GetVisualParent())
        {
            // A button on the card acts on the card; it does not need to select it first.
            if (step is Button) return;
            if (step is not StyledElement { DataContext: ConnectionRowModel row }) continue;

            sidebar.Select(row);
            return;
        }
    }

    /// <summary>
    /// Double-clicking a connection opens it in the pane somebody is looking at.
    /// </summary>
    /// <remarks>
    /// The rows had no click behaviour at all, which made the panel a list of names beside a
    /// workspace it could not reach. Double rather than single, because a single click is how a
    /// drag begins and how a row is picked up to be filed in another group.
    /// </remarks>
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not ConnectionsSidebar { OpenConnection: { } open }) return;
        if (e.Source is not Visual source) return;

        for (Visual? step = source; step is not null; step = step.GetVisualParent())
        {
            if (step is not StyledElement { DataContext: ConnectionRowModel row }) continue;
            if (row.Id == Guid.Empty) return;

            open(row.Id);
            e.Handled = true;
            return;
        }
    }
}
