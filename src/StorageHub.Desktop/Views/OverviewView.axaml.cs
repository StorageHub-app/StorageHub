using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace StorageHub.Desktop.Views;

public partial class OverviewView : UserControl
{
    public OverviewView()
    {
        AvaloniaXamlLoader.Load(this);

        // The workspace list, as 1.x's worked it: a double-click on a row or Enter opens it, and
        // the right-click menu opens only over a row. The empty table's message is never selected,
        // so the menu cancels there, as 1.x's did in its Opening.
        if (this.FindControl<TableView>("PART_Workspaces") is { } table)
        {
            TableActivation.OnActivate(table, OpenSelected);
        }

        // A recent connection opens in the active pane, as double-clicking its card does.
        if (this.FindControl<TableView>("PART_RecentConnections") is { } recent)
        {
            TableActivation.OnActivate(recent, () =>
                recent.SelectedItem is RecentConnectionRow { ConnectionId: { } id } &&
                DataContext is OverviewModel { OpenConnection: { } openConnection } &&
                Run(() => openConnection(id)));
        }

        if (this.FindControl<ContextMenu>("PART_WorkspaceMenu") is { } menu)
        {
            menu.Opening += (_, e) => e.Cancel = (DataContext as OverviewModel)?.SelectedWorkspace?.Shortcut is null;
        }
    }

    private static bool Run(Action action)
    {
        action();
        return true;
    }

    private bool OpenSelected()
    {
        if (DataContext is not OverviewModel { OpenWorkspaceCommand: { } open } || !open.CanExecute(null)) return false;
        open.Execute(null);
        return true;
    }
}
