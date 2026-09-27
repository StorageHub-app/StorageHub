using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StorageHub.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // The Workspace menu's pinned and recent workspaces sit under bold headings, and one whose
        // file has gone is dimmed, as 1.x drew them. A style cannot see an entry's model, so each
        // entry is marked as its container is made; the style in ControlThemes does the rest.
        if (this.FindControl<Menu>("PART_Menu") is { } menu)
        {
            menu.ContainerPrepared += (_, e) =>
            {
                if (e.Container is not MenuItem root) return;
                root.ContainerPrepared -= MarkEntry;
                root.ContainerPrepared += MarkEntry;
            };
        }

        // And the menu is drawn again as it opens, which is when 1.x read the lists: a file can go
        // missing, or another tab be chosen, without either list changing.
        AddHandler(MenuItem.SubmenuOpenedEvent, (_, e) =>
        {
            if (e.Source is MenuItem { DataContext: MenuSection { Menu: UiMenuId.Workspace } } &&
                DataContext is ShellPreviewModel model)
            {
                model.WorkspaceMenuOpening();
            }
        });
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Tunnelling, so the shell sees a shortcut before the focused control does - which is what
        // ProcessCmdKey did. ShellCommandRouter explains why that ordering is worth restoring.
        if (DataContext is ShellPreviewModel model) model.Router.Attach(this);
    }

    private static void MarkEntry(object? sender, ContainerPreparedEventArgs e)
    {
        var entry = (sender as ItemsControl)?.ItemFromContainer(e.Container) as CommandEntry;
        e.Container.Classes.Set("heading", entry?.IsHeading == true);
        e.Container.Classes.Set("missing", entry?.IsMissing == true);
    }
}
