using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Input;
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

internal sealed record ConnectionCard(string Name, string Detail, bool IsSelected);

internal sealed record PaneItem(string Name, string Size, string Type);

internal sealed record WorkspaceTab(
    string Title,
    string LeftTitle,
    bool LeftIsActive,
    IReadOnlyList<PaneItem> Left,
    string RightTitle,
    IReadOnlyList<PaneItem> Right);

internal sealed record QueueTab(string Title);

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

    public IReadOnlyList<ConnectionCard> Connections { get; init; } = [];

    public IReadOnlyList<WorkspaceTab> Workspaces { get; init; } = [];

    public IReadOnlyList<QueueTab> QueueTabs { get; init; } = [];

    public int SelectedWorkspace { get; init; }

    public string Location { get; init; } = string.Empty;

    public string Selection { get; init; } = string.Empty;

    public string Queue { get; init; } = string.Empty;

    public string AgentStatus { get; init; } = string.Empty;
}

/// <summary>Builds the shell's model: real commands, stand-in content.</summary>
internal static class ShellPreview
{
    internal static ShellPreviewModel Sample { get; } = Build();

    private static ShellPreviewModel Build()
    {
        var router = new ShellCommandRouter();
        return new ShellPreviewModel(router)
        {
            Menus = BuildMenus(router),
            Toolbar = BuildToolbar(router),
            Connections =
            [
                new("Design Archive", "Local / UNC \u00b7 Studio", false),
                new("Studio Assets (S3)", "S3 / Object Storage \u00b7 Cloud", true),
                new("Site Backups", "Local / UNC \u00b7 Servers", false),
            ],
            Workspaces =
            [
                new("Welcome", "Overview", false, [], "Recent", []),
                new("Sync tasks", "Profiles", false, [], "Runs", []),
                new(
                    "Workspace 1",
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
            SelectedWorkspace = 2,
            QueueTabs =
            [
                new("Active (0)"), new("Queued (0)"), new("Paused (0)"), new("Failed (0)"),
                new("Completed (2)"), new("Conflicts (0)"), new("Logs"),
            ],
            Location = "No connection",
            Selection = "0 selected",
            Queue = "Queue: 0",
            AgentStatus = "Agent: connected",
        };
    }

    private static IReadOnlyList<MenuSection> BuildMenus(ShellCommandRouter router) =>
    [
        .. UiCommandCatalog.Menus.Select(menu => new MenuSection(
            UiCommandCatalog.MenuTitle(menu),
            [
                .. UiCommandCatalog.ForMenu(menu)
                    .Where(definition => UiCommandCatalog.IsAvailable(definition.Id))
                    .Select(definition => ToEntry(definition, router)),
            ])),
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
