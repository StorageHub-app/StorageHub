using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StorageHub.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}

public sealed record MenuSection(string Header);

public sealed record ToolbarCommand(string Label, string Description);

public sealed record ConnectionCard(string Name, string Detail, bool IsSelected);

public sealed record PaneItem(string Name, string Size, string Type);

public sealed record WorkspaceTab(
    string Title,
    string LeftTitle,
    bool LeftIsActive,
    IReadOnlyList<PaneItem> Left,
    string RightTitle,
    IReadOnlyList<PaneItem> Right);

public sealed record QueueTab(string Title);

/// <summary>
/// The shell's shape, with stand-in content.
/// </summary>
/// <remarks>
/// A view model in outline only. The real one is written per screen as each is ported, against the
/// controllers and agent clients that already exist in Desktop.Core; what this carries is the
/// structure those will fill, so the layout can be measured and photographed before any of them do.
/// </remarks>
public sealed class ShellPreviewModel
{
    public IReadOnlyList<MenuSection> Menus { get; init; } = [];

    public IReadOnlyList<ToolbarCommand> ToolbarCommands { get; init; } = [];

    public IReadOnlyList<ConnectionCard> Connections { get; init; } = [];

    public IReadOnlyList<WorkspaceTab> Workspaces { get; init; } = [];

    public IReadOnlyList<QueueTab> QueueTabs { get; init; } = [];

    public int SelectedWorkspace { get; init; }

    public string Location { get; init; } = string.Empty;

    public string Selection { get; init; } = string.Empty;

    public string Queue { get; init; } = string.Empty;

    public string AgentStatus { get; init; } = string.Empty;
}

/// <summary>Stand-in content, so the shell has something to lay out and photograph.</summary>
public static class ShellPreview
{
    public static ShellPreviewModel Sample { get; } = new()
    {
        // The nine menus UiCommandCatalog declares.
        Menus =
        [
            new("Workspace"), new("Edit"), new("View"), new("Go"), new("Connections"),
            new("Transfer"), new("Sync"), new("Tools"), new("Help"),
        ],
        ToolbarCommands =
        [
            new("New", "New workspace"), new("Open", "Open"), new("Save", "Save workspace"),
            new("Back", "Back"), new("Forward", "Forward"), new("Refresh", "Refresh"),
            new("Copy", "Copy"), new("Move", "Move"), new("Delete", "Delete"),
        ],
        Connections =
        [
            new("Design Archive", "Local / UNC · Studio", false),
            new("Studio Assets (S3)", "S3 / Object Storage · Cloud", true),
            new("Site Backups", "Local / UNC · Servers", false),
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
