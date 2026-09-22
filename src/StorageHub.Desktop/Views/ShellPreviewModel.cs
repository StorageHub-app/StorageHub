using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Threading;
using Lucide.Avalonia;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Configuration;
using StorageHub.Desktop.Framework;

namespace StorageHub.Desktop.Views;

/// <summary>One command, as the menu and the toolbar need it.</summary>
/// <remarks>
/// Flattened from <see cref="UiCommandDefinition"/> at build time rather than bound through to it,
/// because the view wants an icon kind and the catalog deliberately knows only about a
/// <see cref="UiGlyph"/> - which icon set draws that glyph is <see cref="IconCatalog"/>'s business.
/// </remarks>
internal sealed record CommandEntry(
    string Id,
    string Label,
    string Description,
    KeyGesture? Shortcut,
    LucideIconKind? Icon,
    UiIconTone Tone,
    ICommand Command)
{
    internal bool IsPrimary => Tone == UiIconTone.Primary;

    internal bool IsDanger => Tone == UiIconTone.Danger;
}

internal sealed record MenuSection(string Header, IReadOnlyList<CommandEntry> Items);

/// <summary>A toolbar divider. Its own type so the toolbar can template it separately.</summary>
internal sealed record ToolbarSeparator
{
    internal static ToolbarSeparator Instance { get; } = new();
}

/// <summary>
/// One workspace tab: either a page, or the two panes a workspace is.
/// </summary>
/// <remarks>
/// The panes are real browsers now, each with its own connection to the agent. They were two lists
/// of invented rows and a pair of titles, which was enough to photograph the shell and nothing
/// else.
/// </remarks>
internal sealed record WorkspaceTab(
    string Title,
    LucideIconKind Icon,
    object? Page,
    WorkspaceModel? Workspace = null);

internal sealed record QueueTab(string Title, LucideIconKind Icon);

/// <summary>
/// The shell's shape. The menus and toolbar are real; the content they act on is not yet.
/// </summary>
/// <remarks>
/// Every menu entry, shortcut, tooltip and icon below comes from UiCommandCatalog and ToolbarLayout,
/// so this is the shipping command set in the shipping order, in the current language - not a
/// hand-written imitation that would drift the moment a command was added. What is still stand-in is
/// the data: connections, pane rows and queue tabs. Those arrive per screen as each is ported, and
/// the commands are not wired to anything yet - a Command per entry is the next step.
/// </remarks>
internal sealed class ShellPreviewModel : INotifyPropertyChanged
{
    private string _status = string.Empty;
    private ShellStatusSnapshot _shellStatus = ShellStatusSnapshot.Initial;

    internal ShellPreviewModel(ShellCommandRouter router)
    {
        Router = router;
        // A command with nowhere to go is reachable by shortcut, which does not consult
        // CanExecute the way a menu does. Saying so beats appearing to ignore the keystroke.
        Router.Unhandled += (_, id) => Status = Ui.Format(Ui.Shell.CommandNotBuiltFormat, id);
    }

    /// <summary>Dispatch for every command, whether it arrives by menu, toolbar or keystroke.</summary>
    public ShellCommandRouter Router { get; }

