using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Framework;
using StorageHub.Desktop.Services;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The Edit Connection dialog: the connection editor for one connection, as 1.x's was.
/// </summary>
/// <remarks>
/// The connection is loaded when the window opens rather than when it is built, so a window that
/// never opens never asks the agent.
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

        // On the tab it was asked for, once shown, as 1.x's OnShown chose it. The tabs are always
        // the same three in the same order, so the tab is its index.
        Opened += (_, _) =>
        {
            if (DataContext is ConnectionManagerModel model && this.FindControl<TabControl>("PART_Tabs") is { } tabs)
            {
                tabs.SelectedIndex = (int)model.Tab;
            }
        };
    }

    /// <summary>
    /// The dialog over the real agent, on a saved connection or, given none, on a new one.
    /// </summary>
    /// <remarks>
    /// The key store and icon pickers are opened over this window rather than over the shell,
    /// because this window is modal to the shell and a dialog owned by something behind a modal
    /// ends up behind it too. Closing the window stops a connection still loading, as closing
    /// 1.x's cancelled its form's lifetime.
    /// </remarks>
    /// <param name="initialProvider">
    /// The provider a new connection starts on: S3, as 1.x's did, unless a caller names one.
    /// </param>
    internal static ConnectionManagerWindow ForCurrentAgent(
        Guid? connectionId = null,
        ConnectionEditorTab tab = ConnectionEditorTab.General,
        StorageProviderKind initialProvider = StorageProviderKind.S3)
    {
        var window = new ConnectionManagerWindow();
        var preferences = LoadPreferences();
        var model = new ConnectionManagerModel(
            static () => new ConnectionManagerController(
                new NamedPipeRemoteConnectionProfileClient(),
                new NamedPipeRemoteSecretVaultClient()),
            static () => new NamedPipeRemoteStorageAgentClient(),
            ShellServices.Dialogs,
            ShellServices.FilePicker,
            static () => new NamedPipeKeyStoreAgentClient(),
            entries => KeyStorePickerWindow.ChooseAsync(window, entries),
            (current, title) => IconPickerWindow.AskAsync(window, current, title),
            preferences?.ConnectionDefaults,
            preferences?.SshHostKeyDiscovery ?? SshHostKeyDiscoveryMode.AskBeforeFetching)
        {
            Tab = tab
        };
        var lifetime = new CancellationTokenSource();
        window.DataContext = model;
        window.Opened += (_, _) => _ = model.OpenAsync(connectionId, initialProvider, lifetime.Token);

        // Settings' host-key discovery is applied as the Trust tab comes forward, as 1.x's
        // SettingsTabSelected applied it.
        if (window.FindControl<TabControl>("PART_Tabs") is { } tabs)
        {
            tabs.SelectionChanged += (_, e) =>
            {
                if (e.Source == tabs && tabs.SelectedIndex == (int)ConnectionEditorTab.Trust)
                {
                    _ = model.Editor.OfferHostKeyDiscoveryAsync(lifetime.Token);
                }
            };
        }
        window.Closed += (_, _) =>
        {
            lifetime.Cancel();
            lifetime.Dispose();
        };
        return window;
    }

    /// <summary>
    /// Settings as saved, for each provider's new-connection defaults and the host-key discovery.
    /// </summary>
    /// <remarks>
    /// A settings file that cannot be read leaves the built-in defaults, as it does everywhere
    /// else: a new connection is still worth starting without them.
    /// </remarks>
    private static DesktopUpdatePreferences? LoadPreferences()
    {
        try
        {
            return new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot).Load();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }
}
