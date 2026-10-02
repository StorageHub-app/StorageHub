using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

/// <summary>
/// Dragging a connection from one group to another, inside the connections panel.
/// </summary>
/// <remarks>
/// <para>
/// Attached to the panel rather than to each row, because the rows are rebuilt on every
/// rearrangement and handlers hung on them would have to be hung again each time. One handler on
/// the container survives every rebuild and is also the only thing that can see both ends of a
/// drag.
/// </para>
/// <para>
/// The payload is the connection's id rather than the row, because the row that is dropped is not
/// the row that will exist afterwards: the panel rebuilds from the new arrangement, and an id is
/// the only part of a row that outlives that.
/// </para>
/// </remarks>
internal static class ConnectionDragHandler
{
    /// <summary>
    /// Our own format, scoped to this application.
    /// </summary>
    /// <remarks>
    /// A connection is not a file and not text. An application format never reaches the platform
    /// clipboard, so a connection dragged out of StorageHub means nothing elsewhere -- rather than
    /// landing in a text editor as a bare GUID, which is what carrying it as text would do.
    /// </remarks>
    private static readonly DataFormat<string> Format =
        DataFormat.CreateStringApplicationFormat("StorageHub.Connection.v1");

    internal static void Attach(Control panel, Func<ConnectionsSidebar?> sidebar)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(sidebar);

        DragDrop.SetAllowDrop(panel, true);

        // From the press rather than from a movement threshold: DoDragDropAsync takes the
        // PointerPressedEventArgs, and a press that never moves simply completes as no drop.
        panel.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (RowAt(e.Source) is not { Id: var id } || id == Guid.Empty) return;
            if (!e.GetCurrentPoint(panel).Properties.IsLeftButtonPressed) return;
            // Not from a card's own buttons: a drag holds the pointer until it is let go, so the
            // edit and delete on a selected card never saw their click.
            if (OnButton(e.Source)) return;

            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(Format, id.ToString("D")));
            _ = DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
        }, RoutingStrategies.Tunnel);

        panel.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = Target(e, sidebar()) is null ? DragDropEffects.None : DragDropEffects.Move;
            e.Handled = true;
        });

        panel.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (sidebar() is not { } model || Target(e, model) is not { } target) return;
            _ = model.MoveToGroupAsync(target.Id, target.Group);
            e.Handled = true;
        });
    }

    /// <summary>
    /// Where a drag would land: which group.
    /// </summary>
    /// <remarks>
    /// Anywhere in a group's part of the panel, its heading, its cards or its "No connections"
    /// line, so an empty group can still be the first place something is filed. Within a group the
    /// connections are in name order, so where among them it is dropped does not matter.
    /// </remarks>
    private static (Guid Id, ConnectionGroupModel Group)? Target(DragEventArgs e, ConnectionsSidebar? sidebar)
    {
        if (sidebar is null ||
            e.DataTransfer?.TryGetValue(Format) is not { } text ||
            !Guid.TryParse(text, out var id) ||
            e.Source is not Visual source ||
            GroupOf(source) is not { } group)
        {
            return null;
        }

        // Favorites is made by marking a connection, not by filing it, so a drag lands nowhere over it.
        return group.IsFavorites ? null : (id, group);
    }

    /// <summary>The group whose part of the panel a visual sits in.</summary>
    private static ConnectionGroupModel? GroupOf(Visual source) =>
        Nearest<ConnectionGroupModel>(source);

    private static bool OnButton(object? source)
    {
        for (var step = source as Visual; step is not null; step = step.GetVisualParent())
        {
            if (step is Button) return true;
            if (step is ConnectionCard) return false;
        }

        return false;
    }

    private static ConnectionRowModel? RowAt(object? source) =>
        source is Visual visual ? Nearest<ConnectionRowModel>(visual) : null;

    private static T? Nearest<T>(Visual source) where T : class
    {
        for (Visual? step = source; step is not null; step = step.GetVisualParent())
        {
            if (step is StyledElement { DataContext: T found }) return found;
        }

        return null;
    }
}