    /// <summary>
    /// Points the listing commands at whichever pane is active.
    /// </summary>
    /// <remarks>
    /// These have menu entries and shortcuts already; what they lacked was somewhere to go. They
    /// resolve the pane when they run rather than being bound to one, because "the active pane" is
    /// a question with a different answer after every click -- and with four panes, binding them to
    /// a pane at startup would mean three panes whose menu entries did nothing.
    /// </remarks>
    internal void RouteToActivePane()
    {
        // What a shortcut needs to know about the pane it would act on. A pane showing a shell
        // refuses pane commands outright, because the keystroke belongs to the remote host.
        Router.ActivePane = () => ActivePane() is { } pane
            ? (HasPane: true, IsSshPane: pane.IsTerminal)
            : (HasPane: false, IsSshPane: false);

        // Selection, which the pane answers directly.
        Router.Handle(UiCommandIds.EditSelectAll, () => ActivePane()?.SelectAll());
        Router.Handle(UiCommandIds.EditInvertSelection, () => ActivePane()?.InvertSelection());

        // Everything else the pane already draws a button for. Routed through the command rather
        // than the method so the menu entry and the button decline in the same cases -- a rename
        // with two rows selected, a paste into a terminal -- instead of the menu finding out by
        // failing.
        Pane(UiCommandIds.ViewRefresh, static pane => pane.RefreshCommand);
        Pane(UiCommandIds.EditNewFolder, static pane => pane.NewFolderCommand);
        Pane(UiCommandIds.EditNewEmptyFile, static pane => pane.NewFileCommand);
        Pane(UiCommandIds.EditRename, static pane => pane.RenameCommand);
        Pane(UiCommandIds.EditDelete, static pane => pane.DeleteCommand);
        Pane(UiCommandIds.EditProperties, static pane => pane.PropertiesCommand);
        Pane(UiCommandIds.GoBack, static pane => pane.BackCommand);
        Pane(UiCommandIds.GoForward, static pane => pane.ForwardCommand);
        Pane(UiCommandIds.GoUp, static pane => pane.UpCommand);

        // Cut, copy and paste belong to the workspace: each names two panes, or would if "the
        // other pane" still meant anything at four. They stage and paste, exactly as the panes'
        // own buttons do.
        Workspace(UiCommandIds.EditCopy, static workspace => workspace.StageCopyCommand);
        Workspace(UiCommandIds.EditCut, static workspace => workspace.StageMoveCommand);
        Workspace(UiCommandIds.EditPaste, static workspace => workspace.PasteCommand);

        // Moving between panes is the workspace's too: it owns which one is active.
        Router.Handle(UiCommandIds.GoNextPane, FocusNextPane);

        void Pane(string id, Func<BrowserPaneModel, ICommand?> choose) =>
            Router.Handle(id, () => Run(ActivePane() is { } pane ? choose(pane) : null));

        void Workspace(string id, Func<WorkspaceModel, ICommand?> choose) =>
            Router.Handle(id, () => Run(ActiveWorkspace() is { } workspace ? choose(workspace) : null));

        static void Run(ICommand? command)
        {
            if (command?.CanExecute(null) == true) command.Execute(null);
        }
    }

    /// <summary>
    /// Makes the next pane in the arrangement the active one, wrapping at the end.
    /// </summary>
    /// <remarks>
    /// The order is the layout's, so tabbing through panes follows the order they are read in
    /// rather than the order they happened to be created.
    /// </remarks>
    private void FocusNextPane()
    {
        if (ActiveWorkspace() is not { Panes.Count: > 1 } workspace) return;

        var current = workspace.Panes.IndexOf(workspace.Active);
        workspace.Panes[(current + 1) % workspace.Panes.Count].IsActive = true;
    }

