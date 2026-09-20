using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Services;

namespace StorageHub.Desktop.Views;

public partial class SyncProfileEditorWindow : Window
{
    public SyncProfileEditorWindow() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// The editor, pointed at the running agent.
    /// </summary>
    /// <remarks>
    /// Two clients, because the editor needs both the sync profiles and the connections they can
    /// point at, and those live on different surfaces. It loads when shown rather than here, so
    /// opening the window is not held behind an agent that may not be running.
    /// </remarks>
    internal static SyncProfileEditorWindow ForCurrentAgent()
    {
        var model = SyncProfileEditorModel.Create(
            static () => new NamedPipeSyncManagementAgentClient(),
            static () => new NamedPipeRemoteStorageAgentClient());
        var window = new SyncProfileEditorWindow { DataContext = model };
        window.Opened += (_, _) => _ = model.LoadAsync();
        return window;
    }
}
