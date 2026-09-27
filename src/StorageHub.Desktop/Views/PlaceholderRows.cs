using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace StorageHub.Desktop.Views;

/// <summary>
/// A row that is only there to be read: an empty table's "No workspaces yet".
/// </summary>
/// <remarks>
/// A row model says so itself, rather than each table knowing which of its strings is the empty
/// one, so that every list in the application treats the line the same way (<see cref="PlaceholderRows"/>).
/// </remarks>
internal interface IPlaceholderRow
{
    /// <summary>True for the line an empty table shows in place of its rows.</summary>
    bool IsPlaceholder { get; }
}

/// <summary>
/// Keeps an empty table's message from behaving like a row.
/// </summary>
/// <remarks>
/// <para>
/// The Welcome and Sync tasks tables say what would be there when there is nothing ("No workspaces
/// yet"), as 1.4's did, and the line is a row because that is what keeps the columns and the
/// table's height as they are with rows in it. It was a row in every other way too: it lit up under
/// the pointer, took the selection when clicked, and anything that acts on the selected row would
/// have acted on it. 1.4's lists just showed the message.
/// </para>
/// <para>
/// So a placeholder's container gets the <c>placeholder</c> class, whose style in ControlThemes
/// takes it out of hit testing and out of keyboard focus: the pointer and a click pass through to
/// the table, and the arrow keys step over it. What is left, Select all and a selection set from
/// code, is undone straight after. Between them a command that acts on the selected row never gets
/// the placeholder. One that can run with nothing selected, a context menu on the whole table or a
/// double-click handler, still reaches the table from the message and has to cancel when
/// SelectedItem is null, as 1.4's workspace menu did in its Opening. Installed once for every
/// ListBox and TableView in the application (<see cref="Install"/>), so a new table gets it by
/// giving its empty row <see cref="IPlaceholderRow.IsPlaceholder"/>.
/// </para>
/// </remarks>
internal static class PlaceholderRows
{
    /// <summary>The class a placeholder's container carries, which the style in ControlThemes keys on.</summary>
    internal const string PlaceholderClass = "placeholder";

    private static bool _installed;

    /// <summary>Marks the placeholders in every list as they are shown, and keeps them unselected.</summary>
    internal static void Install()
    {
        if (_installed) return;
        _installed = true;

        // A container's DataContext is its item, and a recycled container gets a new one, so this
        // is where a row becomes, or stops being, a placeholder.
        StyledElement.DataContextProperty.Changed.AddClassHandler<ListBoxItem>(static (container, _) =>
            container.Classes.Set(PlaceholderClass, IsPlaceholder(container.DataContext)));

        SelectingItemsControl.SelectionChangedEvent.AddClassHandler<ListBox>(static (list, e) => Deselect(list, e));
    }

    /// <summary>Whether an item is an empty table's message rather than a row.</summary>
    internal static bool IsPlaceholder(object? item) => item is IPlaceholderRow { IsPlaceholder: true };

    private static void Deselect(ListBox list, SelectionChangedEventArgs e)
    {
        // A drop-down inside a row raises the same event, and it bubbles through the list.
        if (!ReferenceEquals(e.Source, list) || !e.AddedItems.Cast<object?>().Any(IsPlaceholder)) return;

        // After the change rather than inside it. Deselecting while Select all was still being
        // reported left the list holding its real rows twice. Normal rather than Post's own
        // default, which is below Render: Normal runs before the next frame is drawn, so the
        // placeholder is never seen selected, though whatever handles this change still sees it.
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var index in list.Selection.SelectedIndexes.ToArray())
            {
                if (index < list.ItemsView.Count && IsPlaceholder(list.ItemsView[index])) list.Selection.Deselect(index);
            }
        }, DispatcherPriority.Normal);
    }
}
