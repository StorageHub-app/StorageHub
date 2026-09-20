using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;

namespace StorageHub.Desktop;

public partial class App : global::Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // The families cannot be written in XAML because they differ by platform, so they join the
        // rest of the tokens here before anything is measured with them.
        DesignTokens.Apply(this);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var model = ShellPreview.Sample;
            desktop.MainWindow = new MainWindow { DataContext = model };

            // Started here rather than in the model so the headless tests measure a shell that is
            // not polling a socket. On Linux this reaches an agent.sock under the runtime root; on
            // Windows, the named pipe - the desktop no longer knows which.
            // Held by the shutdown handler rather than by a field: the application outlives
            // nothing, so a field would only make App disposable for no one to dispose it.
            var monitor = new AgentStatusMonitor();
            model.Watch(monitor);
            desktop.ShutdownRequested += async (_, _) => await monitor.DisposeAsync().ConfigureAwait(false);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
