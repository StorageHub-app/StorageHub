using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Threading;
using Lucide.Avalonia;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Themes;

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

internal sealed record PaneItem(string Name, string Size, string Type);

internal sealed record WorkspaceTab(
    string Title,
    LucideIconKind Icon,
    object? Page,
    string LeftTitle,
    bool LeftIsActive,
    IReadOnlyList<PaneItem> Left,
    string RightTitle,
    IReadOnlyList<PaneItem> Right);

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
        Router.Invoked += (_, id) => Status = id;
    }

    /// <summary>Dispatch for every command, whether it arrives by menu, toolbar or keystroke.</summary>
    public ShellCommandRouter Router { get; }

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

    public IReadOnlyList<WorkspaceTab> Workspaces { get; init; } = [];

    public ConnectionsSidebar Sidebar { get; init; } = null!;

    public ICommand NewWorkspaceCommand { get; init; } = null!;

    public string NewWorkspaceLabel { get; init; } = string.Empty;

    /// <summary>The transfer queue, reading the agent rather than a stand-in.</summary>
    public TransferQueueModel Queue { get; init; } = null!;

    public int SelectedWorkspace { get; init; }

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
            ShellStatus = ShellStatus with
            {
                AgentState = e.Status.State,
                ActiveJobs = e.Status.ActiveTransfers,
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

    private static ShellPreviewModel Build(int selectedWorkspace = 0)
    {
        var router = new ShellCommandRouter();
        return new ShellPreviewModel(router)
        {
            Menus = BuildMenus(router),
            Toolbar = BuildToolbar(router),
            Workspaces =
            [
                new(
                    Ui.Shell.TabWelcome,
                    LucideIconKind.House,
                    OverviewModel.Create(ShellStatusSnapshot.Initial),
                    "Overview", false, [], "Recent", []),
                new(
                    Ui.Shell.TabSyncTasks,
                    LucideIconKind.ArrowLeftRight,
                    new TabbedPageModel(
                    [
                        new PageTab(Ui.Sync.TasksTitle, SyncTasksModel.Create()),
                        new PageTab(Ui.Sync.RunHistoryAndReview, SyncRunHistoryModel.Create()),
                    ]),
                    "Profiles", false, [], "Runs", []),
                new(
                    "Workspace 1",
                    LucideIconKind.Folder,
                    null,
                    "Pane 1 (Active)",
                    true,
                    [
                        new("C:\\", "930,5 GiB", "Local disk drive"),
                        new("D:\\", "447,1 GiB", "Local disk drive"),
                        new("reports", string.Empty, "Folder"),
                        new("render-0421.exr", "184,2 MiB", "EXR image"),
                    ],
                    "Pane 2",
                    [
                        new("Design Archive", string.Empty, "LOCAL"),
                        new("Field Recordings", string.Empty, "LOCAL"),
                        new("Site Backups", string.Empty, "LOCAL"),
                        new("Studio Assets (S3)", string.Empty, "S3"),
                    ]),
            ],
            SelectedWorkspace = selectedWorkspace,
            Sidebar = BuildSidebar(router),
            NewWorkspaceCommand = router.For(UiCommandIds.WorkspaceNewWorkspace),
            NewWorkspaceLabel = Ui.Commands.WorkspaceNewWorkspace,
            Queue = new TransferQueueModel(static () => new NamedPipeTransferQueueAgentClient()),
        };
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
        static () => new NamedPipeRemoteStorageAgentClient());

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

    private static IReadOnlyList<object> BuildToolbar(ShellCommandRouter router) =>
    [
        .. ToolbarLayout.Resolve(null).Select(object (id) => id == ToolbarLayout.Separator
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
