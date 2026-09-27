using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using StorageHub.Agent;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Framework;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Services;
using StorageHub.Desktop.Themes;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The Settings window.
/// </summary>
/// <remarks>
/// Only what a binding cannot say is wired here: the colour scheme, keys, and the machine's own
/// services. The page on screen follows the navigation through
/// <see cref="SettingsModel.SelectedPageModel"/>, which is an ordinary binding; it used to be
/// wired in code-behind from DataContextChanged, which runs before the visual tree exists, so the
/// list it went looking for was never found and choosing a category did nothing.
/// </remarks>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // The shortcut box takes the chord before the box itself can act on it, which is what
        // lets Ctrl+A or Delete be assigned rather than select or delete the box's text. Tab and
        // Escape pass through, so the keyboard can still leave the box and close the window.
        AddHandler(KeyDownEvent, CaptureShortcut, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, FoldNavigation, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Undoes a previewed scheme however the window closed. Cancel has already done it, and OK
    /// saved it, so this is for the title bar's X and Alt+F4.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        (DataContext as SettingsModel)?.Discard();
        base.OnClosed(e);
    }

    /// <summary>
    /// Left and Right fold the rail's groups, as they did in 1.4's tree.
    /// </summary>
    /// <remarks>
    /// Folding rebuilds the list, which takes the focus away with the row that had it, so the
    /// focus is put back on the row now selected for the next key to land on.
    /// </remarks>
    private void FoldNavigation(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right) ||
            e.KeyModifiers != KeyModifiers.None ||
            (e.Source as Visual)?.FindAncestorOfType<ListBox>(includeSelf: true) is not { } list ||
            !list.Classes.Contains("settings-nav") ||
            DataContext is not SettingsModel model ||
            !model.Fold(open: e.Key == Key.Right))
        {
            return;
        }

        e.Handled = true;
        list.UpdateLayout();
        if (model.SelectedEntry is { } selected)
        {
            list.ContainerFromItem(selected)?.Focus(NavigationMethod.Directional);
        }
    }

    private static void CaptureShortcut(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Tab or Key.Escape ||
            (e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is not { Name: "ShortcutCapture" } box ||
            box.DataContext is not ShortcutsPageModel page)
        {
            return;
        }

        page.Capture(new KeyGesture(e.Key, e.KeyModifiers));
        e.Handled = true;
    }

    /// <summary>
    /// The window over the real settings file, with the colour scheme previewing as it is chosen.
    /// </summary>
    /// <param name="pageKey">The catalog page to open on, such as "performance"; null for the first.</param>
    /// <param name="connectionsChanged">
    /// Told when a Connection Manager opened from a provider's page closes, so the shell can
    /// re-read its connections the way it does after its own manager.
    /// </param>
    /// <remarks>
    /// Saving a change the agent reads -- concurrency, total speed limits -- restarts the agent,
    /// because it reads them only when it starts. Before this, such a change was written and then
    /// did nothing until the agent happened to restart.
    /// </remarks>
    internal static SettingsWindow ForCurrentUser(string? pageKey = null, Action? connectionsChanged = null)
    {
        var store = new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot);
        store.Preflight();
        SettingsWindow? window = null;

        var model = new SettingsModel(
            store.Load,
            preferences =>
            {
                var before = store.Load();
                store.Save(preferences);
                if (before.ChangesWhatTheAgentReads(preferences))
                {
                    _ = RestartAgentAsync(() => window);
                }
            },
            ApplyScheme,
            ShellServices.FilePicker,
            () => ImportPrivateKeyAsync(() => window),
            provider => CreateConnection(window, provider, connectionsChanged),
            new AgentModeServices(static () => DesktopAgentHost.Mode, OperatingSystem.IsWindows() ? ApplyAgentMode : null));
        if (pageKey is not null)
        {
            model.SelectPage(pageKey);
        }

        window = new SettingsWindow { DataContext = model };
        model.Closed += (_, _) => window.Close();
        return window;
    }

    /// <summary>
    /// Opens the Connection Manager on a new connection of one provider, over this window, as the
    /// button under a provider's defaults did in 1.4.
    /// </summary>
    private static void CreateConnection(Window? owner, StorageProviderKind provider, Action? connectionsChanged)
    {
        var manager = ConnectionManagerWindow.ForCurrentAgent();
        if (manager.DataContext is ConnectionManagerModel model)
        {
            model.StartNew();
            model.Editor.Provider = ConnectionProviderCatalog.Get(provider);
        }

        manager.Closed += (_, _) => connectionsChanged?.Invoke();
        if (owner is not null) _ = manager.ShowDialog(owner);
        else manager.Show();
    }

    /// <summary>
    /// Enrols a private key file in the vault, for the default SFTP or SSH key, and answers with
    /// its reference, or nothing when it was cancelled or refused.
    /// </summary>
    /// <remarks>
    /// 1.4's import, unchanged: the file is read once, handed to the agent, and wiped from memory;
    /// only the opaque reference reaches the settings file. A link, an empty file and one larger
    /// than the vault takes are refused before anything is read.
    /// </remarks>
    private static async Task<string?> ImportPrivateKeyAsync(Func<Window?> owner)
    {
        var path = await ShellServices.FilePicker.PickFileAsync(new Shell.FilePickerRequest
        {
            Title = Ui.Settings.SelectPrivateKeyTitle,
            Filters =
            [
                new Shell.FilePickerFilter(Ui.KeyStore.PrivateKeyFiles, ["key", "pem"]),
                new Shell.FilePickerFilter(Ui.KeyStore.AllFiles, ["*"])
            ]
        }).ConfigureAwait(true);
        if (path is null)
        {
            return null;
        }

        var dialogs = new AvaloniaDialogService(owner);
        byte[]? material = null;
        try
        {
            var file = new FileInfo(path);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                file.Length is <= 0 or > SecretVaultIpcContract.MaximumSecretBytes)
            {
                throw new IOException(Ui.Settings.PrivateKeyUnavailable);
            }

            material = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
            await using var vault = new NamedPipeRemoteSecretVaultClient();
            var response = await vault.EnrollAsync(SecretMaterialPurpose.SshPrivateKey, material).ConfigureAwait(true);
            if (response.Succeeded && !string.IsNullOrWhiteSpace(response.Reference))
            {
                return response.Reference;
            }

            await dialogs.ShowAsync(new Shell.DialogRequest
            {
                Title = Ui.Dialogs.DefaultSshPrivateKeyCaption,
                Message = response.Failure?.Message ?? Ui.Dialogs.DefaultSshPrivateKeyImportFailed,
                Severity = Shell.DialogSeverity.Warning
            }).ConfigureAwait(true);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or TimeoutException or System.Text.Json.JsonException)
        {
            await dialogs.ShowAsync(new Shell.DialogRequest
            {
                Title = Ui.Dialogs.DefaultSshPrivateKeyCaption,
                Message = Ui.Dialogs.DefaultSshPrivateKeyVaultFailed,
                Severity = Shell.DialogSeverity.Warning
            }).ConfigureAwait(true);
            return null;
        }
        finally
        {
            if (material is not null)
            {
                CryptographicOperations.ZeroMemory(material);
            }
        }
    }

    /// <summary>
    /// Moves the agent between starting at sign-in and running only while StorageHub is open.
    /// </summary>
    /// <remarks>
    /// The two differ only by the sign-in registration, so this adds or removes it and then asks
    /// the machine which mode it is now in, since policy or the environment switch can refuse the
    /// registration. Windows only, as in 1.4; on Linux the systemd unit is the Agent control
    /// screen's business, and the page says how the agent runs without offering to change it.
    /// </remarks>
    private static AgentModeChange ApplyAgentMode(AgentHostMode desired)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new AgentModeChange(false, Ui.Settings.AgentModeChangeFailed);
        }

        try
        {
            using var lifecycle = WindowsDesktopLifecycle.Create();
            _ = desired == AgentHostMode.UserSession
                ? lifecycle.ConfigureAutostart(force: true)
                : lifecycle.RemoveAutostart();
            DesktopAgentHost.Invalidate();
            return DesktopAgentHost.Mode == desired
                ? new AgentModeChange(true, Ui.Settings.AgentModeApplied)
                : new AgentModeChange(false, Ui.Settings.AgentModeChangeFailed);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or
            IOException or UnauthorizedAccessException)
        {
            return new AgentModeChange(false, Ui.Settings.AgentModeChangeFailed);
        }
    }

    /// <summary>
    /// Restarts the agent so it picks up what was saved, and says so if it could not.
    /// </summary>
    /// <remarks>
    /// Nothing is said when it works: the status bar already shows the agent going and coming back.
    /// A failure is said over whichever window is still open, which after OK is the shell.
    /// </remarks>
    private static async Task RestartAgentAsync(Func<Window?> settingsWindow)
    {
        if (AgentLifecycleControllers.ForThisMachine() is not { } controller)
        {
            return;
        }

        AgentLifecycleResult result;
        try
        {
            result = await controller.ExecuteAsync(AgentLifecycleAction.Restart).ConfigureAwait(true);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            result = new AgentLifecycleResult(false, error.Message);
        }

        if (result.Succeeded)
        {
            return;
        }

        var owner = settingsWindow() is { IsVisible: true } open
            ? open
            : (global::Avalonia.Application.Current?.ApplicationLifetime as
                global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        await new Services.AvaloniaDialogService(() => owner).ShowAsync(new Shell.DialogRequest
        {
            Title = Ui.Settings.WindowTitle,
            Message = Ui.Shell.ConcurrencyAgentRestartFailed,
            Detail = result.Message,
            Severity = Shell.DialogSeverity.Warning
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Applies whatever scheme the settings file already holds, at startup.
    /// </summary>
    /// <remarks>
    /// Before the window is shown, so the shell opens in the chosen scheme rather than flashing the
    /// default and changing. A settings file that cannot be read is not worth failing startup for:
    /// the shell then opens on the tokens as authored, which is the house dark scheme.
    /// <para>
    /// Returns what the check of the settings files found. This is the first check of the session,
    /// and the only one that can see a damaged file: it sets the file aside, so every later one
    /// finds a clean set.
    /// </para>
    /// </remarks>
    internal static ConfigPreflightReport ApplySavedScheme()
    {
        try
        {
            var store = new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot);
            var report = store.Preflight();
            ApplyScheme(store.Load());
            return report;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or InvalidOperationException)
        {
            return ConfigPreflightReport.Empty;
        }
    }

    /// <summary>
    /// Resolves what the preferences ask for and puts it on screen.
    /// </summary>
    /// <remarks>
    /// The appearance decides which half of a pair is used, so "Solarized" plus "follow the system"
    /// is Solarized Dark on a dark desktop and Solarized Light on a light one. A scheme with no
    /// counterpart stays itself, which is why Dracula does not turn into anything at sunrise.
    /// </remarks>
    internal static void ApplyScheme(DesktopUpdatePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var application = global::Avalonia.Application.Current;
        if (application is null)
        {
            return;
        }

        var preferDark = preferences.Appearance switch
        {
            DesktopAppearance.Light => false,
            DesktopAppearance.Dark => true,
            _ => application.ActualThemeVariant != global::Avalonia.Styling.ThemeVariant.Light
        };

        var chosen = ColorSchemeCatalog.Resolve(preferences.ColorScheme, preferDark);
        ColorSchemeApplier.Apply(application, ColorSchemeCatalog.ForAppearance(chosen, preferDark));
    }
}