    /// <summary>
    /// Keeps the status bar reporting the pane and the queue, rather than its startup values.
    /// </summary>
    /// <remarks>
    /// Three of its five cells were frozen: only the agent state and the active count were ever
    /// written, so the location said "No connection" and the selection said "No selection" for the
    /// life of the process however much was open or picked.
    /// </remarks>
    internal void WatchTheStatusBar()
    {
        Queue.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TransferQueueModel.ActiveCount)
                or nameof(TransferQueueModel.QueuedCount))
            {
                ShellStatus = ShellStatus with
                {
                    ActiveJobs = Queue.ActiveCount,
                    QueuedJobs = Queue.QueuedCount
                };
            }
        };

        foreach (var tab in Workspaces)
        {
            if (tab.Workspace is { } workspace) Watch(workspace);
        }

        Workspaces.CollectionChanged += (_, e) =>
        {
            foreach (var added in e.NewItems?.OfType<WorkspaceTab>() ?? [])
            {
                if (added.Workspace is { } workspace) Watch(workspace);
            }
        };

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedWorkspace)) ReportThePane();
        };

        ReportThePane();

        void Watch(WorkspaceModel workspace)
        {
            foreach (var pane in workspace.Panes) WatchPane(pane);
            workspace.Panes.CollectionChanged += (_, e) =>
            {
                foreach (var added in e.NewItems?.OfType<BrowserPaneModel>() ?? []) WatchPane(added);
                ReportThePane();
            };
        }

        void WatchPane(BrowserPaneModel pane)
        {
            pane.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(BrowserPaneModel.Path)
                    or nameof(BrowserPaneModel.IsActive)
                    or nameof(BrowserPaneModel.SelectionSummary))
                {
                    ReportThePane();
                }
            };

            pane.SelectedRows.CollectionChanged += (_, _) => ReportThePane();
        }
    }

    /// <summary>
    /// What the active pane is showing and what is picked in it.
    /// </summary>
    /// <remarks>
    /// The byte total counts files only. A folder contributes nothing because nobody has counted
    /// what is inside it, which is what the pane's own summary does and what 1.x did.
    /// </remarks>
    private void ReportThePane()
    {
        if (ActivePane() is not { } pane)
        {
            ShellStatus = ShellStatus with
            {
                Location = Ui.Shell.StatusNoConnection,
                SelectedItems = 0,
                SelectedBytes = 0
            };
            return;
        }

        var chosen = pane.SelectedRows.Where(static row => !row.IsParentNavigation).ToArray();
        ShellStatus = ShellStatus with
        {
            Location = string.IsNullOrWhiteSpace(pane.Path) ? Ui.Shell.StatusNoConnection : pane.Path,
            SelectedItems = chosen.Length,
            SelectedBytes = chosen.Sum(static row => row.Length ?? 0)
        };
    }

    /// <summary>The workspace on screen, or none when the tab is a page.</summary>
    private WorkspaceModel? ActiveWorkspace() =>
        Workspaces.ElementAtOrDefault(SelectedWorkspace)?.Workspace;

    /// <summary>The active pane of the workspace on screen, or none when a page is showing.</summary>
    /// <summary>
    /// Reloads the active pane after an edited file was uploaded, and says so there.
    /// </summary>
    /// <remarks>
    /// The active pane, as 1.x did: it is almost always the one the file was opened from, and a
    /// reload of the wrong one costs a listing, not a mistake. The sentence goes on after the
    /// reload, which would otherwise clear it the moment the listing arrived.
    /// </remarks>
    internal async Task EditedFileUploadedAsync()
    {
        if (ActivePane() is not { } pane) return;
        await pane.RefreshAsync().ConfigureAwait(true);
        pane.Status = Ui.Shell.StatusEditedFileUploaded;
    }

    internal BrowserPaneModel? ActivePane() =>
        ActiveWorkspace() is { Panes.Count: > 0 } workspace ? workspace.Active : null;

    /// <summary>
    /// The id of the last command invoked.
    /// </summary>
    /// <remarks>
    /// Stand-in feedback, and deliberately visible: until the handlers move out of MainForm there is
    /// nothing for a command to do, and a menu that silently does nothing is indistinguishable from
    /// one that is not wired at all. The status bar showing the id proves the whole path - menu or
    /// toolbar or shortcut, through the focus rules, to a command.
    /// </remarks>
    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<MenuSection> Menus { get; init; } = [];

    public IReadOnlyList<object> Toolbar { get; init; } = [];

    /// <summary>
    /// The tabs across the top, which New Workspace adds to.
    /// </summary>
    /// <remarks>
    /// Observable rather than a fixed list, because a workspace is something somebody makes. The
    /// tab strip binds to it directly, so adding one here is the whole of adding one.
    /// </remarks>
    public ObservableCollection<WorkspaceTab> Workspaces { get; } = [];

    /// <summary>
    /// How a new workspace tab is built, given the arrangement that was chosen for it.
    /// </summary>
    /// <remarks>
    /// Held rather than called once, because every workspace needs its own agent clients and this
    /// is the only place that knows how to make them. Null in a preview that has no agent behind
    /// it, in which case the "+" has nothing to add and says so by being unavailable.
    /// </remarks>
    internal Func<WorkspacePreset, WorkspaceTab>? WorkspaceFactory { get; init; }

    /// <summary>
    /// Adds a workspace with the arrangement chosen, and shows it.
    /// </summary>
    /// <remarks>
    /// The new tab is selected, because somebody who just chose an arrangement wants to see it --
    /// and because a new tab that appeared behind the current one would look like nothing
    /// happened.
    /// </remarks>
    internal void AddWorkspace(WorkspacePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (WorkspaceFactory is not { } factory) return;

        Workspaces.Add(factory(preset));
        SelectedWorkspace = Workspaces.Count - 1;
    }

    public ConnectionsSidebar Sidebar { get; init; } = null!;

    public ICommand NewWorkspaceCommand { get; init; } = null!;

    public string NewWorkspaceLabel { get; init; } = string.Empty;

    /// <summary>The transfer queue, reading the agent rather than a stand-in.</summary>
    public TransferQueueModel Queue { get; init; } = null!;

    /// <summary>
    /// The Sync tasks tab and its two sub-tabs.
    /// </summary>
    /// <remarks>
    /// Held so the shell can send somebody to the right one: previewing a profile in the editor
    /// produces a run, and the run belongs on the review sub-tab rather than left to be found.
    /// </remarks>
    internal TabbedPageModel? SyncPage { get; set; }

    internal SyncTasksModel? SyncTasks =>
        SyncPage?.Tabs.Select(static tab => tab.Content).OfType<SyncTasksModel>().FirstOrDefault();

    internal SyncRunHistoryModel? SyncRunHistory =>
        SyncPage?.Tabs.Select(static tab => tab.Content).OfType<SyncRunHistoryModel>().FirstOrDefault();

    /// <summary>Shows a run on the review sub-tab, whichever tab is currently open.</summary>
    internal void ReviewRun(Guid syncRunId)
    {
        if (SyncPage is not { } page || SyncRunHistory is not { } history) return;

        SelectedWorkspace = Workspaces
            .Select(static (tab, index) => (tab, index))
            .FirstOrDefault(entry => ReferenceEquals(entry.tab.Page, page)).index;
        page.SelectedIndex = 1;
        _ = history.LoadRunAsync(syncRunId);
    }

    /// <summary>Which tab is showing. Settable, because adding a workspace moves to it.</summary>
    public int SelectedWorkspace
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedWorkspace)));
        }
    }

    /// <summary>
    /// The status bar, as the shell has always modelled it.
    /// </summary>
    /// <remarks>
    /// ShellStatusSnapshot already turns a state into the four strings the bar shows, in the current
    /// language, and it moved to Desktop.Core with the rest of the presentation models. Binding to it
    /// rather than to four strings of this view's own is what keeps the Avalonia bar saying exactly
    /// what the WinForms one says.
    /// </remarks>
    public ShellStatusSnapshot ShellStatus
    {
        get => _shellStatus;
        private set
        {
            if (_shellStatus == value) return;
            _shellStatus = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShellStatus)));
        }
    }

    /// <summary>
    /// The last thing the agent said about itself, whole, or null before it has said anything.
    /// </summary>
    /// <remarks>
    /// Read by the agent control window rather than pushed to it, because that window is opened on
    /// demand and may never be.
    /// </remarks>
    public AgentMonitorStatus? AgentStatus { get; private set; }

    /// <summary>
    /// Watches a real agent and reports its state in the status bar.
    /// </summary>
    /// <remarks>
    /// Opt-in rather than started in the constructor, so the headless tests measure a shell that is
    /// not polling a socket. The monitor raises on its own thread, and Avalonia requires the
    /// property change on the UI one.
    /// </remarks>
    public void Watch(AgentStatusMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        monitor.StatusChanged += (_, e) => Dispatcher.UIThread.Post(() =>
        {
            // Kept whole as well as summarised. The status bar needs two of these five fields; the
            // agent control window needs all of them, including the detail that says *why* the
            // agent is in recovery, which is the one thing a single word cannot carry.
            AgentStatus = e.Status;
            ShellStatus = ShellStatus with
            {
                AgentState = e.Status.State,
                ActiveJobs = e.Status.ActiveTransfers,
            };
        });

        monitor.Start();
    }
}

