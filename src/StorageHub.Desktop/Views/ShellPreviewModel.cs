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
    LucideIconKind? Icon,
    UiIconTone Tone,
    ICommand Command)
{
    /// <summary>What a menu entry draws in place of its label: a line between groups.</summary>
    internal const string SeparatorLabel = "-";

    internal bool IsPrimary => Tone == UiIconTone.Primary;

    internal bool IsDanger => Tone == UiIconTone.Danger;

    /// <summary>The tooltip, or none for an entry with nothing to say, rather than an empty box.</summary>
    internal string? ToolTip => Description.Length > 0 ? Description : null;

    /// <summary>A group's heading, such as the Workspace menu's "Pinned": shown bold, never run.</summary>
    internal bool IsHeading { get; init; }

    /// <summary>
    /// A workspace whose file was not there when the menu was drawn. Dimmed, not disabled, as in
    /// 1.x: clicking it is how it offers to leave the list.
    /// </summary>
    internal bool IsMissing { get; init; }

    /// <summary>
    /// The check mark of an entry that shows or hides something, such as View > Connections Panel,
    /// which 1.x ticked in the menu and drew pressed on the toolbar. <see cref="CommandCheck.None"/>
    /// for one that just runs, so the menu's and the toolbar's bindings always find a value.
    /// </summary>
    internal CommandCheck Check { get; init; } = CommandCheck.None;

    /// <summary>
    /// The key the command answers to, which Settings can rebind while a menu holds the entry.
    /// <see cref="CommandShortcut.None"/> for an entry that is no command, such as a workspace in
    /// the Workspace menu.
    /// </summary>
    internal CommandShortcut Shortcut { get; init; } = CommandShortcut.None;

    /// <summary>
    /// A check box for an entry that has a check of its own, which is what tells a screen reader it
    /// is on or off. The tick itself is drawn around the entry's icon, as 1.x drew it.
    /// </summary>
    internal Avalonia.Controls.MenuItemToggleType ToggleType => ReferenceEquals(Check, CommandCheck.None)
        ? Avalonia.Controls.MenuItemToggleType.None
        : Avalonia.Controls.MenuItemToggleType.CheckBox;
}

/// <summary>
/// Whether an entry that turns something on and off is on, which can change while its menu is open.
/// </summary>
/// <remarks>
/// A class of its own rather than a property of <see cref="CommandEntry"/>: an entry is a record,
/// and a record that changed while a menu held it would stop being equal to itself.
/// </remarks>
internal sealed class CommandCheck : INotifyPropertyChanged
{
    /// <summary>
    /// The check of every entry that just runs: never set, so it is never on. One shared instance
    /// rather than null, which a binding would report as an error for each entry.
    /// </summary>
    internal static CommandCheck None { get; } = new();

    public bool IsChecked
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One of the menu bar's menus.</summary>
/// <remarks>
/// The entries are observable because the Workspace menu's change while the shell runs: the pinned
/// and recent workspaces are listed in it, ahead of Exit, as 1.x listed them.
/// </remarks>
internal sealed record MenuSection(UiMenuId Menu, string Header, IReadOnlyList<CommandEntry> Items);

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
    private ShellStatusSnapshot _shellStatus = ShellStatusSnapshot.Initial;
    private string _agentToolTip = Ui.Shell.AgentControlsTooltip;
    private DesktopUpdateSnapshot _update = DesktopUpdateSnapshot.Initial;

    /// <summary>Puts the location back once the last message has been up long enough.</summary>
    private IDisposable? _unsay;

    /// <summary>What <see cref="Say"/> said, until <see cref="MessageLifetime"/> has passed.</summary>
    private string? _said;

    /// <summary>What <see cref="SayUntilResolved"/> said, until it is resolved or replaced.</summary>
    private string? _standing;

