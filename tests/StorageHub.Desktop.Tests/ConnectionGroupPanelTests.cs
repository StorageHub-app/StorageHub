using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Lucide.Avalonia;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Shell;
using StorageHub.Desktop.Themes;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The connections panel as it is drawn: groups, and a badge on every row.
/// </summary>
/// <remarks>
/// <see cref="ConnectionGrouping"/> is tested in Desktop.Core, where the arrangement is a value
/// and every rule about it can be asserted. What is left here is that the arrangement reaches the
/// panel, that rearranging it is remembered, and that the badge somebody reads at a glance is the
/// one the connection actually warrants.
/// </remarks>
public class ConnectionGroupPanelTests
{
    [AvaloniaFact]
    public async Task ConnectionsArriveInTheGroupTheirFolderNames()
    {
        var saved = new List<ConnectionGroupEntry>();
        var sidebar = Sidebar(saved, Summary("Studio Assets", "Team"), Summary("Scratch"));

        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Team", ConnectionGrouping.DefaultGroupName], sidebar.Groups.Select(g => g.Name));
        Assert.Equal(["Studio Assets"], sidebar.Groups[0].Connections.Select(r => r.Name));
    }

    /// <summary>The badge is what is left of the Storage and Clients split, on the row.</summary>
    [AvaloniaFact]
    public async Task EveryRowSaysWhetherItIsStorageOrAClient()
    {
        var sidebar = Sidebar(
            [],
            Summary("Studio Assets"),
            Summary("build-box", provider: StorageConnectionProvider.Ssh, client: true));

        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        var rows = sidebar.Groups.SelectMany(static g => g.Connections).ToArray();
        Assert.Equal(Ui.Connections.BadgeStorage, rows.Single(r => r.Name == "Studio Assets").Badge);
        Assert.Equal(Ui.Connections.BadgeClient, rows.Single(r => r.Name == "build-box").Badge);
        Assert.True(rows.Single(r => r.Name == "build-box").IsClient);
    }

    /// <summary>Filing a connection somewhere else is remembered straight away.</summary>
    [AvaloniaFact]
    public async Task MovingAConnectionIsSavedAtOnce()
    {
        var saved = new List<ConnectionGroupEntry>();
        var first = Summary("Studio Assets");
        var sidebar = Sidebar(saved, first, Summary("Scratch"));
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        sidebar.Move(first.ConnectionId, "Archive", 0);

        Assert.Equal("Archive", sidebar.Groups[^1].Name);
        Assert.Equal(["Studio Assets"], sidebar.Groups[^1].Connections.Select(r => r.Name));

        // Saved on every rearrangement, so a shell that does not close cleanly still reopens the
        // way it was left.
        Assert.Contains(saved, group => group.Name == "Archive" && group.Members.Count == 1);
    }

