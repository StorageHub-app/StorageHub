using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Framework;
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
            ShellServices.Dialogs,
            NewScheduleTimeZone());
        var window = new ScheduleManagerWindow { DataContext = model };
        window.Opened += (_, _) => _ = model.LoadAsync();
        return window;
    }

    /// <summary>
    /// The zone Settings starts a new schedule on. The window is modal, so Settings cannot change
    /// it while the window is open, and reading it once is enough.
    /// </summary>
    /// <remarks>
    /// A settings file that cannot be read follows the system, which is the default anyway.
    /// </remarks>
    private static string? NewScheduleTimeZone()
    {
        try
        {
            return new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot).Load().NewScheduleTimeZone;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }
}
