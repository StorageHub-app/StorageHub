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

            // The key store: import a key or a certificate once, and reference it from any number
            // of connections. It is what the Connection Manager's "Key Store…" buttons pick from.
            model.Router.Handle(UiCommandIds.ConnectionsKeyStore, () => ShowKeyStore(desktop));

            // The sync profile editor, from the menu and from the tasks screen's New button.
            // Review & run opens the same window: in 1.x it was a second entry point into the same
            // form, and previewing is what its primary button already does.
            model.Router.Handle(UiCommandIds.SyncSyncProfiles, () => ShowSyncEditor(desktop, model));
            model.Router.Handle(UiCommandIds.SyncReviewRun, () => ShowSyncEditor(desktop, model));
            // And the schedule manager, which is what turns a profile into something that runs
            // without anybody present.
            model.Router.Handle(UiCommandIds.SyncSchedules, () => ShowSchedules(desktop, model));

            // Settings out to a file and back in again. Import is the one that can change what
            // the agent holds, so the shell refreshes itself when it reports that it did.
            model.Router.Handle(UiCommandIds.ToolsExportSettings, () => ShowExportSettings(desktop));
            model.Router.Handle(UiCommandIds.ToolsImportSettings, () => ShowImportSettings(desktop, model));

            // The background agent's own screen. It is the only place the agent can be started or
            // stopped from, which matters most on Linux: the .deb installs the unit but leaves
            // enabling it to each user, and its own postinst points them here.
            model.Router.Handle(UiCommandIds.ToolsBackgroundAgent, () => ShowAgentControl(desktop, model));

            // One updater for the session, shared with the window that shows it. Two would each
            // stage the same release into the same directory, and the second would find it taken.
            var updater = Views.UpdateCheckerWindow.CreateUpdater();
            model.Router.Handle(
                UiCommandIds.HelpCheckForUpdates, () => ShowUpdateChecker(desktop, updater));
            model.Router.Handle(UiCommandIds.HelpAboutStorageHub, () =>
            {
                var about = Views.AboutWindow.Create();
                if (desktop.MainWindow is { } owner) _ = about.ShowDialog(owner);
                else about.Show();
            });
            if (model.SyncTasks is { } syncTasks)
            {
                syncTasks.NewProfileCommand = new RelayCommand(
                    _ => ShowSyncEditor(desktop, model, startNew: true));
                syncTasks.SchedulesCommand = new RelayCommand(_ => ShowSchedules(desktop, model));
            }

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
            // An edited file that was uploaded shows up in the pane it came from.
            Services.ShellServices.EditedFileUploaded += (_, _) => _ = model.EditedFileUploadedAsync();

            desktop.ShutdownRequested += async (_, _) =>
            {
                updater.Dispose();
                await Services.ShellServices.CloseEditingAsync().ConfigureAwait(false);
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

    /// <summary>Opens the export dialog.</summary>
    private static void ShowExportSettings(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = Views.SettingsExportWindow.ForCurrentUser();
        if (desktop.MainWindow is { } owner) _ = window.ShowDialog(owner);
        else window.Show();
    }

    /// <summary>
    /// Opens the import wizard, and refreshes the shell when it changed something.
    /// </summary>
    /// <remarks>
    /// An import can replace the connections, sync tasks and schedules the shell is showing, so
    /// the panel and the tasks screen are re-read on close -- but only when something was actually
    /// applied, since the common way out of this window is to look and cancel.
    /// </remarks>
    private static void ShowImportSettings(
        IClassicDesktopStyleApplicationLifetime desktop,
        ShellPreviewModel model)
    {
        var window = Views.SettingsImportWindow.ForCurrentUser();
        window.Closed += (_, _) =>
        {
            if (window.DataContext is not Views.SettingsImportModel import || !import.Changed) return;
            _ = model.Sidebar.RefreshAsync();
            _ = model.SyncTasks?.RefreshAsync();
        };

        if (desktop.MainWindow is { } owner) _ = window.ShowDialog(owner);
        else window.Show();
    }

    /// <summary>
    /// Opens the agent control window, reading whatever the shell last heard from the agent.
    /// </summary>
    /// <remarks>
    /// The status is passed as a function rather than a value because the window polls: the agent
    /// it is showing may start or stop while it is open, which is rather the point of it.
    /// </remarks>
    private static void ShowAgentControl(
        IClassicDesktopStyleApplicationLifetime desktop,
        ShellPreviewModel model)
    {
        var window = Views.AgentControlWindow.ForCurrentAgent(() => model.AgentStatus);
        if (desktop.MainWindow is { } owner) _ = window.ShowDialog(owner);
        else window.Show();
    }

    /// <summary>Opens the update window over the shell's own updater.</summary>
    private static void ShowUpdateChecker(
        IClassicDesktopStyleApplicationLifetime desktop,
        DesktopUpdater updater)
    {
        var window = Views.UpdateCheckerWindow.For(updater);
        if (desktop.MainWindow is { } owner) _ = window.ShowDialog(owner);
        else window.Show();
    }

    /// <summary>Opens the key store.</summary>
    private static void ShowKeyStore(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = Views.KeyStoreWindow.ForCurrentAgent();
        if (desktop.MainWindow is { } owner) _ = window.ShowDialog(owner);
        else window.Show();
    }

    /// <summary>
    /// Opens the sync profile editor, and takes a preview through to the review tab.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Previewing produces a run, and a run is reviewed and approved on the Run history screen --
    /// which already exists, and is where approving is guarded. 1.x solved this by embedding a
    /// second copy of the review control inside the editor, so a run could be approved from two
    /// places with two sets of buttons to keep in agreement. Here the editor hands the run over and
    /// the shell switches to the one screen that reviews runs.
    /// </para>
    /// <para>
    /// The tasks screen is refreshed on close for the same reason the connections panel is: the
    /// window is modal, so nothing can be looking at the list while it is open.
    /// </para>
    /// </remarks>
    private static void ShowSyncEditor(
        IClassicDesktopStyleApplicationLifetime desktop,
        ShellPreviewModel model,
        bool startNew = false)
    {
        var window = Views.SyncProfileEditorWindow.ForCurrentAgent();
        if (window.DataContext is Views.SyncProfileEditorModel editor)
        {
            if (startNew) editor.BeginNewProfile();
            editor.PreviewReady += (_, run) =>
            {
                window.Close();
                model.ReviewRun(run.SyncRunId);
            };
        }

        window.Closed += (_, _) => _ = model.SyncTasks?.RefreshAsync();
        if (desktop.MainWindow is { } owner) _ = window.ShowDialog(owner);
        else window.Show();
    }

    /// <summary>
    /// Opens the schedule manager.
    /// </summary>
    /// <remarks>
    /// The tasks screen is refreshed on close because enabling or deleting a schedule changes what
    /// the counts there mean, and the window is modal so one refresh at the end is the only moment
    /// it matters.
    /// </remarks>
    private static void ShowSchedules(
        IClassicDesktopStyleApplicationLifetime desktop,
        ShellPreviewModel model)
    {
        var window = Views.ScheduleManagerWindow.ForCurrentAgent();
        window.Closed += (_, _) => _ = model.SyncTasks?.RefreshAsync();
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
