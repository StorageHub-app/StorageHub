using System.Collections.Specialized;
using System.ComponentModel;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Views;

/// <summary>
/// The shell's View toggles that are not a pane's own: the transfer queue, the session log under
/// it, hidden files in every pane, and the theme.
/// </summary>
/// <remarks>
/// <para>
/// What <see cref="ConnectionsPanelLayout"/> is for the connections panel: whether each is on, the
/// tick the View menu and the toolbar draw for it, and where a change is saved. The queue and
/// hidden files are saved as they change, so the shell reopens the way it was left; the theme is
/// the Appearance setting Settings already has, cycled.
/// </para>
/// <para>
/// None of these did anything in 1.4: the four View entries were declared and never wired, and the
/// queue, its log and hidden files had no switch anywhere. The theme had one, in Settings, which is
/// what this one changes.
/// </para>
/// </remarks>
internal sealed class ShellViewToggles : INotifyPropertyChanged
{
    private TransferQueueModel? _queue;

    /// <summary>The Queued, Active and other tab the log was opened from, to go back to.</summary>
    private int _tabBeforeLog;

    internal ShellViewToggles() => Restore(DesktopUpdatePreferences.Defaults);

    /// <summary>Whether the transfer queue is shown under the workspaces.</summary>
    public bool QueueVisible
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            QueueCheck.IsChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QueueVisible)));
            FollowQueue();
        }
    }

    /// <summary>Whether panes list hidden files and folders.</summary>
    public bool ShowHiddenFiles
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            HiddenCheck.IsChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowHiddenFiles)));
        }
    }

    /// <summary>The Appearance setting: light, dark, or following the system.</summary>
    public DesktopAppearance Appearance { get; private set; } = DesktopAppearance.System;

    /// <summary>View > Directory Tree's tick, on while the active pane shows its tree.</summary>
    internal CommandCheck TreeCheck { get; } = new();

    internal CommandCheck QueueCheck { get; } = new();

    /// <summary>On while the queue shows and its Logs tab is the one chosen.</summary>
    internal CommandCheck LogCheck { get; } = new();

    internal CommandCheck HiddenCheck { get; } = new();

    /// <summary>Where each change is written. Null in a preview, which has no settings file.</summary>
    internal Action<Func<DesktopUpdatePreferences, DesktopUpdatePreferences>>? Persist { get; set; }

    /// <summary>
    /// Puts an appearance on screen. Null in a preview, whose colours the photographs depend on.
    /// </summary>
    internal Action<DesktopAppearance>? ApplyAppearance { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Takes what was saved, as the shell opens.</summary>
    internal void Restore(DesktopUpdatePreferences saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        QueueVisible = saved.TransferQueueVisible;
        QueueCheck.IsChecked = QueueVisible;
        ShowHiddenFiles = saved.ShowHiddenFiles;
        HiddenCheck.IsChecked = ShowHiddenFiles;
        FollowSettings(saved);
    }

    /// <summary>Takes the appearance Settings saved, which its Apply can have changed.</summary>
    internal void FollowSettings(DesktopUpdatePreferences saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        Appearance = Enum.IsDefined(saved.Appearance) ? saved.Appearance : DesktopAppearance.System;
    }

    /// <summary>The queue whose Logs tab View > Session Log opens, watched for the tick.</summary>
    internal void Watch(TransferQueueModel queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _queue = queue;
        queue.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TransferQueueModel.SelectedTab)) FollowQueue();
        };
        FollowQueue();
    }

    /// <summary>View > Transfer Queue: hides a showing queue and shows a hidden one.</summary>
    internal void ToggleQueue() => SetQueueVisible(!QueueVisible);

    internal void SetQueueVisible(bool visible)
    {
        if (QueueVisible == visible) return;
        QueueVisible = visible;
        Persist?.Invoke(current => current with { TransferQueueVisible = visible });
    }

    /// <summary>
    /// View > Session Log: the queue's Logs tab, which is where the session's activity is written.
    /// </summary>
    /// <remarks>
    /// Showing it shows the queue too, if it was hidden. Hiding it goes back to the tab it was
    /// opened from rather than hiding the queue, which View > Transfer Queue does.
    /// </remarks>
    internal void ToggleSessionLog()
    {
        if (_queue is not { } queue || LogTab(queue) is not { } log) return;

        if (QueueVisible && queue.SelectedTab == log)
        {
            queue.SelectedTab = _tabBeforeLog == log ? 0 : _tabBeforeLog;
            return;
        }

        _tabBeforeLog = queue.SelectedTab;
        SetQueueVisible(true);
        queue.SelectedTab = log;
    }

    /// <summary>View > Hidden Files: every pane lists hidden files, or none does.</summary>
    internal void ToggleHiddenFiles()
    {
        var show = !ShowHiddenFiles;
        ShowHiddenFiles = show;
        Persist?.Invoke(current => current with { ShowHiddenFiles = show });
    }

    /// <summary>
    /// View > Theme: light, then dark, then following the system, and round again.
    /// </summary>
    /// <remarks>
    /// The Appearance setting Settings shows, saved and put on screen at once as Settings does. A
    /// colour scheme chosen there is kept; this picks which half of it shows, which for a scheme
    /// with no light or dark twin is the scheme itself.
    /// </remarks>
    internal DesktopAppearance CycleAppearance()
    {
        var next = Appearance switch
        {
            DesktopAppearance.Light => DesktopAppearance.Dark,
            DesktopAppearance.Dark => DesktopAppearance.System,
            _ => DesktopAppearance.Light
        };
        Appearance = next;
        Persist?.Invoke(current => current with { Appearance = next });
        ApplyAppearance?.Invoke(next);
        return next;
    }

    /// <summary>The name Settings gives an appearance.</summary>
    internal static string NameOf(DesktopAppearance appearance) => appearance switch
    {
        DesktopAppearance.Light => Ui.Settings.ThemeLight,
        DesktopAppearance.Dark => Ui.Settings.ThemeDark,
        _ => Ui.Settings.ThemeSystem
    };

    private void FollowQueue() =>
        LogCheck.IsChecked = QueueVisible && _queue is { } queue && queue.SelectedTab == LogTab(queue);

    private static int? LogTab(TransferQueueModel queue)
    {
        for (var index = 0; index < queue.Tabs.Count; index++)
        {
            if (queue.Tabs[index].IsLog) return index;
        }

        return null;
    }
}

