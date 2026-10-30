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
    /// group's heading opens the heading's own "..." menu.
    /// </summary>
    /// <remarks>
    /// The card's own menu, filled as it opens. It is the card's from the start: one handed to it
    /// here, while this request is on its way, is not opened by it, so the first right-click on a
    /// card opened nothing. The card is the element carrying the menu, not the first one with the
    /// row as its context, which is whatever text or icon was under the pointer. 1.x's heading went
    /// straight to the icon picker, which left two different menus on one heading.
    /// </remarks>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not ConnectionsSidebar sidebar || e.Source is not Visual source) return;

        for (Visual? step = source; step is not null; step = step.GetVisualParent())
        {
            if (step is StyledElement { DataContext: ConnectionGroupModel { ChangeIconCommand: { } changeIcon } group } heading &&
                heading.Classes.Contains("group-header"))
            {
                // The heading's own "..." menu, so a right-click and the button offer the same five
                // things. A heading without one, such as Favorites, still goes to the icon picker.
                if (group.HasMenu && heading is Visual header &&
                    header.GetVisualDescendants().OfType<Button>().FirstOrDefault(static b => b.Flyout is not null) is { } more)
                {
                    more.Flyout!.ShowAt(more);
                }
                else if (changeIcon.CanExecute(null))
                {
                    changeIcon.Execute(null);
                }

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
                    if (entry.Checked is { } isChecked)
                    {
                        item.ToggleType = MenuItemToggleType.CheckBox;
                        item.IsChecked = isChecked;
                    }

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