/// <summary>Builds the shell's model: real commands, stand-in content.</summary>
internal static class ShellPreview
{
    internal static ShellPreviewModel Sample { get; } = Build();

    /// <summary>The same shell, opened on Sync tasks, so that screen can be photographed too.</summary>
    internal static ShellPreviewModel SampleOnSyncTasks { get; } = Build(selectedWorkspace: 1);

    /// <summary>And on the workspace, which is where the browser panes are.</summary>
    internal static ShellPreviewModel SampleOnWorkspace { get; } = Build(selectedWorkspace: 2);

    /// <summary>
    /// A shell of its own, opened on the workspace.
    /// </summary>
    /// <remarks>
    /// The three properties above are cached because the application has one shell. A test that
    /// changes what it holds -- the arrangement, the active pane -- has to have its own, or it
    /// leaves that change behind for whichever test runs next. That is not hypothetical: a suite
    /// reordered by adding a file elsewhere started failing on a workspace another test had
    /// already reduced to a single pane.
    /// </remarks>
    /// <param name="terminals">
    /// How its panes open a shell. The default reaches the agent over a pipe, which a test without
    /// one would spend both connect timeouts discovering.
    /// </param>
    internal static ShellPreviewModel CreateOnWorkspace(
        Func<ISshTerminalAgentClient>? terminals = null) =>
        Build(selectedWorkspace: 2, terminals);

