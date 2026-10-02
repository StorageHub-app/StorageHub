using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Settings;
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

        // Every table's columns keep a minimum width, whatever is dragged. Here rather than in the
        // desktop branch below so the headless tests run under the same rules.
        TableColumnRules.Install();

        // And an empty table's "No workspaces yet" is a message, not a row anybody can select.
        PlaceholderRows.Install();

        // And every button, tab, row and menu entry is named for a screen reader by what it shows.
        AccessibleNames.Install();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The splash first, as 1.x opened: settings, the framework and the language, then the
            // agent, and only then the shell. The shell used to open at once, before any of those
            // had happened -- which is why it spoke English whatever was configured and sat at
            // "Agent: not connected" when the service was not already running.
            Services.DesktopErrors.Install(desktop);
            Services.DesktopBoot.Start(desktop, OpenShell);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Builds the shell and wires every command that opens a window. Called by the boot once the
    /// agent is answering, with the splash still up.
    /// </summary>
    private static MainWindow OpenShell(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var model = ShellPreview.CreateLive();
        var window = new MainWindow { DataContext = model };

        // Centred on the screen, as 1.x opened, and made smaller where the screen is.
        window.FitToScreen();

        // When the agent drops, the shell brings it back the way startup did.
        model.RecoverAgent = DesktopAgentStartup.EnsureAsync;

        // And restarts it for a saved change to what it reads only as it starts, the concurrency
        // and total speed limits, once the transfers running then have finished.
        model.AgentLifecycle = AgentLifecycleControllers.ThatCanRestartTheAgent;

        // The first command with somewhere to go. Settings opens over the shell, edits a
        // working copy, and writes through DesktopConfigStore on Apply.
        model.Router.Handle(UiCommandIds.ToolsSettings, () => ShowSettings(pageKey: null));

        // Speed limits live on Transfers & sync beside concurrency, since both are how the
        // agent's transfers run; a connection's own limit is in its Edit Connection dialog.
        model.Router.Handle(
            UiCommandIds.TransferSpeedLimits, () => ShowSettings(SettingsPageCatalog.PerformancePageKey));

        void ShowSettings(string? pageKey)
        {
            // A provider's page can open the Edit Connection dialog, so the panel and Welcome are
            // re-read after a save there the way they are after one from the panel.
            var settings = Views.SettingsWindow.ForCurrentUser(
                pageKey,
                () =>
                {
                    _ = model.Sidebar.RefreshAsync();
                    _ = model.Overview?.RefreshAsync();
                },
                () => _ = model.ApplyAgentSettingsAsync());

            // The connections panel's side is a setting there too, and moves the panel as it closes.
            settings.Closed += (_, _) => FollowSettings(model);

            // A new language is read only as windows are built, so the shell closes and starts
            // again, as 1.4 did. Closing the ordinary way means the cleanup below still runs, and
            // Program.Main starts the new shell only once this process has let go of everything.
            // The shell's close is posted, so Settings has finished closing and handed it back first.
            settings.Closed += (_, _) =>
            {
                if (settings.DataContext is not SettingsModel { LanguageRestartRequested: true }) return;
                DesktopRestart.Request();
                Dispatcher.UIThread.Post(() => window.Close());
            };
            _ = settings.ShowDialog(window);
        }

        // The "+" beside the tabs. It asks which arrangement, unless the answer was saved --
        // which is what the chooser's "stop asking" box does, and what makes the dialog worth
        // having rather than something to dismiss.
        model.Router.Handle(UiCommandIds.WorkspaceNewWorkspace, () => _ = AddWorkspaceAsync(model));
        model.Router.Handle(UiCommandIds.WorkspaceCloseWorkspace, () => _ = model.CloseWorkspaceAsync());

        // Workspace files, as 1.x kept them: the same .shw, so either version opens the other's.
        // Saving and opening both put the file at the front of the recent list.
        if (model.Files is { } files)
        {
            model.Router.Handle(UiCommandIds.WorkspaceOpenWorkspace, () => _ = files.OpenAsync());
            model.Router.Handle(UiCommandIds.WorkspaceSaveWorkspace, () => _ = files.SaveAsync(saveAs: false));
            model.Router.Handle(UiCommandIds.WorkspaceSaveWorkspaceAs, () => _ = files.SaveAsync(saveAs: true));
            model.Router.Handle(UiCommandIds.WorkspaceRenameWorkspace, () => _ = files.RenameAsync());
        }

        // Exit closes the window, as 1.x's called Close(): the same way out as the title bar's X,
        // so it asks about each changed workspace and shuts down through the Closing handler below.
        model.Router.Handle(UiCommandIds.WorkspaceExit, () => window.Close());

        // 1.x's plain Edit Connection dialog. New Connection opens it on a new connection, from
        // the menu, the toolbar, the panel's New button and Welcome's Connections; Edit opens it on
        // the connection it was pressed on, from a card, its menu or the details panel, and the
        // details panel's "Fix credentials…" and "Review trust…" open it on the tab that fixes it.
        // Listing, testing and deleting are the panel's own, as they were in 1.x.
        model.Router.Handle(UiCommandIds.ConnectionsNewConnection, () => ShowConnectionEditor(desktop, model));
        model.Sidebar.EditConnection = (id, tab) => ShowConnectionEditor(desktop, model, id, tab);

        // The key store: import a key or a certificate once, and reference it from any number
        // of connections. It is what the connection editor's "Key Store…" buttons pick from.
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
        model.Router.Handle(UiCommandIds.ToolsExportSettings, () => ShowExportSettings(desktop, model));
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

        // 1.4's update link: the status bar's last cell says what the updater last said. And as
        // in 1.4, an update that is to install closes the shell, the same way Exit does, so each
        // changed workspace is asked about first; the installer starts once the shell has gone.
        model.ShowUpdate(updater.Snapshot);
        updater.StatusChanged += (_, snapshot) => Dispatcher.UIThread.Post(() => model.ShowUpdate(snapshot));
        updater.RestartRequested += (_, _) => Dispatcher.UIThread.Post(() => window.Close());
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

        // The Welcome page asks the agent for itself once the window is up; after that it
        // reloads on its Refresh button and whenever the agent reconnects. Each pane reads
        // its own connections as it is made.
        _ = model.Overview?.RefreshAsync();
        // An edited file that was uploaded shows up in the pane it came from.
        Services.ShellServices.EditedFileUploaded += (_, _) => _ = model.EditedFileUploadedAsync();

        // The first time an installed build opens, it asks how StorageHub should run, as 1.4 did:
        // once the window is up, so the shell is usable behind the question.
        window.Opened += OfferAgentHostMode;
        void OfferAgentHostMode(object? sender, EventArgs e)
        {
            window.Opened -= OfferAgentHostMode;
            _ = CheckForUpdatesOnStartAsync();
            _ = AgentHostModeWindow.OfferOnceAsync(window);
        }

        // 1.4 checked once its window was shown, as the update settings say: not at all with
        // automatic checks off, fetching what it found unless downloads are off, and installing
        // it, which closes the shell, only with automatic restart on.
        async Task CheckForUpdatesOnStartAsync()
        {
            try
            {
                await updater.RunAutomaticAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException)
            {
                // Closing the shell, or turning automatic checks off, ends the check.
            }
        }

        // Everything the shell holds, closed before the window goes, after each changed workspace
        // has asked, as 1.x did; Cancel on any of them leaves the shell exactly as it was. Avalonia
        // does not wait for an async Closing handler, so the first close is held until this is done.
        Services.ShellShutdown.Attach(window, ConfirmCloseAsync, Held(), StopAgentAsync);

        // A restart into a new language closes the shell the same way, so Cancel on a changed
        // workspace drops the restart with the close; left asked for, the next Exit would come
        // back up.
        async Task<bool> ConfirmCloseAsync()
        {
            var closing = await (model.Files?.ConfirmExitAsync() ?? Task.FromResult(true)).ConfigureAwait(true);
            if (!closing) DesktopRestart.Reset();
            return closing;
        }

        IEnumerable<Func<Task>> Held()
        {
            // No restart for saved settings starts after this, not even from a status that arrives
            // late: one could relaunch the agent beside the stop below, or race the next shell's
            // startup on a language restart. One already running is waited for before that stop.
            yield return () =>
            {
                model.StopRestartingAgent();
                return Task.CompletedTask;
            };

            yield return () =>
            {
                updater.Dispose();
                return Task.CompletedTask;
            };

            // A sync run under review is watched by polling the agent, which must not go on
            // through the agent's own stop.
            yield return () =>
            {
                model.SyncRunHistory?.Dispose();
                return Task.CompletedTask;
            };
            yield return () => Services.ShellServices.CloseEditingAsync().AsTask();
            yield return () => monitor.DisposeAsync().AsTask();
            yield return () => model.Queue.DisposeAsync().AsTask();

            // Each workspace closes its panes' connections and shells as it goes.
            foreach (var workspace in model.Workspaces
                .Select(static tab => tab.Workspace)
                .OfType<WorkspaceModel>()
                .ToArray())
            {
                yield return () => workspace.DisposeAsync().AsTask();
            }
        }

        // "Only while StorageHub is open" means exactly that, so the agent goes with the window, as
        // it did in 1.x. Only Windows has that mode; on Linux the agent is the user's unit. A restart
        // for saved settings is finished first rather than cut off. Not on a restart of the shell:
        // it is back in a moment, and stopping the agent would interrupt the transfers and syncs it
        // is running for nothing. Nor an agent started by hand for a build run from source, which the
        // shell used as it found it and leaves the same way.
        async Task StopAgentAsync()
        {
            await model.AgentSettingsRestart.ConfigureAwait(true);
            if (!OperatingSystem.IsWindows() || !DesktopAgentHost.DesktopStopsAgent || DesktopRestart.Requested) return;
            using var lifecycle = WindowsDesktopLifecycle.Create();
            if (DesktopAgentStartup.RunsAnAgentStartedByHand(lifecycle)) return;
            _ = await lifecycle.TryStopAgentAsync(AgentShutdownReason.Restart).ConfigureAwait(true);
        }

        return window;
    }

    /// <summary>
    /// Brings the shell into line with what Settings saved.
    /// </summary>
    /// <remarks>
    /// Rebound shortcuts, the toolbar's layout and labels, and the connections panel's side, which
    /// is a setting as well as a View command, all follow when the window closes rather than at the
    /// next start. Settings is modal, so nothing can be pressed in the shell before then.
    /// </remarks>
    private static void FollowSettings(ShellPreviewModel model)
    {
        if (ShellPreview.ReadPreferences() is { } saved) model.FollowSettings(saved);
    }

    /// <summary>
    /// Opens the Edit Connection dialog on a connection, or on a new one, and brings the panel and
    /// Welcome's recent connections up to date whenever it writes one, as 1.x did.
    /// </summary>
    /// <remarks>
    /// Refreshed on every write rather than once the dialog closes, as 1.x's ProfilesChanged did: a
    /// save still in flight when Cancel is pressed lands after the close, and one whose pin was
    /// refused has written the profile although the dialog stays open.
    /// </remarks>
    private static void ShowConnectionEditor(
        IClassicDesktopStyleApplicationLifetime desktop,
        ShellPreviewModel model,
        Guid? connectionId = null,
        Views.ConnectionEditorTab tab = Views.ConnectionEditorTab.General)
    {
        var window = Views.ConnectionManagerWindow.ForCurrentAgent(connectionId, tab);
        if (window.DataContext is Views.ConnectionManagerModel editor)
        {
            editor.ProfilesChanged += (_, _) =>
            {
                _ = model.Sidebar.RefreshAsync();
                _ = model.Overview?.RefreshAsync();
            };
        }

        if (desktop.MainWindow is { } owner) _ = window.ShowDialog(owner);
        else window.Show();
    }

    /// <summary>Opens the export dialog, and says in the status bar where the file went, as 1.x did.</summary>
    private static void ShowExportSettings(
        IClassicDesktopStyleApplicationLifetime desktop,
        ShellPreviewModel model)
    {
        var window = Views.SettingsExportWindow.ForCurrentUser();
        window.Closed += (_, _) =>
        {
            if (window.DataContext is Views.SettingsExportModel { ExportedPath: { } path })
            {
                model.Say(Ui.Format(Ui.Shell.SettingsExportedFormat, Path.GetFileName(path)));
            }
        };

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

            // An import can replace the pinned and recent workspaces too, and Welcome would go on
            // listing the old ones until something else changed them, as 1.x's did not.
            model.ShowWorkspaceShortcuts();

            // And the shortcuts, which 1.x's import refreshed as well, with the toolbar, which a
            // General section can bring and 1.x left until the next start.
            FollowSettings(model);
            model.Say(Ui.Shell.StatusSettingsImported);

            // The import's summary says the agent restarts for new concurrency, and as in 1.4 it
            // does, by the same rule as a change made in Settings. After "Settings imported", as
            // 1.4 said them, so the status bar goes straight to the wait or the restart.
            if (import.Report is { ConcurrencyChanged: true }) _ = model.ApplyAgentSettingsAsync();
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
    /// the shell switches to the one screen that reviews runs. The editor closes itself after a
    /// plain preview, and stays open over the run after one that warns, until the warning is read.
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
            editor.PreviewReady += (_, run) => model.ReviewRun(run.SyncRunId);
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
}
