using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

/// <summary>The transfer queue and its activity log, below the workspaces.</summary>
public partial class TransferQueueView : UserControl
{
    public TransferQueueView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// A right-click on a row that is not part of the selection makes it the selection, as the
    /// pane's list does, so the menu acts on what was clicked rather than on rows chosen before.
    /// </summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is TransferQueueModel model &&
            e.GetCurrentPoint(this).Properties.IsRightButtonPressed &&
            e.Source is Visual source &&
            source.FindAncestorOfType<TableView>() is not null &&
            (source as StyledElement ?? source.FindAncestorOfType<StyledElement>())?.DataContext is TransferRow row &&
            !model.SelectedRows.Contains(row))
        {
            model.Selected = row;
        }
    }

    /// <summary>
    /// The table's right-click menu, as 1.x's: clear the selected history, cancel and clear the
    /// selected, and clear all history.
    /// </summary>
    /// <remarks>
    /// Filled when it opens, so each entry is enabled for the rows chosen at that moment, the one
    /// just right-clicked among them. The entries are the model's commands, so the menu can do
    /// nothing the model would not. The menu itself is the table's from the start: one handed to
    /// the table here, while this request is on its way, is not opened by it, so the first
    /// right-click opened nothing. Only on the state tabs: the Logs tab has a table of its own,
    /// and none of this means anything there.
    /// </remarks>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not TransferQueueModel model || e.Source is not Visual source) return;
        if (source.FindAncestorOfType<TableView>(includeSelf: true) is not
            { DataContext: TransferQueueTab { IsStateFilter: true }, ContextMenu: { } menu })
        {
            return;
        }

        AutomationProperties.SetName(menu, TransferQueueModel.HistoryCommandsLabel);
        menu.ItemsSource = new List<Control>
        {
            new MenuItem
            {
                Header = TransferQueueModel.ClearSelectedLabel,
                Command = model.ClearSelectedCommand,
            },
            new MenuItem
            {
                Header = TransferQueueModel.CancelAndClearLabel,
                Command = model.CancelAndClearCommand,
            },
            new Separator(),
            new MenuItem
            {
                Header = TransferQueueModel.ClearAllHistoryLabel,
                Command = model.ClearAllHistoryCommand,
            },
        };
    }

    /// <summary>
    /// The least the queue can be dragged down to: its toolbar, its tabs, the table's heading and
    /// QueueMinimumRows rows under it.
    /// </summary>
    /// <remarks>
    /// The toolbar's height is a token's, but its labels and the tab strip are text, so both are
    /// measured. The strip is the tab control's own items presenter, which is the row of tabs,
    /// measured at the queue's width: a narrow window wraps it onto a second row.
    /// </remarks>
    internal double MinimumHeight()
    {
        var height = LayoutLimits.ListBand("QueueMinimumRows");
        if (this.FindControl<Border>("PART_Toolbar") is { } toolbar) height += LayoutLimits.Natural(toolbar).Height;
        if (this.FindControl<TabControl>("PART_Tabs")?.GetVisualDescendants()
                .OfType<ItemsPresenter>().FirstOrDefault() is { } strip)
        {
            height += LayoutLimits.Natural(strip, Bounds.Width).Height;
        }

        return height;
    }
}
