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

            // The first command with somewhere to go. Settings opens over the shell, edits a
            // working copy, and writes through DesktopConfigStore on Apply.
            model.Router.Handle(UiCommandIds.ToolsSettings, () =>
            {
                var settings = Views.SettingsWindow.ForCurrentUser();
                _ = settings.ShowDialog(desktop.MainWindow);
            });

            // The "+" beside the tabs. It asks which arrangement, unless the answer was saved --
            // which is what the chooser's "stop asking" box does, and what makes the dialog worth
            // having rather than something to dismiss.
            model.Router.Handle(UiCommandIds.WorkspaceNewWorkspace, () => _ = AddWorkspaceAsync(model));

            // The Connection Manager, from the menu and from the panel's own New button. Both open
            // the same window: "new connection" is the manager with an empty editor, which is one
            // screen rather than a second one that would have to agree with it about every field.
            model.Router.Handle(UiCommandIds.ConnectionsNewConnection, () =>
                ShowConnections(desktop, model, startNew: true));
            model.Sidebar.ManageCommand = new RelayCommand(
                _ => ShowConnections(desktop, model, startNew: false));

            // Whatever was saved last time, before the window is shown, so the shell opens in the
            // scheme rather than flashing the default and changing.
            Views.SettingsWindow.ApplySavedScheme();

            // Started here rather than in the model so the headless tests measure a shell that is
            // not polling a socket. On Linux this reaches an agent.sock under the runtime root; on
            // Windows, the named pipe - the desktop no longer knows which.
            // Held by the shutdown handler rather than by a field: the application outlives
            // nothing, so a field would only make App disposable for no one to dispose it.
            var monitor = new AgentStatusMonitor();
            model.Watch(monitor);

            // Fire and forget, deliberately: the window opens on whatever the sidebar already says
            // and fills in when the agent answers. Awaiting here would hold the shell closed behind
            // a process that may not be running.
            _ = model.Sidebar.RefreshAsync();

            // The queue polls on its own timer and reports what the agent holds. Started here for
            // the same reason the monitor is: a headless test measures a shell that is not talking
            // to a socket unless it asked to.
            model.Queue.Start();

            // Each pane reads the connections it can be pointed at. Fire and forget, for the same
            // reason the sidebar is: the window opens on what it already has and fills in when the
            // agent answers, rather than being held closed behind a process that may not be running.
            foreach (var pane in model.Workspaces.SelectMany(Panes))
            {
                _ = pane.LoadConnectionsAsync();
            }
            desktop.ShutdownRequested += async (_, _) =>
            {
                await monitor.DisposeAsync().ConfigureAwait(false);
                await model.Queue.DisposeAsync().ConfigureAwait(false);
                foreach (var workspace in model.Workspaces
                    .Select(static tab => tab.Workspace)
                    .OfType<WorkspaceModel>())
                {
                    await workspace.DisposeAsync().ConfigureAwait(false);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Opens the Connection Manager, and refreshes the panel once it closes.
    /// </summary>
    /// <remarks>
    /// Refreshed on close rather than on every write, because the manager is modal to the shell:
    /// nothing can look at the panel while it is open, so one refresh at the end is both cheaper
    /// and the only moment it matters.
    /// </remarks>
    private static void ShowConnections(
        IClassicDesktopStyleApplicationLifetime desktop,
        ShellPreviewModel model,
        bool startNew)
    {
        var window = Views.ConnectionManagerWindow.ForCurrentAgent();
        if (startNew && window.DataContext is Views.ConnectionManagerModel manager)
        {
            manager.StartNew();
        }

        window.Closed += (_, _) => _ = model.Sidebar.RefreshAsync();
        if (desktop.MainWindow is { } owner) _ = window.ShowDialog(owner);
        else window.Show();
    }

    /// <summary>
    /// Asks which arrangement, then makes the workspace.
    /// </summary>
    /// <remarks>
    /// Dismissing the chooser makes nothing, which is the same contract every dialog in the shell
    /// has: cancel means do nothing, and the caller does not have to handle the title bar.
    /// </remarks>
    private static async Task AddWorkspaceAsync(ShellPreviewModel model)
    {
        if ((global::Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner)
        {
            return;
        }

        if (await Views.NewWorkspaceWindow.ChooseAsync(owner).ConfigureAwait(true) is { } preset)
        {
            model.AddWorkspace(preset);
        }
    }

    /// <summary>Both panes of a workspace tab, or none for a tab that is a page.</summary>
    private static IEnumerable<BrowserPaneModel> Panes(WorkspaceTab tab) =>
        tab.Workspace is { } workspace ? workspace.Panes : [];
}
