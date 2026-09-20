using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Services;

namespace StorageHub.Desktop.Views;

public partial class KeyStoreWindow : Window
{
    public KeyStoreWindow() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// The key store, pointed at the running agent.
    /// </summary>
    /// <remarks>
    /// Two clients, because metadata travels on the ordinary pipe and material is enrolled only on
    /// the dedicated secret pipe, which never reads anything back. The import dialog is opened over
    /// this window, so it is modal to the store rather than to the shell behind it.
    /// </remarks>
    internal static KeyStoreWindow ForCurrentAgent()
    {
        KeyStoreWindow? window = null;
        var model = KeyStoreModel.Create(
            static () => new NamedPipeKeyStoreAgentClient(),
            static () => new NamedPipeRemoteSecretVaultClient(),
            ShellServices.Dialogs,
            kind => KeyStoreImportWindow.AskAsync(window!, kind));
        window = new KeyStoreWindow { DataContext = model };
        window.Opened += (_, _) => _ = model.LoadAsync();
        return window;
    }
}