    /// <summary>A saved arrangement is what the panel comes back as.</summary>
    [AvaloniaFact]
    public async Task ASavedArrangementIsRestored()
    {
        var first = Summary("Studio Assets");
        var second = Summary("Scratch");
        var saved = new List<ConnectionGroupEntry>
        {
            new("Archive", [second.ConnectionId]),
            new("Live", [first.ConnectionId])
        };
        var sidebar = Sidebar(saved, first, second);

        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["Archive", "Live"], sidebar.Groups.Select(g => g.Name));
        Assert.Equal(["Scratch"], sidebar.Groups[0].Connections.Select(r => r.Name));
    }

    /// <summary>Removing a group tidies the panel and never loses a connection.</summary>
    [AvaloniaFact]
    public async Task RemovingAGroupKeepsItsConnections()
    {
        var first = Summary("Studio Assets", "Team");
        var sidebar = Sidebar([], first, Summary("Scratch"));
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        sidebar.RemoveGroup("Team");

        var remaining = Assert.Single(sidebar.Groups);
        Assert.Equal(["Scratch", "Studio Assets"], remaining.Connections.Select(r => r.Name));
    }

    /// <summary>A group's icon is drawn on its heading and remembered at once.</summary>
    [AvaloniaFact]
    public async Task AGroupIconIsShownAndSaved()
    {
        IReadOnlyDictionary<string, string>? savedIcons = null;
        var sidebar = IconSidebar(
            icons => savedIcons = icons,
            (_, _) => Task.FromResult(new IconChoice(true, "layers")),
            [Summary("Studio Assets", "Team")]);
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LucideIconKind.Folder, sidebar.Groups[0].Icon);

        await sidebar.ChangeGroupIconAsync("Team");

        Assert.Equal("layers", savedIcons?["Team"]);
        Assert.NotEqual(LucideIconKind.Folder, sidebar.Groups[0].Icon);
    }

    /// <summary>An icon goes with its group when renamed, and away with it when removed.</summary>
    [AvaloniaFact]
    public async Task AGroupIconFollowsRenameAndRemove()
    {
        IReadOnlyDictionary<string, string>? savedIcons = null;
        var sidebar = IconSidebar(
            icons => savedIcons = icons,
            (_, _) => Task.FromResult(new IconChoice(true, "layers")),
            [Summary("Studio Assets", "Team"), Summary("Scratch")],
            dialogs: new AnswerDialogs("Crew"));
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);
        await sidebar.ChangeGroupIconAsync("Team");

        await sidebar.RenameGroupAsync("Team", TestContext.Current.CancellationToken);
        Assert.Equal(["Crew"], savedIcons!.Keys);

        sidebar.RemoveGroup("Crew");
        Assert.Empty(savedIcons!);
    }

    /// <summary>Choosing the default clears the group back to a folder.</summary>
    [AvaloniaFact]
    public async Task AGroupIconCanBeCleared()
    {
        IReadOnlyDictionary<string, string>? savedIcons = null;
        var sidebar = IconSidebar(
            icons => savedIcons = icons,
            (_, _) => Task.FromResult(new IconChoice(true, null)),
            [Summary("Studio Assets", "Team")],
            loaded: new Dictionary<string, string> { ["Team"] = "layers" });
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(LucideIconKind.Folder, sidebar.Groups[0].Icon);

        await sidebar.ChangeGroupIconAsync("Team");

        Assert.Empty(savedIcons!);
        Assert.Equal(LucideIconKind.Folder, sidebar.Groups[0].Icon);
    }

    /// <summary>Every connection reaches the panel, with a badge, under its group's heading.</summary>
    [AvaloniaFact]
    public async Task EveryConnectionIsDrawnWithItsBadge()
    {
        var window = await PanelAsync(
            Summary("Studio Assets", "Team"),
            Summary("build-box", "Team", StorageConnectionProvider.Ssh, client: true),
            Summary("Scratch"));

        var badges = window.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("badge"))
            .ToArray();

        Assert.Equal(3, badges.Length);
        Assert.Equal(1, badges.Count(badge => badge.Classes.Contains("client")));
    }

    /// <summary>
    /// Photographs the panel in both appearances, for a human to look at.
    /// </summary>
    /// <remarks>
    /// The badge is new paint in two states, and the group heading is new paint in one. Set
    /// STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThePanelCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        var window = await PanelAsync(
            Summary("Studio Assets", "Team"),
            Summary("Renders", "Team"),
            Summary("build-box", "Team", StorageConnectionProvider.Ssh, client: true),
            Summary("Site Backups"),
            Summary("Old NAS"));

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        using var stream = File.Create(
            Path.Combine(directory, $"connections-{(dark ? "dark" : "light")}.png"));
        frame!.Save(stream, new PngBitmapEncoderOptions());
    }

    /// <summary>The panel on its own, with connections in it.</summary>
    private static async Task<Window> PanelAsync(params ConnectionSummary[] connections)
    {
        // "Team" carries a chosen icon, so the photograph shows a heading with one and one without.
        var sidebar = new ConnectionsSidebar(
            new RelayCommand(static _ => { }),
            () => new FixedAgent(connections),
            loadIcons: () => new Dictionary<string, string> { ["Team"] = "layers" });
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        var window = new Window
        {
            Content = new ConnectionsPanelView { DataContext = sidebar },
            Width = 300,
            Height = 620
        };
        window.Show();
        window.Measure(new Size(300, 620));
        window.Arrange(new Rect(0, 0, 300, 620));
        window.UpdateLayout();
        return window;
    }

    /// <summary>A sidebar that keeps group icons, answering the picker as told.</summary>
    private static ConnectionsSidebar IconSidebar(
        Action<IReadOnlyDictionary<string, string>> saveIcons,
        Func<string?, string, Task<IconChoice>> pickIcon,
        ConnectionSummary[] connections,
        IDialogService? dialogs = null,
        IReadOnlyDictionary<string, string>? loaded = null) =>
        new(
            new RelayCommand(static _ => { }),
            () => new FixedAgent(connections),
            dialogs,
            loadIcons: () => loaded,
            saveIcons: saveIcons,
            pickIcon: pickIcon);

    /// <summary>Answers every prompt with the same text.</summary>
    private sealed class AnswerDialogs(string answer) : IDialogService
    {
        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DialogChoice> ConfirmAsync(DialogRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(DialogChoice.Cancel);

        public Task<string?> PromptAsync(DialogPromptRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(answer);
    }

    /// <summary>A sidebar over a fixed set of connections, saving into a list.</summary>
    private static ConnectionsSidebar Sidebar(
        List<ConnectionGroupEntry> saved,
        params ConnectionSummary[] connections) =>
        new(
            new RelayCommand(static _ => { }),
            () => new FixedAgent(connections),
            dialogs: null,
            load: () => saved,
            save: groups =>
            {
                saved.Clear();
                saved.AddRange(groups);
            });

    private static ConnectionSummary Summary(
        string name,
        string? folder = null,
        StorageConnectionProvider provider = StorageConnectionProvider.S3,
        bool client = false) =>
        new(
            Guid.NewGuid(),
            name,
            provider,
            folder,
            Tags: [],
            IsFavorite: false,
            IsEnabled: true,
            IconKey: null,
            AccentColor: null,
            Version: 1,
            client ? ConnectionProfileType.Client : ConnectionProfileType.Storage);

    private sealed class FixedAgent(ConnectionSummary[] connections) : IRemoteStorageAgentClient
    {
        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionListResponse(StorageIpcContract.CurrentVersion, connections));

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
