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
