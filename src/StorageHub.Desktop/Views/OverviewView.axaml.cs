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
            table.DoubleTapped += (_, e) =>
            {
                if (e.Source is Visual source && source.FindAncestorOfType<TableViewRow>(includeSelf: true) is not null)
                {
                    e.Handled = OpenSelected();
                }
            };
            // Tunnelling, because the table is a ListBox, and a ListBox marks Enter handled in its
            // own class handler before a KeyDown handler added here would ever see it.
            table.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.Enter) e.Handled = OpenSelected();
            }, RoutingStrategies.Tunnel);
        }

        if (this.FindControl<ContextMenu>("PART_WorkspaceMenu") is { } menu)
        {
            menu.Opening += (_, e) => e.Cancel = (DataContext as OverviewModel)?.SelectedWorkspace?.Shortcut is null;
        }
    }

    private bool OpenSelected()
    {
        if (DataContext is not OverviewModel { OpenWorkspaceCommand: { } open } || !open.CanExecute(null)) return false;
        open.Execute(null);
        return true;
    }
}
