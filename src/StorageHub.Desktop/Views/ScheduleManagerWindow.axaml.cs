using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Services;

namespace StorageHub.Desktop.Views;

public partial class ScheduleManagerWindow : Window
{
    public ScheduleManagerWindow() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// The manager, pointed at the running agent.
    /// </summary>
    /// <remarks>
    /// Two clients, because a schedule names a sync profile and those live on different surfaces.
    /// It takes the dialog service for one reason: deleting a schedule cannot be undone, and its
    /// run history is then the only record it ever existed.
    /// </remarks>
    internal static ScheduleManagerWindow ForCurrentAgent()
    {
        var model = ScheduleManagerModel.Create(
            static () => new NamedPipeScheduleManagementAgentClient(),
            static () => new NamedPipeSyncManagementAgentClient(),
            ShellServices.Dialogs);
        var window = new ScheduleManagerWindow { DataContext = model };
        window.Opened += (_, _) => _ = model.LoadAsync();
        return window;
    }
}
