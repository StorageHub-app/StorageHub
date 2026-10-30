using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StorageHub.Desktop.Views;

public partial class SyncTasksView : UserControl
{
    public SyncTasksView()
    {
        AvaloniaXamlLoader.Load(this);

        // A run in Last syncs opens on the review sub-tab beside this one.
        if (this.FindControl<TableView>("PART_LastSyncs") is { } runs)
        {
            TableActivation.OnActivate(runs, () =>
            {
                if (runs.SelectedItem is not LastSyncRow { RunId: { } id } ||
                    DataContext is not SyncTasksModel { OpenRun: { } open })
                {
                    return false;
                }

                open(id);
                return true;
            });
        }
    }
}