    private static ShellPreviewModel Build(
        int selectedWorkspace = 0, Func<ISshTerminalAgentClient>? terminals = null)
    {
        terminals ??= static () => new NamedPipeSshTerminalAgentClient();
        var router = new ShellCommandRouter();

        // The queue is built first so the workspace can tell it to refresh the moment a transfer
        // is accepted, rather than leaving somebody to watch a tab count that updates on its own
        // schedule two seconds later.
        var queue = new TransferQueueModel(
            static () => new NamedPipeTransferQueueAgentClient(),
            // The Logs tab. Its clients are made per read, like the queue's, and it reads nothing
            // until its tab is opened.
            new ActivityLogModel(new ActivityLogReader(
                static () => new NamedPipeTransferQueueAgentClient(),
                static () => new NamedPipeSyncManagementAgentClient()).ReadAsync));
        var workspaces = 0;
        var model = new ShellPreviewModel(router)
        {
            Menus = BuildMenus(router),
            Toolbar = BuildToolbar(router),
            SelectedWorkspace = selectedWorkspace,
            Sidebar = BuildSidebar(router),
            NewWorkspaceCommand = router.For(UiCommandIds.WorkspaceNewWorkspace),
            NewWorkspaceLabel = Ui.Commands.WorkspaceNewWorkspace,
            Queue = queue,

            // Panes on their own connection to the agent, one each. A client per pane rather than
            // one shared: the browser controller holds a listing position, and two panes sharing
            // one would have the second navigation cancel the first. A factory rather than a fixed
            // set, because a workspace can hold one to four and somebody can make another.
            WorkspaceFactory = preset => new WorkspaceTab(
                Ui.Format(Ui.Shell.WorkspaceTabFormat, ++workspaces),
                LucideIconKind.Folder,
                null,
                new WorkspaceModel(
                    // A pane makes its own inspector client per operation, for the same reason a
                    // transfer does: one held open is one that broke when the agent restarted.
                    () => new BrowserPaneModel(
                        mutations: static () => new PaneMutationController(
                            static () => new NamedPipeObjectInspectorAgentClient()),
                        dialogs: Services.ShellServices.Dialogs,

                        // One terminal client per session, not per pane: the protocol keeps two
                        // pipe connections open for as long as a shell is running, and the pane
                        // disposes them with the session.
                        terminals: terminals,

                        // What lets a terminal restart an agent left running from an older build,
                        // once, instead of telling somebody to restart StorageHub themselves.
                        agentLifecycle: AgentLifecycleControllers.ForThisMachine,
                        inspect: Services.ShellServices.InspectObjectAsync,
                        edit: Services.ShellServices.EditExternallyAsync),
                    static () => new NamedPipeTransferQueueAgentClient(),
                    static () => new NamedPipeRemoteStorageAgentClient(),
                    static () => new NamedPipeObjectInspectorAgentClient(),
                    () => queue.RefreshAsync(),
                    preset,
                    Services.ShellServices.Dialogs)),
        };

        model.Workspaces.Add(new WorkspaceTab(
            Ui.Shell.TabWelcome,
            LucideIconKind.House,
            OverviewModel.Create(ShellStatusSnapshot.Initial)));
        var syncPage = new TabbedPageModel(
            [
                // The tasks screen asks the agent for the saved profiles and the runs behind them.
                // A client per load rather than one held open, for the reason the panes and the
                // queue already hold: a connection kept across an agent restart is one that has to
                // be found broken before it can be replaced.
                new PageTab(
                    Ui.Sync.TasksTitle,
                    SyncTasksModel.Create(static () => new NamedPipeSyncManagementAgentClient())),
                // And the review screen reads one run at a time, by id. It takes the dialog service
                // because Approve & dispatch is the only button in the shell that authorises the
                // agent to delete files without naming them, and it confirms before it does.
                new PageTab(
                    Ui.Sync.RunHistoryAndReview,
                    SyncRunHistoryModel.Create(
                        static () => new NamedPipeSyncManagementAgentClient(),
                        Services.ShellServices.Dialogs)),
            ]);
        model.SyncPage = syncPage;
        model.Workspaces.Add(new WorkspaceTab(
            Ui.Shell.TabSyncTasks, LucideIconKind.ArrowLeftRight, syncPage));

        // The tasks screen's own Run history button goes to the sub-tab beside it. It knows the
        // page it is on no more than the panel knows what a pane is, so the shell says what it does.
        if (model.SyncTasks is { } tasks)
        {
            tasks.RunHistoryCommand = new RelayCommand(_ => syncPage.SelectedIndex = 1);
        }

        model.AddWorkspace(WorkspacePreset.All[1]);
        model.SelectedWorkspace = selectedWorkspace;
        model.RouteToActivePane();
        model.WatchTheStatusBar();

        // The panel knows nothing about panes, so the shell hands it the one thing it needs to
        // act on a row: what to do with a connection id.
        model.Sidebar.OpenConnection = id =>
        {
            if (model.ActivePane() is { } pane) _ = pane.OpenConnectionAsync(id);
        };
        return model;
    }