/// <summary>
/// Gives the View, Go and Connections commands 1.4 declared and never wired somewhere to go.
/// </summary>
/// <remarks>
/// <para>
/// One place for the twelve of them, so the shell's builder calls one method rather than growing
/// twelve more registrations. Each acts on the active pane, a shell toggle or the connections
/// panel through what those already do; nothing here is a second way of doing something.
/// </para>
/// <para>
/// The ones that need something to act on -- a pane, a listing, a saved connection -- say so
/// through <see cref="ShellCommandRouter.When"/>, so the menu entry, the toolbar button and the
/// shortcut dim together rather than doing nothing when pressed. Those conditions are asked again
/// whenever the active pane, or what it shows, changes.
/// </para>
/// </remarks>
internal static class ShellViewCommands
{
    internal static void Wire(ShellPreviewModel model, ShellViewToggles toggles)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(toggles);
        var router = model.Router;

        // View. Directory Tree is the active pane's, as the pane's own menu has it; the queue, its
        // log and hidden files are the shell's.
        Pane(UiCommandIds.ViewDirectoryTree, static pane => pane.IsListing, static pane => pane.ShowTree = !pane.ShowTree);
        router.Handle(UiCommandIds.ViewTransferQueue, toggles.ToggleQueue);
        if (model.Queue is { } queue)
        {
            toggles.Watch(queue);
            router.Handle(UiCommandIds.ViewSessionLog, toggles.ToggleSessionLog);
        }

        router.Handle(UiCommandIds.ViewHiddenFiles, () =>
        {
            toggles.ToggleHiddenFiles();
            model.Say(toggles.ShowHiddenFiles ? Ui.Shell.HiddenFilesShown : Ui.Shell.HiddenFilesNotShown);
        });
        router.Handle(UiCommandIds.ViewTheme, () =>
            model.Say(Ui.Format(Ui.Shell.ThemeNowFormat, ShellViewToggles.NameOf(toggles.CycleAppearance()))));

        // Go, on the active pane, as Back and Up are.
        Pane(UiCommandIds.GoHome, static pane => pane.CanGoHome, static pane => _ = pane.GoHomeAsync());
        Pane(UiCommandIds.GoHistory, static pane => pane.IsListing, static pane => pane.ShowHistory());
        Pane(UiCommandIds.GoFavorites, static _ => true, static pane => pane.ShowFavorites());

