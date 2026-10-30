using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Opens a table's row on a double-click or Enter, as the Welcome workspace list always has.
/// </summary>
/// <remarks>
/// For the tables that list things somewhere else in the window -- a connection, a sync run -- and
/// looked as if they could be opened but did nothing. A double-click on the header or the empty
/// space under the rows is not a row, so it does nothing.
/// </remarks>
internal static class TableActivation
{
    /// <param name="open">Opens the selected row, answering whether it did.</param>
    internal static void OnActivate(TableView table, Func<bool> open)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(open);

        table.DoubleTapped += (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorOfType<TableViewRow>(includeSelf: true) is not null)
            {
                e.Handled = open();
            }
        };
        // Tunnelling, because the table is a ListBox, and a ListBox marks Enter handled in its own
        // class handler before a KeyDown handler added here would ever see it.
        table.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter) e.Handled = open();
        }, RoutingStrategies.Tunnel);
    }
}
