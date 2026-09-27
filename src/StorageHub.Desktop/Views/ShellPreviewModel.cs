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
/// One workspace tab: either a page, or the panes a workspace is.
/// </summary>
/// <remarks>
/// The panes are real browsers now, each with its own connection to the agent. They were two lists
/// of invented rows and a pair of titles, which was enough to photograph the shell and nothing
/// else.
/// </remarks>
internal sealed class WorkspaceTab : INotifyPropertyChanged
{
    private readonly string _title = string.Empty;

    /// <summary>A page of the shell: Welcome, Sync tasks.</summary>
    internal WorkspaceTab(string title, LucideIconKind icon, object page)
    {
        _title = title;
        Icon = icon;
        Page = page;
    }

    /// <summary>A workspace, whose tab is named after it and follows it when it is renamed or saved.</summary>
    internal WorkspaceTab(WorkspaceModel workspace)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Icon = LucideIconKind.Folder;
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(WorkspaceModel.Name)
                or nameof(WorkspaceModel.IsDirty)
                or nameof(WorkspaceModel.FilePath))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolTip)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
            }
        };
    }

    /// <summary>The name, and " *" while a workspace differs from its file, as 1.x's tab said.</summary>
    public string Title => Workspace is { } workspace
        ? workspace.Name + (workspace.IsDirty ? " *" : string.Empty)
        : _title;

    /// <summary>Where a workspace is saved, or its name until it is; nothing for a page.</summary>
    public string? ToolTip => Workspace is { } workspace ? workspace.FilePath ?? workspace.Name : null;

    /// <summary>"Render farm workspace" for a screen reader, set with the title as 1.x's tab was.</summary>
    public string AccessibleName => Workspace is { } workspace
        ? Ui.Format(Ui.Shell.WorkspaceAccessibleNameFormat, workspace.Name)
        : _title;

    public LucideIconKind Icon { get; }

    public object? Page { get; }

    public WorkspaceModel? Workspace { get; }

    /// <summary>Whether the tab has a close button: a workspace does, a page of the shell does not.</summary>
    public bool IsClosable => Workspace is not null;

    public event PropertyChangedEventHandler? PropertyChanged;
}

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

        // Ctrl+L, as 1.x and every browser have it: the cursor in the address, all of it selected.
        Router.Handle(UiCommandIds.GoFocusAddress, () => ActivePane()?.FocusAddress());

        // Everything else the pane already draws a button for. Routed through the command rather
        // than the method so the menu entry and the button decline in the same cases -- a rename
        // with two rows selected, a paste into a terminal -- instead of the menu finding out by
        // failing.
        Pane(UiCommandIds.ViewRefresh, static pane => pane.RefreshCommand);
        Pane(UiCommandIds.EditNewFolder, static pane => pane.NewFolderCommand);
        Pane(UiCommandIds.EditNewEmptyFile, static pane => pane.NewFileCommand);
        Pane(UiCommandIds.EditRename, static pane => pane.RenameCommand);
        Pane(UiCommandIds.EditBatchRename, static pane => pane.BatchRenameCommand);
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
                or nameof(TransferQueueModel.QueuedCount)
                or nameof(TransferQueueModel.BytesPerSecond))
            {
                ShellStatus = ShellStatus with
                {
                    // Synchronizations are counted as the monitor counts them, so the two
                    // writers agree rather than the count flickering between them.
                    ActiveJobs = Queue.ActiveCount + (AgentStatus?.ActiveSyncRuns ?? 0),
                    QueuedJobs = Queue.QueuedCount,
                    TransferBytesPerSecond = Queue.BytesPerSecond
                };
                FollowTransfers();
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

                if (e.PropertyName == nameof(BrowserPaneModel.Connection) && pane.Connection?.Id is { } opened)
                {
                    Overview?.RecordRecent(opened);
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
    /// How a new workspace is built, given the arrangement that was chosen for it and its name.
    /// </summary>
    /// <remarks>
    /// Held rather than called once, because every workspace needs its own agent clients and this
    /// is the only place that knows how to make them. Null in a preview that has no agent behind
    /// it, in which case the "+" has nothing to add and says so by being unavailable.
    /// </remarks>
    internal Func<WorkspacePreset, string, WorkspaceModel>? WorkspaceFactory { get; init; }

    /// <summary>
    /// Saving, opening and renaming workspace files. Null in a preview, which has nowhere to save
    /// and nobody to ask, so closing a changed workspace there does not stop to ask either.
    /// </summary>
    internal WorkspaceFiles? Files { get; set; }

    private int _nextWorkspaceNumber = 1;

    /// <summary>
    /// Adds a workspace with the arrangement chosen, and shows it.
    /// </summary>
    /// <remarks>
    /// The new tab is selected, because somebody who just chose an arrangement wants to see it --
    /// and because a new tab that appeared behind the current one would look like nothing
    /// happened.
    /// </remarks>
    /// <param name="name">The name to give it; null numbers it, as "+" does.</param>
    internal WorkspaceTab? AddWorkspace(WorkspacePreset preset, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (WorkspaceFactory is not { } factory) return null;

        var tab = new WorkspaceTab(factory(preset, name ?? NextWorkspaceName()));
        Workspaces.Add(tab);
        SelectedWorkspace = Workspaces.Count - 1;
        return tab;
    }

    /// <summary>
    /// "Workspace 3", skipping a number an open workspace is already called, as 1.x did: one
    /// opened from a file named "Workspace 2" would otherwise sit beside a new one of that name.
    /// </summary>
    private string NextWorkspaceName()
    {
        string name;
        do
        {
            name = Ui.Format(Ui.Shell.WorkspaceTabFormat, _nextWorkspaceNumber++);
        }
        while (Workspaces.Any(tab => string.Equals(tab.Workspace?.Name, name, StringComparison.OrdinalIgnoreCase)));

        return name;
    }

    public ConnectionsSidebar Sidebar { get; init; } = null!;

    /// <summary>What a pane's right-click menu offers after its own Open and Edit.</summary>
    internal IReadOnlyList<object> PaneContextEntries { get; init; } = [];

    /// <summary>
    /// Brings a stopped agent back, the same way startup does. Set by the application; null in a
    /// preview, which has nothing to start.
    /// </summary>
    internal Func<CancellationToken, Task<AgentEnsureResult>>? RecoverAgent { get; set; }

    /// <summary>How long after a failed attempt the next one waits, so an absent agent is not
    /// relaunched on every eight-second poll.</summary>
    private static readonly TimeSpan RecoveryBackoff = TimeSpan.FromSeconds(30);

    private bool _recovering;
    private DateTimeOffset _nextRecovery = DateTimeOffset.MinValue;

    /// <summary>
    /// Restarts the agent when it drops, as 1.x did, and reloads everything once it answers.
    /// </summary>
    private async Task RecoverAgentAsync()
    {
        if (RecoverAgent is not { } recover || _recovering || DateTimeOffset.UtcNow < _nextRecovery) return;

        _recovering = true;
        ShellStatus = ShellStatus with { AgentState = AgentConnectionState.Starting };
        try
        {
            var result = await recover(CancellationToken.None).ConfigureAwait(true);
            if (result.IsReady)
            {
                ShellStatus = ShellStatus with { AgentState = AgentConnectionState.Connected };
                ReloadEverything();
            }
            else
            {
                _nextRecovery = DateTimeOffset.UtcNow + RecoveryBackoff;
                ShellStatus = ShellStatus with { AgentState = AgentConnectionState.Disconnected };
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _nextRecovery = DateTimeOffset.UtcNow + RecoveryBackoff;
        }
        finally
        {
            _recovering = false;
        }
    }

    /// <summary>
    /// How this machine restarts its agent, asked each time it is needed. Set by the application;
    /// null in a preview, and a null answer is a machine where a restart would stop the agent with
    /// nothing to start in its place, such as a build run from source with its agent started by hand.
    /// </summary>
    internal Func<IAgentLifecycleController?>? AgentLifecycle { get; set; }

    /// <summary>Whether a restart for saved settings is waiting for the running work to finish.</summary>
    private bool _agentRestartPending;

    /// <summary>
    /// Whether the agent is being restarted for saved settings right now. It is down on purpose
    /// meanwhile, so recovery leaves it to the restart rather than launching a second one beside it.
    /// </summary>
    private bool _restartingAgent;

    /// <summary>Whether the shell is closing, after which the agent is not restarted or recovered.</summary>
    private bool _closing;

    /// <summary>
    /// The restart for saved settings under way, or a finished task. The shell's close waits for it,
    /// so the process neither exits nor stops the agent halfway through one.
    /// </summary>
    internal Task AgentSettingsRestart { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Transfers and synchronizations running. The queue and the monitor each write the status
    /// bar's count at their own moments, so the monitor's own figure is read beside it and the
    /// larger taken: a synchronization with no transfer yet is still work to wait for.
    /// </summary>
    private int RunningWork => Math.Max(
        ShellStatus.ActiveJobs,
        AgentStatus is { } status ? status.ActiveTransfers + status.ActiveSyncRuns : 0);

    /// <summary>
    /// Gets the agent to read saved concurrency and speed limits, which it reads only as it starts.
    /// </summary>
    /// <remarks>
    /// As 1.4 did it: nothing running, and the agent restarts now; transfers or synchronizations
    /// running, and it waits until the agent reports none, rather than cutting them off to apply a
    /// setting. Either way the status bar says which, since a saved number that has not taken
    /// effect yet looks exactly like one that has.
    /// </remarks>
    internal Task ApplyAgentSettingsAsync()
    {
        if (_closing) return Task.CompletedTask;

        // A restart already under way may have read the settings before this save, so this one
        // follows it rather than overlapping it.
        if (_restartingAgent)
        {
            _agentRestartPending = true;
            return AgentSettingsRestart;
        }

        if (AgentLifecycle?.Invoke() is not { } agent)
        {
            Say(Ui.Shell.StatusConcurrencyRestartRequired);
            return Task.CompletedTask;
        }

        if (RunningWork > 0)
        {
            _agentRestartPending = true;
            Say(Ui.Shell.StatusConcurrencyPendingIdle);
            return Task.CompletedTask;
        }

        return AgentSettingsRestart = RestartAgentForSettingsAsync(agent);
    }

    /// <summary>
    /// Stops applying saved settings to the agent, as the shell closes: nothing pending is started,
    /// and a status that arrives late does not start one. <see cref="AgentSettingsRestart"/> is
    /// what to wait for if one is already running.
    /// </summary>
    internal void StopRestartingAgent()
    {
        _closing = true;
        _agentRestartPending = false;
    }

    /// <param name="agent">The controller already asked for, or null to ask again.</param>
    private async Task RestartAgentForSettingsAsync(IAgentLifecycleController? agent = null)
    {
        if (_closing) return;

        // An agent being brought back after it dropped is left to come up first; the poll that
        // finds it answering and idle runs this again.
        if (RunningWork > 0 || _recovering)
        {
            _agentRestartPending = true;
            return;
        }

        _agentRestartPending = false;
        agent ??= AgentLifecycle?.Invoke();
        if (agent is null) return;

        Say(Ui.Shell.StatusApplyingConcurrency);
        AgentLifecycleResult result;
        _restartingAgent = true;
        try
        {
            result = await agent.ExecuteAsync(AgentLifecycleAction.Restart).ConfigureAwait(true);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or
            UnauthorizedAccessException or TimeoutException)
        {
            result = new AgentLifecycleResult(false, error.Message);
        }
        finally
        {
            _restartingAgent = false;
        }

        Say(result.Succeeded ? Ui.Shell.AdaptiveConcurrencyActive : Ui.Shell.ConcurrencyAgentRestartFailed);

        // A save made while the agent was restarting is applied now, or once the work is done.
        if (_agentRestartPending) await RestartAgentForSettingsAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// A sentence in the status bar's first cell, where 1.x put its messages. The next pane that
    /// is opened or picked writes over it, as it did there.
    /// </summary>
    private void Say(string message) => ShellStatus = ShellStatus with { Location = message };

    /// <summary>How often the open workspace's panes are re-read while transfers run, as in 1.x.</summary>
    private static readonly TimeSpan TransferRefreshInterval = TimeSpan.FromSeconds(5);

    private DispatcherTimer? _transferRefresh;
    private bool _reloadWhenTransfersSettle;

    /// <summary>
    /// Files appear in a folder as a copy runs, so the open workspace is re-read while anything is
    /// queued or running, and once more when it stops. Not per transfer: a folder copy finishes
    /// thousands of them, and a reload each would spend the copy re-listing the destination.
    /// </summary>
    private void FollowTransfers()
    {
        if (Queue.ActiveCount + Queue.QueuedCount > 0)
        {
            _reloadWhenTransfersSettle = true;
            _transferRefresh ??= new DispatcherTimer(
                TransferRefreshInterval, DispatcherPriority.Background, (_, _) => RefreshOpenPanesQuietly());
            _transferRefresh.Start();
            return;
        }

        _transferRefresh?.Stop();
        if (_reloadWhenTransfersSettle)
        {
            _reloadWhenTransfersSettle = false;
            RefreshOpenPanesQuietly();
        }
    }

    private void RefreshOpenPanesQuietly()
    {
        if (Workspaces.ElementAtOrDefault(SelectedWorkspace)?.Workspace is not { } workspace) return;
        foreach (var pane in workspace.Panes) _ = pane.RefreshQuietlyAsync();
    }

    /// <summary>Welcome, the panel, Sync tasks, the queue and every pane with a connection.</summary>
    private void ReloadEverything()
    {
        _ = Overview?.RefreshAsync();
        _ = Sidebar.RefreshAsync();
        _ = SyncTasks?.RefreshAsync();
        _ = Queue.RefreshAsync(background: true);
        foreach (var pane in Workspaces
            .Select(static tab => tab.Workspace)
            .OfType<WorkspaceModel>()
            .SelectMany(static workspace => workspace.Panes)
            .Where(static pane => pane.Connection is not null && !pane.IsTerminal))
        {
            if (pane.RefreshCommand.CanExecute(null)) pane.RefreshCommand.Execute(null);
        }
    }

    /// <summary>The Welcome page, which follows the status bar and reloads when the agent connects.</summary>
    internal OverviewModel? Overview { get; set; }

    public ICommand NewWorkspaceCommand { get; init; } = null!;

    /// <summary>The X on a workspace tab; its parameter is the tab.</summary>
    public ICommand CloseWorkspaceCommand => _closeWorkspace ??= new RelayCommand(
        tab => _ = CloseWorkspaceAsync(tab as WorkspaceTab),
        tab => tab is WorkspaceTab { IsClosable: true });

    private RelayCommand? _closeWorkspace;

    public static string CloseWorkspaceLabel => Ui.Commands.WorkspaceCloseWorkspace;

    /// <summary>
    /// Closes a workspace tab, or the one showing when no tab is named.
    /// </summary>
    /// <remarks>
    /// Its panes are disposed with it, which closes their connections and any shell they held: a
    /// closed tab that kept a socket open would be one nobody could reach to close it. The tab to
    /// its left is shown next, as 1.x did, so closing is never a jump to somewhere unrelated. A
    /// workspace with unsaved changes asks first, and Cancel keeps it open.
    /// </remarks>
    internal async Task CloseWorkspaceAsync(WorkspaceTab? tab = null)
    {
        tab ??= Workspaces.ElementAtOrDefault(SelectedWorkspace);
        if (tab is not { Workspace: { } workspace } || !Workspaces.Contains(tab)) return;
        if (Files is { } files &&
            !await files.ConfirmDiscardAsync(tab, Ui.Dialogs.CloseWorkspaceCaption).ConfigureAwait(true))
        {
            return;
        }

        var index = Workspaces.IndexOf(tab);
        if (index < 0) return;

        var showing = SelectedWorkspace;
        Workspaces.RemoveAt(index);
        SelectedWorkspace = showing > index || showing >= Workspaces.Count
            ? Math.Max(0, showing - 1)
            : showing;
        await workspace.DisposeAsync().ConfigureAwait(true);
    }

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

    /// <summary>
    /// Which tab is showing. Settable, because adding a workspace moves to it.
    /// </summary>
    /// <remarks>
    /// Coming to Sync tasks reads the agent again, as 1.x's control did each time it became
    /// visible: so the tab is never a table of nothing waiting for Refresh, and a run the scheduler
    /// finished while somebody was in a workspace is listed when they come back. Here rather than
    /// in the view, because the view is rebuilt whenever its sub-tab is, and 1.x did not reload for
    /// going to Run history and back.
    /// </remarks>
    public int SelectedWorkspace
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedWorkspace)));
            if (SyncPage is { } page && ReferenceEquals(Workspaces.ElementAtOrDefault(value)?.Page, page))
            {
                _ = SyncTasks?.RefreshAsync();
            }
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
            Overview?.UpdateStatus(value);
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

        monitor.StatusChanged += (_, e) => Dispatcher.UIThread.Post(() => Observe(e.Status));

        // Every surface reloads when the agent comes back, and so do the panes: a pane that failed
        // while the agent was away kept its error until somebody refreshed it by hand.
        DesktopAgentAvailability.Changed += (_, e) =>
        {
            if (e.Recovered) Dispatcher.UIThread.Post(ReloadEverything);
        };

        monitor.Start();
    }

    /// <summary>
    /// Takes in what the agent said about itself: the status bar, recovery when it has gone, and a
    /// restart for saved settings once the work it was waiting on is done.
    /// </summary>
    internal void Observe(AgentMonitorStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        // Kept whole as well as summarised. The status bar needs two of these five fields; the
        // agent control window needs all of them, including the detail that says *why* the
        // agent is in recovery, which is the one thing a single word cannot carry. A running
        // synchronization is work in progress as much as a transfer is, as 1.4 counted it.
        AgentStatus = status;
        ShellStatus = ShellStatus with
        {
            AgentState = status.State,
            ActiveJobs = status.ActiveTransfers + status.ActiveSyncRuns,
        };

        // An agent restarted for settings is down on purpose and the restart brings it back, and
        // one left behind by a closing shell is not relaunched beside the shutdown.
        if (_restartingAgent || _closing) return;

        if (status.State == AgentConnectionState.Disconnected) _ = RecoverAgentAsync();
        if (_agentRestartPending && status.ActiveTransfers + status.ActiveSyncRuns == 0)
        {
            AgentSettingsRestart = RestartAgentForSettingsAsync();
        }
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
    /// <param name="syncAgent">
    /// What its Sync tasks screens read. The default is an agent with nothing saved, as the samples
    /// have.
    /// </param>
    internal static ShellPreviewModel CreateOnWorkspace(
        Func<ISshTerminalAgentClient>? terminals = null,
        Func<ISyncManagementAgentClient>? syncAgent = null) =>
        Build(selectedWorkspace: 2, terminals, syncAgent: syncAgent);

    /// <summary>
    /// The shell the application runs: Welcome and Sync tasks, and no workspace until somebody
    /// asks for one, as 1.x opened. Its Welcome page and its panes ask the agent for what there is.
    /// </summary>
    /// <remarks>
    /// The samples above keep a workspace open because the tests photograph and drive one; the
    /// application used to share them, which is why it opened on a "Workspace 1" nobody made.
    /// </remarks>
    internal static ShellPreviewModel CreateLive() => Build(live: true);

    private static ShellPreviewModel Build(
        int selectedWorkspace = 0,
        Func<ISshTerminalAgentClient>? terminals = null,
        bool live = false,
        Func<ISyncManagementAgentClient>? syncAgent = null)
    {
        terminals ??= static () => new NamedPipeSshTerminalAgentClient();

        // Only the application's own shell reads the real agent's sync profiles. The others are
        // photographed, and a picture of whatever a dev agent held that day compares with nothing.
        if (syncAgent is null)
        {
            syncAgent = live
                ? static () => new NamedPipeSyncManagementAgentClient()
                : EmptySyncAgent.Factory;
        }

        var router = new ShellCommandRouter();

        // Folders being read for a transfer, before their files are all queued. The workspaces
        // write to it and the queue reads it: its Active tab and its log show them, and its Cancel
        // stops them, as 1.x's did.
        var drops = new PendingDropRegistry();

        // The queue is built first so the workspace can tell it to refresh the moment a transfer
        // is accepted, rather than leaving somebody to watch a tab count that updates on its own
        // schedule two seconds later.
        var queue = new TransferQueueModel(
            static () => new NamedPipeTransferQueueAgentClient(),
            // The Logs tab. Its clients are made per read, like the queue's, and it reads nothing
            // until its tab is opened.
            new ActivityLogModel(new ActivityLogReader(
                static () => new NamedPipeTransferQueueAgentClient(),
                static () => new NamedPipeSyncManagementAgentClient(),
                drops.Snapshot).ReadAsync),
            // Clear all history asks first, as 1.x's did.
            Services.ShellServices.Dialogs)
        {
            PendingDrops = drops
        };
        if (live)
        {
            // Only while "Warn before clearing all transfer history" is on, which was saved and
            // never read; and its "Don't show this warning again" turns it off.
            queue.ClearAllConfirmation = static () =>
                ReadPreferences()?.ConfirmBeforeClearingTransferHistory ?? true;
            queue.StopClearAllConfirmation = static () => UpdatePreferences(static preferences =>
                preferences with { ConfirmBeforeClearingTransferHistory = false });
        }

        var model = new ShellPreviewModel(router)
        {
            Menus = BuildMenus(router),
            PaneContextEntries = BuildPaneContextMenu(router),
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
            WorkspaceFactory = (preset, name) => new WorkspaceModel(
                // A pane makes its own inspector client per operation, for the same reason a
                // transfer does: one held open is one that broke when the agent restarted.
                () => Loaded(live, new BrowserPaneModel(
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
                    edit: Services.ShellServices.EditExternallyAsync,
                    batchRename: static (sources, occupied) => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                        () => BatchRenameWindow.AskAsync(Services.ShellServices.MainWindow(), sources, occupied)))),
                static () => new NamedPipeTransferQueueAgentClient(),
                static () => new NamedPipeRemoteStorageAgentClient(),
                static () => new NamedPipeObjectInspectorAgentClient(),
                // From the newest, as 1.x's was, so a transfer just queued is on the page
                // shown even when Next had moved on from it.
                () => queue.RefreshFromStartAsync(),
                preset,
                Services.ShellServices.Dialogs,
                name)
            {
                PendingDrops = drops
            },
        };

        // The queue names a transfer's connections from the list the sidebar has read, rather than
        // listing them again on every poll. At startup the queue's first read can come back before
        // the sidebar's, so the rows are read again as the list comes in, rather than showing
        // short ids until the next poll. A rename shows as the Connection Manager closes, too.
        queue.ConnectionName = model.Sidebar.NameOf;
        model.Sidebar.Listed = () => _ = queue.RefreshAsync(background: true);

        // The Welcome page. Live, it asks the agent itself, as 1.x's did; its buttons go where the
        // same commands go from the menu and the toolbar.
        var overview = live
            ? OverviewModel.ForAgent(
                ShellStatusSnapshot.Initial,
                static () => new NamedPipeRemoteStorageAgentClient(),
                static () => new NamedPipeTransferQueueAgentClient())
            : OverviewModel.Create(ShellStatusSnapshot.Initial);
        overview.NewWorkspaceCommand = router.For(UiCommandIds.WorkspaceNewWorkspace);
        overview.ConnectionsCommand = new RelayCommand(_ => model.Sidebar.ManageCommand?.Execute(null));
        model.Overview = overview;
        model.Workspaces.Add(new WorkspaceTab(Ui.Shell.TabWelcome, LucideIconKind.House, overview));
        var syncPage = new TabbedPageModel(
            [
                // The tasks screen asks the agent for the saved profiles and the runs behind them.
                // A client per load rather than one held open, for the reason the panes and the
                // queue already hold: a connection kept across an agent restart is one that has to
                // be found broken before it can be replaced.
                new PageTab(Ui.Sync.TasksTitle, SyncTasksModel.Create(syncAgent)),
                // And the review screen reads one run at a time, by id. It takes the dialog service
                // because Approve & dispatch is the only button in the shell that authorises the
                // agent to delete files without naming them, and it confirms before it does.
                new PageTab(
                    Ui.Sync.RunHistoryAndReview,
                    SyncRunHistoryModel.Create(syncAgent, Services.ShellServices.Dialogs)),
            ]);
        model.SyncPage = syncPage;
        var syncTab = new WorkspaceTab(Ui.Shell.TabSyncTasks, LucideIconKind.ArrowLeftRight, syncPage);
        model.Workspaces.Add(syncTab);
        overview.SyncTasksCommand = new RelayCommand(_ => model.SelectedWorkspace = model.Workspaces.IndexOf(syncTab));

        // The tasks screen's own Run history button goes to the sub-tab beside it. It knows the
        // page it is on no more than the panel knows what a pane is, so the shell says what it does.
        if (model.SyncTasks is { } tasks)
        {
            tasks.RunHistoryCommand = new RelayCommand(_ => syncPage.SelectedIndex = 1);
        }

        if (!live) model.AddWorkspace(WorkspacePreset.All[1]);

        // Workspace files, live only: the samples have no settings file to record a recent
        // workspace in, and nobody to ask whether to save one before it closes.
        if (live)
        {
            model.Files = new WorkspaceFiles(
                model,
                Services.ShellServices.Dialogs,
                Services.ShellServices.FilePicker,
                WorkspaceBookmarks.ForCurrentUser(),
                static () => ReadPreferences()?.ReconnectRemotePanesAutomatically ?? true);
        }

        model.SelectedWorkspace = selectedWorkspace;
        model.RouteToActivePane();
        model.WatchTheStatusBar();

        // The panel knows nothing about panes, so the shell hands it the one thing it needs to
        // act on a row: what to do with a connection id.
        model.Sidebar.OpenConnection = id =>
        {
            // From Welcome or Sync tasks there is no pane to open into, so a workspace is made
            // for it, as 1.x did, rather than the double-click doing nothing.
            if (model.ActivePane() is null) model.AddWorkspace(TwoPanes());
            if (model.ActivePane() is { } pane) _ = pane.OpenConnectionAsync(id);
        };

        // And beside what is open, as 1.x's "Open in new pane" did: the active pane is split and
        // the connection opens in the new half, which becomes the active one.
        model.Sidebar.OpenConnectionInNewPane = id =>
        {
            if (model.ActivePane() is null)
            {
                model.AddWorkspace(TwoPanes());
            }
            else if (model.Workspaces.ElementAtOrDefault(model.SelectedWorkspace)?.Workspace is { } workspace &&
                     workspace.SplitActive(WorkspaceDockEdge.Right) is { } added)
            {
                added.IsActive = true;
            }

            if (model.ActivePane() is { } pane) _ = pane.OpenConnectionAsync(id);
        };
        return model;

        // Two panes in the orientation Settings names under "Default pane layout", as 1.x made a
        // workspace to open a connection into. Not the new-workspace choice: that answers the "+",
        // and a double-click on a connection is not a request for a single pane or four.
        WorkspacePreset TwoPanes()
        {
            var layout = live ? ReadPreferences()?.DefaultWorkspaceLayout : null;
            return WorkspacePreset.Find(2, layout ?? WorkspaceLayout.SideBySide) ?? WorkspacePreset.All[1];
        }
    }


    /// <summary>
    /// A pane that, in the running application, starts reading its connections at once, asks
    /// before deleting according to the saved setting, and opens a shell on the saved terminal
    /// settings.
    /// </summary>
    private static BrowserPaneModel Loaded(bool live, BrowserPaneModel pane)
    {
        if (!live) return pane;
        pane.DeleteConfirmation = static () => ReadPreferences()?.ConfirmBeforeDeletingItems ?? true;
        pane.StopDeleteConfirmation = static () => UpdatePreferences(static preferences =>
            preferences with { ConfirmBeforeDeletingItems = false });
        pane.TerminalPreferences = static () => ReadPreferences()?.SshTerminal;
        _ = pane.LoadConnectionsAsync();
        return pane;
    }

    private static DesktopUpdatePreferences? ReadPreferences()
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

    private static void UpdatePreferences(Func<DesktopUpdatePreferences, DesktopUpdatePreferences> change)
    {
        try
        {
            var store = new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot);
            store.Save(change(store.Load()));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // The warning comes back next time rather than failing the delete over it.
        }
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
        SaveGroups,
        LoadGroupIcons,
        SaveGroupIcons,
        static (current, title) => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () => IconPickerWindow.AskAsync(Services.ShellServices.MainWindow(), current, title)));

    /// <summary>The icons chosen for groups in the connections panel, from the settings file.</summary>
    private static IReadOnlyDictionary<string, string>? LoadGroupIcons()
    {
        try
        {
            var store = new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot);
            store.Preflight();
            return store.Load().FolderIcons;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void SaveGroupIcons(IReadOnlyDictionary<string, string> icons)
    {
        try
        {
            var store = new DesktopConfigStore(DesktopFrameworkPaths.Resolve().ApplicationRoot);
            store.Preflight();
            store.Save(store.Load() with { FolderIcons = icons });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The icon shows either way; only remembering it is lost.
        }
    }

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

    /// <summary>
    /// A pane's right-click menu, as 1.x had it: making things, then changing the selection, then
    /// moving it between panes, then the pane itself. Built from the catalog, so each entry is the
    /// menu bar's own -- the same label, icon, shortcut and routing to the pane it was opened on.
    /// Open and Edit are the pane's own and are added by the view in front of these.
    /// </summary>
    private static IReadOnlyList<object> BuildPaneContextMenu(ShellCommandRouter router) =>
    [
        .. new[]
        {
            UiCommandIds.EditNewFolder, UiCommandIds.EditNewEmptyFile, ToolbarLayout.Separator,
            UiCommandIds.EditRename, UiCommandIds.EditBatchRename, ToolbarLayout.Separator,
            UiCommandIds.EditCopy, UiCommandIds.EditCut, UiCommandIds.EditPaste, UiCommandIds.EditDelete,
            ToolbarLayout.Separator,
            UiCommandIds.ViewRefresh, UiCommandIds.EditSelectAll, ToolbarLayout.Separator,
            UiCommandIds.EditProperties,
        }
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