    /// <summary>
    /// The sidebar, asking a real agent.
    /// </summary>
    /// <remarks>
    /// The client factory is passed rather than a client: each call opens a connection and closes
    /// it, which is what every other desktop agent client already does. Holding one open would mean
    /// holding one that broke when the agent restarted.
    /// </remarks>
    private static ConnectionsSidebar BuildSidebar(ShellCommandRouter router) => new(
        router.For(UiCommandIds.ConnectionsNewConnection),
        static () => new NamedPipeRemoteStorageAgentClient(),
        Services.ShellServices.Dialogs,
        LoadGroups,
        SaveGroups);

    /// <summary>
    /// The connections panel's saved arrangement.
    /// </summary>
    /// <remarks>
    /// Read and written through the settings file rather than held anywhere, because the panel is
    /// the only thing that arranges it and a shell that did not close cleanly should still reopen
    /// the way it was left. A settings file that cannot be read means an unarranged panel, which
    /// groups by folder path and is the state a new installation is in anyway.
    /// </remarks>
    private static IReadOnlyList<ConnectionGroupEntry>? LoadGroups()
    {
        try
        {
            var store = new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot);
            store.Preflight();
            return store.Load().ConnectionGroups;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void SaveGroups(IReadOnlyList<ConnectionGroupEntry> groups)
    {
        try
        {
            var store = new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot);
            store.Preflight();
            store.Save(store.Load() with { ConnectionGroups = groups });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The panel is arranged either way; only remembering it is lost.
        }
    }

    private static IReadOnlyList<MenuSection> BuildMenus(ShellCommandRouter router) =>
    [
        .. UiCommandCatalog.Menus
            .Select(menu => new MenuSection(
                UiCommandCatalog.MenuTitle(menu),
                [
                    .. UiCommandCatalog.ForMenu(menu)
                        .Where(definition => UiCommandCatalog.IsAvailable(definition.Id))
                        .Select(definition => ToEntry(definition, router)),
                ]))
            .Where(section => section.Items.Count > 0),
    ];

    /// <summary>
    /// The toolbar, filtered the way the menu already was.
    /// </summary>
    /// <remarks>
    /// The menu has always dropped what <see cref="UiCommandCatalog.IsAvailable"/> refuses and the
    /// toolbar never did, so it drew buttons for commands 1.x itself never wired -- Search and
    /// Compare panes among them. Same rule, both places.
    /// </remarks>
    private static IReadOnlyList<object> BuildToolbar(ShellCommandRouter router) =>
    [
        .. ToolbarLayout.Resolve(null)
            .Where(id => id == ToolbarLayout.Separator || UiCommandCatalog.IsAvailable(id))
            .Select(object (id) => id == ToolbarLayout.Separator
                ? ToolbarSeparator.Instance
                : ToEntry(UiCommandCatalog.GetDefinition(id), router)),
    ];

    private static CommandEntry ToEntry(UiCommandDefinition definition, ShellCommandRouter router) => new(
        definition.Id,
        definition.Label,
        definition.Description,
        definition.Shortcut,
        IconCatalog.Resolve(definition.Glyph),
        definition.Tone,
        router.For(definition.Id));
}
