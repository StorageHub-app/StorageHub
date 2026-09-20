using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Services;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The Connection Manager window.
/// </summary>
/// <remarks>
/// Every saved connection and the editor for whichever is chosen. The list is loaded when the
/// window opens rather than when it is built, so a window that never opens never asks the agent.
/// </remarks>
public partial class ConnectionManagerWindow : Window
{
    public ConnectionManagerWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ConnectionManagerModel model) model.Closed += (_, _) => Close();
        };

        Opened += (_, _) =>
        {
            if (DataContext is ConnectionManagerModel model) _ = model.RefreshAsync();
        };
    }

    /// <summary>The manager over the real agent.</summary>
    internal static ConnectionManagerWindow ForCurrentAgent() => new()
    {
        DataContext = new ConnectionManagerModel(
            static () => new NamedPipeRemoteStorageAgentClient(),
            static () => new ConnectionManagerController(
                new NamedPipeRemoteConnectionProfileClient(),
                new NamedPipeRemoteSecretVaultClient()),
            ShellServices.Dialogs)
    };
}
