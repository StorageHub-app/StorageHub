using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Framework;
using StorageHub.Desktop.Themes;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The Settings window.
/// </summary>
/// <remarks>
/// Nothing but the colour scheme is wired here. The page on screen follows the navigation through
/// <see cref="SettingsModel.SelectedPageModel"/>, which is an ordinary binding; it used to be
/// wired in code-behind from DataContextChanged, which runs before the visual tree exists, so the
/// list it went looking for was never found and choosing a category did nothing.
/// </remarks>
public partial class SettingsWindow : Window
{
    public SettingsWindow() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// The window over the real settings file, with the colour scheme previewing as it is chosen.
    /// </summary>
    internal static SettingsWindow ForCurrentUser()
    {
        var store = new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot);
        store.Preflight();

        var model = new SettingsModel(
            store.Load,
            store.Save,
            ApplyScheme,
            Services.ShellServices.FilePicker);

        var window = new SettingsWindow { DataContext = model };
        model.Closed += (_, _) => window.Close();
        return window;
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