    internal ShellPreviewModel(ShellCommandRouter router)
    {
        Router = router;
        // A command with nowhere to go is reachable by shortcut, which does not consult
        // CanExecute the way a menu does. Saying so beats appearing to ignore the keystroke.
        Router.Unhandled += (_, id) => Say(Ui.Format(Ui.Shell.CommandNotBuiltFormat, id));
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
                or nameof(TransferQueueModel.BytesPerSecond)
                or nameof(TransferQueueModel.SelectedTab))
            {
                ShellStatus = ShellStatus with
                {
                    // Running syncs count, as 1.x's agent report counted them, so the queue's
                    // count and the agent's agree rather than taking turns in the cell.
                    ActiveJobs = Queue.ActiveCount + (AgentStatus?.ActiveSyncRuns ?? 0),
                    QueuedJobs = Queue.QueuedCount,

                    // The Logs tab reads the log rather than the queue, so the last rate read
                    // would stay up after the transfer had finished. 0 B/s claims nothing, as
                    // 1.x's always did, until the queue is read again.
                    TransferBytesPerSecond = Queue.Tabs.ElementAtOrDefault(Queue.SelectedTab) is { IsLog: true }
                        ? 0
                        : Queue.BytesPerSecond
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
            // "Copied 3 item(s). Choose a destination and paste.", as 1.x said on staging, and what
            // a paste or drop came to, which nothing else in the window shows. A refused one is a
            // warning instead, as 1.x's was.
            workspace.Announced += (_, message) => Say(message);
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
    /// A message being said keeps the first cell: 1.x's bar was not redrawn by a click in a pane,
    /// so "Choose a destination and paste" was still there when the destination was chosen.
    /// </remarks>
    private void ReportThePane()
    {
        var message = _said ?? _standing;
        if (ActivePane() is not { } pane)
        {
            ShellStatus = ShellStatus with
            {
                Location = message ?? Ui.Shell.StatusNoConnection,
                SelectedItems = 0,
                SelectedBytes = 0
            };
            return;
        }

        var chosen = pane.SelectedRows.Where(static row => !row.IsParentNavigation).ToArray();
        ShellStatus = ShellStatus with
        {
            // A pane with nothing chosen still has a path, "/", which is nowhere.
            Location = message ?? (pane.Source is null || string.IsNullOrWhiteSpace(pane.Path)
                ? Ui.Shell.StatusNoConnection
                : pane.Path),
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
    /// How long a message stays in the status bar's first cell before the location comes back.
    /// </summary>
    /// <remarks>
    /// 1.x's lasted until the agent's next report redrew the bar, which came every eight seconds;
    /// this is that at its longest, without the chance of a message gone as soon as it came.
    /// </remarks>
    internal TimeSpan MessageLifetime { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// A short sentence in the status bar's first cell, where 1.x put its messages: what was staged,
    /// what a paste or drop came to, that settings were imported or exported, a command that has
    /// nowhere to go yet.
    /// </summary>
    /// <remarks>
    /// It stands in for the location until <see cref="MessageLifetime"/> has passed or something
    /// else is said, and then gives the cell back to the location, or to a state still standing
    /// (<see cref="SayUntilResolved"/>). Another pane picked, a folder opened or a row selected
    /// does not write over it, as none of them redrew 1.x's bar.
    /// </remarks>
    internal void Say(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _said = message;
        _unsay?.Dispose();
        _unsay = DispatcherTimer.RunOnce(() =>
        {
            _said = null;
            ReportThePane();
        }, MessageLifetime);
        ReportThePane();
    }

    /// <summary>
    /// A sentence for a state rather than an event, which stays in the first cell until it is
    /// over: saved settings waiting for transfers to finish, or being applied.
    /// </summary>
    /// <remarks>
    /// It replaces whatever was said before it. A short message said meanwhile goes over it for
    /// its usual while and then gives the cell back to it, since the state is still true: a paste
    /// made while settings wait for the transfers does not make them any less pending.
    /// <see cref="Resolve"/> or another state ends it.
    /// </remarks>
    internal void SayUntilResolved(string state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _standing = state;
        _said = null;
        _unsay?.Dispose();
        ReportThePane();
    }

    /// <summary>
    /// Ends the state <see cref="SayUntilResolved"/> put up, and says what it came to for the
    /// usual while.
    /// </summary>
    internal void Resolve(string outcome)
    {
        _standing = null;
        Say(outcome);
    }

    /// <summary>What Tools > Background agent runs, which the status bar's agent cell runs too.</summary>
    public ICommand AgentControlCommand => Router.For(UiCommandIds.ToolsBackgroundAgent);

    /// <summary>What Help > Check for updates runs, which the status bar's update cell runs too, as 1.4's link did.</summary>
    public ICommand UpdateCheckCommand => Router.For(UiCommandIds.HelpCheckForUpdates);

    /// <summary>
    /// What the updater last said, in the status bar's last cell, as 1.4's update link said it.
    /// </summary>
    /// <remarks>
    /// The updater's own words, "Updates: current (2.0.0)" or "Update 2.0.1 available", and 1.4's
    /// colours: green once an update is ready or installing, amber while one waits to be fetched,
    /// red when the check or the download failed, and muted otherwise.
    /// </remarks>
    public DesktopUpdateSnapshot Update
    {
        get => _update;
        private set
        {
            if (_update == value) return;
            _update = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Update)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateIsReady)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateIsAvailable)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateFailed)));
        }
    }

    public bool UpdateIsReady => _update.State is DesktopUpdateState.ReadyToRestart or DesktopUpdateState.Installing;

    public bool UpdateIsAvailable => _update.State is DesktopUpdateState.UpdateAvailable;

    public bool UpdateFailed => _update.State is DesktopUpdateState.Failed;

    /// <summary>Takes what the updater reports. Called on the UI thread.</summary>
    internal void ShowUpdate(DesktopUpdateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Update = snapshot;
    }

    /// <summary>
    /// The agent cell's tooltip: what the agent last said about itself, as 1.x's carried.
    /// </summary>
    /// <remarks>
    /// "Open background agent controls" until the agent has said anything, then its own detail,
    /// and while it is brought back, what that came to. The one word in the cell cannot say why the
    /// agent is in recovery; this can.
    /// </remarks>
    public string AgentToolTip
    {
        get => _agentToolTip;
        private set
        {
            if (_agentToolTip == value) return;
            _agentToolTip = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AgentToolTip)));
        }
    }

    /// <summary>What a screen reader calls the bar and each of its cells, as 1.x named them.</summary>
    public static string ApplicationStatusLabel => Ui.Shell.ApplicationStatus;

    public static string CurrentLocationLabel => Ui.Shell.CurrentLocation;

    public static string SelectionSummaryLabel => Ui.Shell.SelectionSummary;

    public static string TransferSpeedLabel => Ui.Shell.TransferSpeed;

    public static string QueueSummaryLabel => Ui.Shell.QueueSummary;

    public static string AgentStatusLabel => Ui.Shell.AgentStatus;

    public static string UpdateStatusLabel => Ui.Shell.UpdateStatus;

    public static string UpdateStatusToolTip => Ui.Shell.CheckForUpdatesTooltip;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<MenuSection> Menus { get; init; } = [];

    /// <summary>
    /// The toolbar's buttons and dividers, in the order Settings saved them.
    /// </summary>
    /// <remarks>
    /// Observable, because <see cref="ArrangeToolbar"/> fills it again when Settings changes it.
    /// </remarks>
    public ObservableCollection<object> Toolbar { get; } = [];

    /// <summary>Whether the toolbar's buttons show their labels, beside or under their icons.</summary>
    public bool ToolbarShowsLabels => _toolbarLabels != ToolbarLabelStyle.IconsOnly;

    /// <summary>Whether those labels sit under the icons rather than beside them.</summary>
    public bool ToolbarLabelsUnderIcons => _toolbarLabels == ToolbarLabelStyle.TextUnderIcon;

    /// <summary>The chevron at the toolbar's end, which holds the buttons that did not fit.</summary>
    public static string MoreToolbarCommandsLabel => Ui.Shell.MoreToolbarCommands;

    private ToolbarLabelStyle _toolbarLabels = ToolbarLabelStyle.IconsOnly;

    /// <summary>
    /// The checks of the entries that show or hide something, which a toolbar button built again
    /// has to share with the menu entry for the same command.
    /// </summary>
    internal IReadOnlyDictionary<string, CommandCheck> Checks { get; init; } =
        new Dictionary<string, CommandCheck>(StringComparer.Ordinal);

    /// <summary>
    /// Builds the toolbar from a saved layout, and labels its buttons the saved way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What 1.x's PopulateToolbar did. A command this shell has not wired is left out rather than
    /// drawn as a button that does nothing, as the menu leaves it out, and the dividers that leaves
    /// leading, trailing or doubled go with it, so a missing command cannot show as a gap between
    /// two lines.
    /// </para>
    /// <para>
    /// Each button is the menu entry's command, so it dims and runs exactly as the entry does. The
    /// list is replaced only when it changed, so closing Settings without touching the toolbar
    /// does not rebuild it under the pointer.
    /// </para>
    /// </remarks>
    internal void ArrangeToolbar(IReadOnlyList<string>? items, ToolbarLabelStyle labels)
    {
        var next = new List<object>();
        foreach (var id in ToolbarLayout.Resolve(items))
        {
            if (id == ToolbarLayout.Separator)
            {
                next.Add(ToolbarSeparator.Instance);
            }
            else if (UiCommandCatalog.IsAvailable(id))
            {
                next.Add(ShellPreview.ToEntry(UiCommandCatalog.GetDefinition(id), Router, Checks));
            }
        }

        for (var index = next.Count - 1; index >= 0; index--)
        {
            if (next[index] is ToolbarSeparator &&
                (index == 0 || index == next.Count - 1 || next[index - 1] is ToolbarSeparator))
            {
                next.RemoveAt(index);
            }
        }

        if (!Toolbar.SequenceEqual(next))
        {
            Toolbar.Clear();
            foreach (var item in next) Toolbar.Add(item);
        }

        if (_toolbarLabels == labels || !Enum.IsDefined(labels)) return;
        _toolbarLabels = labels;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolbarShowsLabels)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolbarLabelsUnderIcons)));
    }

    /// <summary>
    /// Brings the shell into line with what Settings saved: the keys each command answers to and
    /// the menus show, the toolbar, the side the connections panel is on, and whether a favourite
    /// is listed in its own group too.
    /// </summary>
    /// <remarks>
    /// What 1.x did once its Settings dialog had saved (RefreshShortcutPresentation,
    /// ApplyToolbarPreferences and the panel's ShowFavoritesInTheirFolders), so a change shows at
    /// once rather than at the next start. An import can change the same settings, so it comes
    /// here too.
    /// </remarks>
    internal void FollowSettings(DesktopUpdatePreferences saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        Router.UseShortcuts(saved.Shortcuts);
        ArrangeToolbar(saved.ToolbarItems, saved.ToolbarLabels);
        ConnectionsPanel.FollowSettings(saved);
        Sidebar.ShowFavoritesInTheirFolders = saved.ShowFavoritesInTheirFolders;
    }

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
    /// <remarks>
    /// Its pinned and recent lists are drawn in two places, Welcome and the Workspace menu, and
    /// both are drawn again whenever either list changes, whichever route changed it.
    /// </remarks>
    internal WorkspaceFiles? Files
    {
        get => _files;
        set
        {
            if (ReferenceEquals(_files, value)) return;
            if (_files is { } previous) previous.Bookmarks.Changed -= OnBookmarksChanged;
            _files = value;
            if (value is not null) value.Bookmarks.Changed += OnBookmarksChanged;
            ShowWorkspaceShortcuts();
        }
    }

    private WorkspaceFiles? _files;

    private void OnBookmarksChanged(object? sender, EventArgs e) => ShowWorkspaceShortcuts();

    /// <summary>
    /// Draws the pinned and recent workspaces again, on Welcome and in the Workspace menu.
    /// </summary>
    internal void ShowWorkspaceShortcuts()
    {
        var shortcuts = _files?.Bookmarks.Shortcuts ?? [];
        Overview?.ShowWorkspaces(shortcuts);
        ListWorkspacesInTheMenu(shortcuts);
    }

    /// <summary>
    /// Draws the Workspace menu's own entries again as it opens, as 1.x did.
    /// </summary>
    /// <remarks>
    /// Whether each file is there, and whether the workspace showing is pinned, are both answers
    /// that can change without the lists changing: a file deleted elsewhere, another tab chosen.
    /// </remarks>
    internal void WorkspaceMenuOpening() => ListWorkspacesInTheMenu(_files?.Bookmarks.Shortcuts ?? []);

    /// <summary>The Workspace menu's entries from the catalog, kept so each redraw starts from them.</summary>
    private CommandEntry[]? _workspaceCatalogEntries;

    /// <summary>Never enabled: a heading or a line in a menu is there to be read.</summary>
    private static readonly RelayCommand Inert = new(static _ => { }, static _ => false);

    /// <summary>
    /// Puts Pin Workspace, then the pinned and then the recent workspaces, into the Workspace menu
    /// ahead of Exit, as 1.x's RebuildWorkspaceSection did.
    /// </summary>
    /// <remarks>
    /// A group with nothing in it leaves out its heading too. The entries are replaced only when
    /// something about them changed, so opening the menu does not rebuild it under the keyboard.
    /// </remarks>
    private void ListWorkspacesInTheMenu(IReadOnlyList<WorkspaceShortcutView> shortcuts)
    {
        if (_files is not { } files ||
            Menus.FirstOrDefault(static section => section.Menu == UiMenuId.Workspace)?.Items
                is not ObservableCollection<CommandEntry> items)
        {
            return;
        }

        _workspaceCatalogEntries ??= [.. items];
        var catalog = _workspaceCatalogEntries;
        var exit = Array.FindIndex(catalog, static entry => entry.Id == UiCommandIds.WorkspaceExit);
        if (exit < 0) exit = catalog.Length;

        var showing = ActiveWorkspace()?.FilePath;
        var pinned = showing is not null && WorkspaceShortcutSettings.Contains(
            [.. shortcuts.Where(static shortcut => shortcut.IsPinned).Select(static shortcut => shortcut.Entry)],
            showing);
        _togglePin ??= new RelayCommand(
            _ => { if (_files is { } current) _ = current.TogglePinAsync(); },
            _ => _files is not null && ActiveWorkspace() is not null);

        var next = new List<CommandEntry>(catalog[..exit])
        {
            Line(),
            new(
                "workspace.pin",
                pinned ? Ui.Shell.UnpinWorkspace : Ui.Shell.PinWorkspace,
                pinned ? Ui.Shell.RemoveFromPinned : Ui.Shell.PinWorkspaceHint,
                null,
                UiIconTone.Text,
                _togglePin),
        };
        Group(Ui.Overview.WorkspaceStatePinned, shortcuts.Where(static shortcut => shortcut.IsPinned));
        Group(Ui.Overview.WorkspaceStateRecent, shortcuts.Where(static shortcut => !shortcut.IsPinned));
        next.AddRange(catalog[exit..]);

        if (items.Select(Key).SequenceEqual(next.Select(Key))) return;
        items.Clear();
        foreach (var entry in next) items.Add(entry);

        void Group(string heading, IEnumerable<WorkspaceShortcutView> group)
        {
            var entries = group.ToArray();
            if (entries.Length == 0) return;

            next.Add(Line());
            next.Add(new("workspace.heading", heading, string.Empty, null, UiIconTone.Text, Inert) { IsHeading = true });
            foreach (var shortcut in entries)
            {
                var path = shortcut.Entry.Path;

                // An underscore in a label marks its access key, so a name's own is doubled. And a
                // workspace called "-" would be drawn as a line nobody can click, so its dash is
                // marked as the access key instead, which reads the same.
                var name = shortcut.Entry.DisplayName.Replace("_", "__", StringComparison.Ordinal);
                if (name == CommandEntry.SeparatorLabel) name = "_" + name;
                next.Add(new(
                    "workspace.shortcut:" + path,
                    shortcut.LooksPresent ? name : Ui.Format(Ui.Shell.WorkspaceMissingEntryFormat, name),
                    path,
                    null,
                    UiIconTone.Text,
                    new RelayCommand(_ => _ = files.OpenPathAsync(path)))
                {
                    IsMissing = !shortcut.LooksPresent
                });
            }
        }

        static CommandEntry Line() =>
            new("workspace.separator", CommandEntry.SeparatorLabel, string.Empty, null, UiIconTone.Text, Inert);

        static (string, string, bool) Key(CommandEntry entry) => (entry.Id, entry.Label, entry.IsMissing);
    }

    /// <summary>Workspace > Pin Workspace, which follows the tab showing as Save does.</summary>
    private RelayCommand? _togglePin;

    /// <summary>The Go menu's entries from the catalog, kept so each redraw starts from them.</summary>
    private CommandEntry[]? _goCatalogEntries;

    /// <summary>
    /// Lists the favourite connections at the foot of the Go menu, under a bold Favorites, as 1.x's
    /// RebuildFavoritesSection did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which connections, and in what order, is <see cref="FavoriteConnectionMenu"/>'s rule: the
    /// enabled favourites a pane can open, by name. Choosing one opens it in the active pane, as
    /// double-clicking its card does, since Go is the menu that moves the pane somebody is looking
    /// at. With none, the menu says so and how to make one, rather than a heading over nothing.
    /// </para>
    /// <para>
    /// Drawn again whenever the connections panel has listed the connections, so a favourite
    /// toggled on a card is there the next time the menu opens. Replaced only when something in it
    /// changed, so a listing does not rebuild a menu that is open.
    /// </para>
    /// </remarks>
    internal void ListFavoritesInTheMenu(IReadOnlyList<Contracts.Ipc.ConnectionSummary> connections)
    {
        if (Menus.FirstOrDefault(static section => section.Menu == UiMenuId.Go)?.Items
            is not ObservableCollection<CommandEntry> items)
        {
            return;
        }

        _goCatalogEntries ??= [.. items];
        var next = new List<CommandEntry>(_goCatalogEntries)
        {
            new("go.favorites.separator", CommandEntry.SeparatorLabel, string.Empty, null, UiIconTone.Text, Inert),
            new("go.favorites.heading", Ui.Shell.Favorites, string.Empty, null, UiIconTone.Text, Inert) { IsHeading = true },
        };

        var favorites = FavoriteConnectionMenu.Select(connections);
        if (favorites.Count == 0)
        {
            next.Add(new(
                "go.favorites.none", Ui.Shell.NoFavoriteConnections, Ui.Shell.FavoritesEmptyHint, null, UiIconTone.Text, Inert));
        }

        foreach (var connection in favorites)
        {
            var id = connection.ConnectionId;

            // Doubled and marked as the Workspace menu's names are, so a name is shown as typed.
            var name = connection.DisplayName.Replace("_", "__", StringComparison.Ordinal);
            if (name == CommandEntry.SeparatorLabel) name = "_" + name;
            next.Add(new(
                "go.favorite:" + id.ToString("D"),
                name,
                string.IsNullOrWhiteSpace(connection.FolderPath)
                    ? ConnectionProviderCatalog.Get(ConnectionCardFactory.MapProvider(connection.Provider)).DisplayName
                    : connection.FolderPath,
                null,
                UiIconTone.Text,
                new RelayCommand(_ => Sidebar.OpenConnection?.Invoke(id))));
        }

        if (items.Select(Key).SequenceEqual(next.Select(Key))) return;
        items.Clear();
        foreach (var entry in next) items.Add(entry);

        static (string, string, string) Key(CommandEntry entry) => (entry.Id, entry.Label, entry.Description);
    }

    /// <summary>
    /// Dims the commands that need a workspace while Welcome or Sync tasks is showing, as 1.x did.
    /// </summary>
    /// <remarks>
    /// Save, Save As, Rename, Close and Pin, the same five 1.x's UpdateWorkspaceCommandState and its
    /// Pin entry governed. The pane commands stay as they were in 1.x, lit on every tab: its
    /// Welcome (docs/ui-reference/01) shows Back, Copy and the rest available and only Save dimmed.
    /// </remarks>
    internal void DimWorkspaceCommandsOnPages()
    {
        foreach (var id in new[]
        {
            UiCommandIds.WorkspaceSaveWorkspace,
            UiCommandIds.WorkspaceSaveWorkspaceAs,
            UiCommandIds.WorkspaceRenameWorkspace,
            UiCommandIds.WorkspaceCloseWorkspace,
        })
        {
            Router.When(id, () => ActiveWorkspace() is not null);
        }

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedWorkspace)) Reconsider();
        };
        Workspaces.CollectionChanged += (_, _) => Reconsider();

        void Reconsider()
        {
            Router.Reconsider();
            _togglePin?.RaiseCanExecuteChanged();
        }
    }

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

    /// <summary>Where the connections panel is docked, how wide, and whether it is showing.</summary>
    internal ConnectionsPanelLayout ConnectionsPanel { get; init; } = new();

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
        AgentToolTip = Ui.Shell.AgentReconnectingTooltip;
        try
        {
            var result = await recover(CancellationToken.None).ConfigureAwait(true);
            if (result.IsReady)
            {
                ShellStatus = ShellStatus with { AgentState = AgentConnectionState.Connected };
                AgentToolTip = result.Status == AgentEnsureStatus.Started
                    ? Ui.Shell.AgentRestarted
                    : Ui.Shell.AgentReconnected;
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
    /// effect yet looks exactly like one that has, and goes on saying it until the restart is done.
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
            SayUntilResolved(Ui.Shell.StatusConcurrencyPendingIdle);
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
        // finds it answering and idle runs this again. The status bar says the settings are
        // waiting unless it says so already: each such poll comes through here, and saying it
        // again would cut short whatever was said over it.
        if (RunningWork > 0 || _recovering)
        {
            _agentRestartPending = true;
            if (_standing != Ui.Shell.StatusConcurrencyPendingIdle)
            {
                SayUntilResolved(Ui.Shell.StatusConcurrencyPendingIdle);
            }

            return;
        }

        _agentRestartPending = false;
        agent ??= AgentLifecycle?.Invoke();
        if (agent is null)
        {
            // Nothing will restart it now, so the wait said in the status bar is over too.
            Resolve(Ui.Shell.StatusConcurrencyRestartRequired);
            return;
        }

        SayUntilResolved(Ui.Shell.StatusApplyingConcurrency);
        AgentLifecycleResult? result = null;
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

            // Here rather than after, so an error the catch does not expect still ends "Applying",
            // which would otherwise stay up for good, as no caller looks at this task's error.
            Resolve(result is { Succeeded: true }
                ? Ui.Shell.AdaptiveConcurrencyActive
                : Ui.Shell.ConcurrencyAgentRestartFailed);
        }

        // A save made while the agent was restarting is applied now, or once the work is done.
        if (_agentRestartPending) await RestartAgentForSettingsAsync().ConfigureAwait(true);
    }

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

    /// <summary>
    /// The Welcome page, which follows the status bar, reloads when the agent connects, and lists
    /// the pinned and recent workspaces.
    /// </summary>
    internal OverviewModel? Overview
    {
        get => _overview;
        set
        {
            _overview = value;
            if (value is not null && _files is not null) ShowWorkspaceShortcuts();
        }
    }

    private OverviewModel? _overview;

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

        // The cell follows what the last call found, as 1.x's did, so it stops saying "connected"
        // the moment a call fails rather than at the next poll, eight seconds later. A poll's own
        // report is posted after this one, so recovery mode from a poll still has the last word.
        // Every surface reloads when the agent comes back, and so do the panes: a pane that failed
        // while the agent was away kept its error until somebody refreshed it by hand.
        DesktopAgentAvailability.Changed += (_, e) => Dispatcher.UIThread.Post(() =>
        {
            var state = e.Availability switch
            {
                AgentAvailability.Online => AgentConnectionState.Connected,
                AgentAvailability.Reconnecting => AgentConnectionState.Reconnecting,
                AgentAvailability.Offline => AgentConnectionState.Disconnected,
                _ => ShellStatus.AgentState,
            };
            if (ShellStatus.AgentState != state) ShellStatus = ShellStatus with { AgentState = state };

            if (e.Recovered) ReloadEverything();
        });

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

        // Every report, as 1.x set it: an agent that says nothing leaves no old sentence up.
        AgentToolTip = string.IsNullOrWhiteSpace(status.Detail)
            ? Ui.Shell.AgentControlsTooltip
            : status.Detail;

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

        // Built before the menus, which tick View > Connections Panel while it shows, and the
        // toolbar, which draws that button pressed.
        var panel = new ConnectionsPanelLayout();
        var checks = new Dictionary<string, CommandCheck>(StringComparer.Ordinal)
        {
            [UiCommandIds.ViewConnectionsPanel] = panel.Check,
        };
        var model = new ShellPreviewModel(router)
        {
            Menus = BuildMenus(router, checks),
            PaneContextEntries = BuildPaneContextMenu(router),
            Checks = checks,
            SelectedWorkspace = selectedWorkspace,
            Sidebar = BuildSidebar(router),
            ConnectionsPanel = panel,
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
                    // once, instead of reporting the mismatch in the agent's words. Only the
                    // application's own shell has one, as only 1.4's did: a restart stops whatever
                    // agent answers, and a preview's is a test's, which must not reach the real one.
                    agentLifecycle: live ? AgentLifecycleControllers.ThatCanRestartTheAgent : null,
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
        model.Sidebar.Answered = () => _ = queue.RefreshAsync(background: true);

        // The Welcome page. Live, it asks the agent itself, as 1.x's did; its buttons go where the
        // same commands go from the menu and the toolbar.
        var overview = live
            ? OverviewModel.ForAgent(
                ShellStatusSnapshot.Initial,
                static () => new NamedPipeRemoteStorageAgentClient(),
                static () => new NamedPipeTransferQueueAgentClient())
            : OverviewModel.Create(ShellStatusSnapshot.Initial);
        overview.NewWorkspaceCommand = router.For(UiCommandIds.WorkspaceNewWorkspace);

        // Connections opens the Edit Connection dialog on a new connection, as 1.x's button did:
        // the saved ones are already listed in the panel beside it.
        overview.ConnectionsCommand = router.For(UiCommandIds.ConnectionsNewConnection);

        // Its workspace list acts through the same files and lists as the Workspace menu, so a
        // file opened, pinned or removed from either is the same file everywhere.
        overview.OpenWorkspace = path => model.Files is { } files ? files.OpenPathAsync(path) : Task.CompletedTask;
        overview.ToggleWorkspacePin = shortcut => model.Files?.Bookmarks.TogglePin(shortcut.Entry.Path, shortcut.Entry.Name);
        overview.ForgetWorkspace = path => model.Files?.Bookmarks.Forget(path);
        overview.CopyWorkspacePath = path => Services.ShellServices.Clipboard.SetTextAsync(path);
        model.Overview = overview;
        model.Workspaces.Add(new WorkspaceTab(Ui.Shell.TabWelcome, LucideIconKind.House, overview));
        var syncPage = new TabbedPageModel(
            [
                // The tasks screen asks the agent for the saved profiles and the runs behind them.
                // A client per load rather than one held open, for the reason the panes and the
                // queue already hold: a connection kept across an agent restart is one that has to
                // be found broken before it can be replaced.
                new PageTab(Ui.Sync.TasksTab, SyncTasksModel.Create(syncAgent)),
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
        model.DimWorkspaceCommandsOnPages();
        model.WatchTheStatusBar();

        // The keys and the toolbar as Settings saved them, as 1.x built its menus and toolbar from
        // the saved preferences. The samples have no settings file, and keep the catalog's.
        var saved = (live ? ReadPreferences() : null) ?? DesktopUpdatePreferences.Defaults;
        router.UseShortcuts(saved.Shortcuts);
        model.ArrangeToolbar(saved.ToolbarItems, saved.ToolbarLabels);

        // View > Connections Panel (Ctrl+B) and Move Connections Panel, and the panel's own Move
        // and Hide, which 1.x offered under its "..." as well.
        router.Handle(UiCommandIds.ViewConnectionsPanel, panel.Toggle);
        router.Handle(UiCommandIds.ViewMoveConnectionsPanel, panel.MoveToOtherSide);
        model.Sidebar.MoveToOtherSideCommand = router.For(UiCommandIds.ViewMoveConnectionsPanel);
        model.Sidebar.HidePanelCommand = new RelayCommand(_ => panel.SetVisible(false));

        // Live, the panel opens the way it was left and every change is saved as it happens. It
        // reads the connections again when it comes back, as 1.x did: they may have changed while
        // it was hidden.
        if (live)
        {
            panel.Restore(saved);
            panel.Persist = UpdatePreferences;
            panel.Shown += (_, _) => _ = model.Sidebar.RefreshAsync();
        }

        // Favourites in their own group as well as under Favorites, or only there, as Settings says.
        model.Sidebar.ShowFavoritesInTheirFolders = saved.ShowFavoritesInTheirFolders;

        // Go > Favorites follows what the panel lists. Live only, as the Workspace menu's own
        // entries are: the samples list no connections and keep the catalog's menu.
        if (live)
        {
            model.ListFavoritesInTheMenu([]);
            model.Sidebar.Listed += (_, _) => model.ListFavoritesInTheMenu(model.Sidebar.Connections);
        }

        // Welcome's recent connections put favourites first and say so, so a favourite toggled or
        // a connection deleted on the panel reads them again, as 1.x's ConnectionsChanged did.
        model.Sidebar.ConnectionsChanged += (_, _) => _ = model.Overview?.RefreshAsync();

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

    /// <summary>The saved settings, or null when the file cannot be read.</summary>
    internal static DesktopUpdatePreferences? ReadPreferences()
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
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException)
        {
            // Save refuses a file it would not read back, as well as one it cannot write. Either
            // way the change stands for this session and is only not remembered: the delete warning
            // comes back next time, and the panel opens as it was last saved.
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
            () => IconPickerWindow.AskAsync(Services.ShellServices.MainWindow(), current, title)),

        // Toggle favorite reads and writes the whole profile, as the Edit Connection dialog does,
        // and Delete removes one at the version the panel listed it at.
        static () => new NamedPipeRemoteConnectionProfileClient());

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

    private static IReadOnlyList<MenuSection> BuildMenus(
        ShellCommandRouter router, IReadOnlyDictionary<string, CommandCheck> checks) =>
    [
        .. UiCommandCatalog.Menus
            .Select(menu => new MenuSection(
                menu,
                UiCommandCatalog.MenuTitle(menu),
                new ObservableCollection<CommandEntry>(UiCommandCatalog.ForMenu(menu)
                    .Where(definition => UiCommandCatalog.IsAvailable(definition.Id))
                    .Select(definition => ToEntry(definition, router, checks)))))
            .Where(section => section.Items.Count > 0),
    ];

    /// <summary>
    /// A pane's right-click menu, as 1.x had it: making things, then changing the selection, then
    /// moving it between panes, then the pane itself. Built from the catalog, so each entry is the
    /// menu bar's own -- the same label, icon, shortcut and routing to the pane it was opened on.
    /// Open and Edit are the pane's own and are added by the view in front of these. Invert selection
    /// is 2.0's addition, from 1.x's "..." on the FILES row: with it, this menu holds everything that
    /// row does, which is what lets a pane hide the row.
    /// </summary>
    private static IReadOnlyList<object> BuildPaneContextMenu(ShellCommandRouter router) =>
    [
        .. new[]
        {
            UiCommandIds.EditNewFolder, UiCommandIds.EditNewEmptyFile, ToolbarLayout.Separator,
            UiCommandIds.EditRename, UiCommandIds.EditBatchRename, ToolbarLayout.Separator,
            UiCommandIds.EditCopy, UiCommandIds.EditCut, UiCommandIds.EditPaste, UiCommandIds.EditDelete,
            ToolbarLayout.Separator,
            UiCommandIds.ViewRefresh, UiCommandIds.EditSelectAll, UiCommandIds.EditInvertSelection,
            ToolbarLayout.Separator,
            UiCommandIds.EditProperties,
        }
        .Where(id => id == ToolbarLayout.Separator || UiCommandCatalog.IsAvailable(id))
        .Select(object (id) => id == ToolbarLayout.Separator
            ? ToolbarSeparator.Instance
            : ToEntry(UiCommandCatalog.GetDefinition(id), router)),
    ];

    internal static CommandEntry ToEntry(
        UiCommandDefinition definition,
        ShellCommandRouter router,
        IReadOnlyDictionary<string, CommandCheck>? checks = null) => new(
        definition.Id,
        definition.Label,
        definition.Description,
        IconCatalog.Resolve(definition.Glyph),
        definition.Tone,
        router.For(definition.Id))
    {
        Check = checks?.GetValueOrDefault(definition.Id) ?? CommandCheck.None,
        Shortcut = router.ShortcutOf(definition.Id),
    };
}
