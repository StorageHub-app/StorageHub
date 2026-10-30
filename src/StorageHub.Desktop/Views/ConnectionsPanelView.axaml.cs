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
        if (this.FindControl<DockPanel>("PART_DetailActions") is { } actions)
        {
            actions.SizeChanged += (_, _) => FitDetailActions(actions);
        }
    }

    /// <summary>What the details' actions need with their labels, measured while they had them.</summary>
    private double _labelledActionsWidth;

    /// <summary>
    /// Drops the labels from the details' actions, all four together, while the panel is too
    /// narrow for them, and gives them back once it is wide enough again.
    /// </summary>
    /// <remarks>
    /// Icons alone rather than a second row: the four are one row of tools wherever they are, as
    /// the pane's FILES row is, and each keeps its label as a tooltip and accessible name. The
    /// labelled width is measured while the labels show, because it cannot be measured once they
    /// are hidden.
    /// </remarks>
    private void FitDetailActions(DockPanel actions)
    {
        if (actions.Bounds.Width <= 0) return;
        var compact = actions.Classes.Contains("compact");
        if (!compact)
        {
            var natural = 0d;
            foreach (var child in actions.Children)
            {
                child.Measure(Size.Infinity);
                natural += child.DesiredSize.Width;
            }

            _labelledActionsWidth = natural;
        }

        var fits = _labelledActionsWidth <= actions.Bounds.Width;
        if (fits == !compact) return;
        actions.Classes.Set("compact", !fits);
        actions.InvalidateMeasure();
    }

    /// <summary>
    /// Right-clicking a card selects it and offers what can be done with it; right-clicking a
    /// group's heading offers to change its icon, as 1.x's did.
    /// </summary>
    /// <remarks>
    /// The card's own menu, filled as it opens. It is the card's from the start: one handed to it
    /// here, while this request is on its way, is not opened by it, so the first right-click on a
    /// card opened nothing. The card is the element carrying the menu, not the first one with the
    /// row as its context, which is whatever text or icon was under the pointer. A heading has no
    /// menu: 1.x went straight to the icon picker, which the heading's "..." also offers.
    /// </remarks>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not ConnectionsSidebar sidebar || e.Source is not Visual source) return;

        for (Visual? step = source; step is not null; step = step.GetVisualParent())
        {
            if (step is StyledElement { DataContext: ConnectionGroupModel { ChangeIconCommand: { } changeIcon } } heading &&
                heading.Classes.Contains("group-header"))
            {
                if (changeIcon.CanExecute(null)) changeIcon.Execute(null);
                e.Handled = true;
                return;
            }

            if (step is not Control { DataContext: ConnectionRowModel row, ContextMenu: { } menu }) continue;

            sidebar.Select(row);
            menu.ItemsSource = sidebar.ContextEntriesFor(row)
                .Select(static Control (entry) =>
                {
                    if (entry.Label == CommandEntry.SeparatorLabel) return new Separator();
                    var item = new MenuItem { Header = entry.Label, IsEnabled = entry.Enabled };
                    item.Click += (_, _) => entry.Run();
                    return item;
                })
                .ToList();
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