        // Connections. Quick Connect is the active pane's picker, searched from the keyboard;
        // Reconnect and Disconnect are what it has open.
        Pane(UiCommandIds.ConnectionsQuickConnect, static _ => true, static pane => pane.ShowConnectionPicker());
        Pane(UiCommandIds.ConnectionsReconnect, static pane => pane.HasSavedConnection, static pane => _ = pane.ReconnectAsync());
        Pane(UiCommandIds.ConnectionsDisconnect, static pane => pane.HasSavedConnection, pane =>
        {
            var name = pane.Connection?.Name;
            _ = pane.DisconnectAsync();
            if (!string.IsNullOrEmpty(name)) model.Say(Ui.Format(Ui.Shell.DisconnectedFromFormat, name));
        });
        router.Handle(UiCommandIds.ConnectionsTestConnection, () => _ = TestAsync(model));
        router.When(UiCommandIds.ConnectionsTestConnection, () => TestTarget(model) is not null);

        FollowThePanes(model, toggles);

        void Pane(string id, Func<BrowserPaneModel, bool> can, Action<BrowserPaneModel> run)
        {
            router.Handle(id, () =>
            {
                if (model.ActivePane() is { } pane && can(pane)) run(pane);
            });
            router.When(id, () => model.ActivePane() is { } pane && can(pane));
        }
    }

    /// <summary>
    /// The connection Test Connection would test: the one selected in the connections panel, as
    /// 1.4's description says, or else the one the active pane has open.
    /// </summary>
    internal static Guid? TestTarget(ShellPreviewModel model)
    {
        if (model.Sidebar?.Selected is { } row) return row.Id;
        return model.ActivePane() is { HasSavedConnection: true, Connection.Id: { } id } ? id : null;
    }

    /// <summary>
    /// Tests a connection through the panel, which records the outcome on its card, and says what
    /// it came to in the status bar, where it is seen whether the panel is showing or not.
    /// </summary>
    internal static async Task TestAsync(ShellPreviewModel model)
    {
        if (TestTarget(model) is not { } id || model.Sidebar is not { } sidebar) return;
        if (sidebar.Selected?.Id != id && !sidebar.SelectConnection(id)) return;

        var name = sidebar.Selected?.Name ?? sidebar.NameOf(id) ?? string.Empty;
        model.SayUntilResolved(Ui.Format(Ui.Shell.TestingConnectionFormat, name));
        await sidebar.TestSelectedAsync().ConfigureAwait(true);
        model.Resolve(Ui.Format(Ui.Shell.TestedConnectionFormat, name, sidebar.DetailStatus));
    }

    /// <summary>
    /// Asks the conditions again, and moves the ticks, whenever the active pane or what it shows
    /// changes, and gives every pane, new ones too, the hidden-files setting.
    /// </summary>
    private static void FollowThePanes(ShellPreviewModel model, ShellViewToggles toggles)
    {
        var watched = new HashSet<BrowserPaneModel>();

        void Refresh()
        {
            toggles.TreeCheck.IsChecked = model.ActivePane() is { IsListing: true, ShowTree: true };
            model.Router.Reconsider();
        }

        void WatchPane(BrowserPaneModel pane)
        {
            pane.ShowHiddenItems = toggles.ShowHiddenFiles;
            if (!watched.Add(pane)) return;
            pane.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(BrowserPaneModel.IsActive) or nameof(BrowserPaneModel.Connection)
                    or nameof(BrowserPaneModel.Path) or nameof(BrowserPaneModel.ContentKind)
                    or nameof(BrowserPaneModel.IsBusy) or nameof(BrowserPaneModel.ShowTree))
                {
                    Refresh();
                }
            };
        }

        void WatchWorkspace(WorkspaceModel workspace)
        {
            foreach (var pane in workspace.Panes) WatchPane(pane);
            workspace.Panes.CollectionChanged += (_, e) =>
            {
                foreach (var added in e.NewItems?.OfType<BrowserPaneModel>() ?? []) WatchPane(added);
                Refresh();
            };
        }

        foreach (var tab in model.Workspaces)
        {
            if (tab.Workspace is { } workspace) WatchWorkspace(workspace);
        }

        model.Workspaces.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (var added in e.NewItems?.OfType<WorkspaceTab>() ?? [])
                {
                    if (added.Workspace is { } workspace) WatchWorkspace(workspace);
                }
            }

            Refresh();
        };
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellPreviewModel.SelectedWorkspace)) Refresh();
        };
        if (model.Sidebar is { } sidebar)
        {
            sidebar.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ConnectionsSidebar.Selected)) Refresh();
            };
        }
        toggles.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ShellViewToggles.ShowHiddenFiles)) return;
            foreach (var pane in watched) pane.ShowHiddenItems = toggles.ShowHiddenFiles;
        };

        Refresh();
    }
}
