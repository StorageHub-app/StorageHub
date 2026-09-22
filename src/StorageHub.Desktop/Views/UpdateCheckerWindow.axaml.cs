using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Framework;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The update window: what is installed, what is available, and the one button that moves it along.
/// </summary>
/// <remarks>
/// Closing cancels whatever is in flight, which is what makes the Cancel label during a download
/// honest. The model unsubscribes from the updater on the way out; the updater outlives this
/// window, so a model that stayed subscribed would be kept alive by it.
/// </remarks>
public partial class UpdateCheckerWindow : Window
{
    public UpdateCheckerWindow()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) =>
        {
            if (DataContext is UpdateCheckerModel model) model.Closed += (_, _) => Close();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Closed += (_, _) => (DataContext as UpdateCheckerModel)?.Dispose();
    }

    /// <summary>
    /// The window over an updater the shell already owns.
    /// </summary>
    /// <remarks>
    /// The updater is passed in rather than made here because the shell's own automatic check uses
    /// the same one: two would each hold a download of the same release, and the second would find
    /// the first one's staging directory already taken.
    /// </remarks>
    internal static UpdateCheckerWindow For(DesktopUpdater updater)
    {
        ArgumentNullException.ThrowIfNull(updater);
        return new UpdateCheckerWindow { DataContext = new UpdateCheckerModel(updater) };
    }

    /// <summary>An updater over the real settings file, for a shell that has not made one.</summary>
    internal static DesktopUpdater CreateUpdater()
    {
        var store = new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot);
        store.Preflight();
        return new DesktopUpdater(store);
    }
}
