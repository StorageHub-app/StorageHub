using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
/// The connections panel as it is drawn: the agent's groups, Ungrouped, and a badge on every row.
/// </summary>
/// <remarks>
/// The groups are the agent's, so every test here runs against <see cref="GroupAgent"/>, which
/// keeps connections and groups the way the agent's database does: a connection names its group,
/// a write moves it to a new version, and the old arrangement is brought in once.
/// </remarks>
public class ConnectionGroupPanelTests
{
    /// <summary>
    /// The arrangement the settings file kept is brought across once, empty groups, icons and folder
    /// groups included, and from then on the panel shows the agent's groups, then Ungrouped.
    /// </summary>
    [AvaloniaFact]
    public async Task TheOldArrangementIsBroughtAcrossOnceAndTheAgentsGroupsAreShown()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var studio = Summary("Studio Assets", "Team");
        var renders = Summary("Renders");
        var scratch = Summary("Scratch");
        var agent = new GroupAgent(studio, renders, scratch);
        var saved = new List<ConnectionGroupEntry> { new("Archive", []), new("Live", [renders.ConnectionId]) };
        ConnectionsSidebar Panel() => new(
            new RelayCommand(static _ => { }),
            () => agent,
            legacyGroups: () => saved,
            legacyIcons: static () => new Dictionary<string, string> { ["Live"] = "layers" },
            profiles: () => agent);

        var sidebar = Panel();
        await sidebar.RefreshAsync(cancellation);

        Assert.Equal(["Archive", "Live", "Team", Ui.Connections.Ungrouped], sidebar.Groups.Select(static g => g.Name));
        Assert.True(sidebar.Groups[0].ShowsEmptyLine);
        Assert.Equal(["Renders"], sidebar.Groups[1].Connections.Select(static r => r.Name));
        Assert.NotEqual(LucideIconKind.Folder, sidebar.Groups[1].Icon);
        Assert.Equal(["Studio Assets"], sidebar.Groups[2].Connections.Select(static r => r.Name));
        Assert.True(sidebar.Groups[3].IsUngrouped);
        Assert.Equal(["Scratch"], sidebar.Groups[3].Connections.Select(static r => r.Name));

        // Once: a later arrangement, from this panel or another desktop's, changes nothing.
        saved.Add(new ConnectionGroupEntry("Later", [scratch.ConnectionId]));
        await Panel().RefreshAsync(cancellation);
        await sidebar.RefreshAsync(cancellation);
        Assert.Equal(1, agent.ImportsApplied);
        Assert.DoesNotContain(sidebar.Groups, static g => g.Name == "Later");
    }

    /// <summary>The badge is what is left of the Storage and Clients split, on the row.</summary>
    [AvaloniaFact]
    public async Task EveryRowSaysWhetherItIsStorageOrAClient()
    {
        var sidebar = Sidebar(
            new GroupAgent(
                Summary("Studio Assets"),
                Summary("build-box", provider: StorageConnectionProvider.Ssh, client: true)));

        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        var rows = sidebar.Groups.SelectMany(static g => g.Connections).ToArray();
        Assert.Equal(Ui.Connections.BadgeStorage, rows.Single(r => r.Name == "Studio Assets").Badge);
        Assert.Equal(Ui.Connections.BadgeClient, rows.Single(r => r.Name == "build-box").Badge);
        Assert.True(rows.Single(r => r.Name == "build-box").IsClient);
    }

    /// <summary>
    /// Dropping a connection on a group files it there in the agent, and on Ungrouped takes it out;
    /// within a group the connections are in name order, wherever they were dropped.
    /// </summary>
    [AvaloniaFact]
    public async Task DroppingAConnectionOnAGroupFilesItThereInTheAgent()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var studio = Summary("Studio Assets");
        var agent = new GroupAgent(studio, Summary("Scratch"));
        var archive = agent.AddGroup("Archive");
        agent.File(Summary("Annex"), archive.GroupId);
        var sidebar = Sidebar(agent);
        await sidebar.RefreshAsync(cancellation);

        await sidebar.MoveToGroupAsync(studio.ConnectionId, sidebar.Groups.Single(static g => g.Name == "Archive"), cancellation);

        Assert.Equal(["Annex", "Studio Assets"], sidebar.Groups[0].Connections.Select(static r => r.Name));
        Assert.Equal(2, agent.Connections.Single(c => c.ConnectionId == studio.ConnectionId).Version);

        await sidebar.MoveToGroupAsync(studio.ConnectionId, sidebar.Groups.Single(static g => g.IsUngrouped), cancellation);
        Assert.Equal(["Scratch", "Studio Assets"], sidebar.Groups.Single(static g => g.IsUngrouped).Connections.Select(static r => r.Name));
    }

    /// <summary>
    /// A group's menu: rename, icon and colour, move up and down, and remove, which asks first and
    /// sends its connections to Ungrouped rather than deleting them.
    /// </summary>
    [AvaloniaFact]
    public async Task AGroupCanBeRenamedRecolouredMovedAndRemovedWithoutLosingAConnection()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var agent = new GroupAgent();
        var team = agent.AddGroup("Team");
        agent.AddGroup("Lab");
        agent.File(Summary("Studio Assets"), team.GroupId);
        var dialogs = new AnswerDialogs("Crew");
        var choice = new IconChoice(true, "layers", "#16A34A");
        var sidebar = new ConnectionsSidebar(
            new RelayCommand(static _ => { }),
            () => agent,
            dialogs,
            pickIcon: (_, _, _) => Task.FromResult(choice),
            profiles: () => agent);
        await sidebar.RefreshAsync(cancellation);
        Assert.Equal(LucideIconKind.Folder, sidebar.Groups[0].Icon);
        Assert.False(sidebar.Groups[0].HasColor);
        Assert.False(sidebar.Groups[0].MoveUpCommand!.CanExecute(null));

        await sidebar.ChangeGroupIconAsync(team.GroupId, cancellation);
        await sidebar.RenameGroupAsync(team.GroupId, cancellation);
        Assert.Equal(("Crew", "layers", "#16A34A"), (agent.Groups[0].Name, agent.Groups[0].IconKey, agent.Groups[0].ColorKey));
        Assert.NotEqual(LucideIconKind.Folder, sidebar.Groups[0].Icon);
        Assert.True(sidebar.Groups[0].HasColor);

        await sidebar.MoveGroupAsync(team.GroupId, 1, cancellation);
        Assert.Equal(["Lab", "Crew", Ui.Connections.Ungrouped], sidebar.Groups.Select(static g => g.Name));

        // Choosing the defaults clears both back to a plain folder.
        choice = new IconChoice(true, null, null);
        await sidebar.ChangeGroupIconAsync(team.GroupId, cancellation);
        Assert.Equal(LucideIconKind.Folder, sidebar.Groups[1].Icon);
        Assert.False(sidebar.Groups[1].HasColor);

        dialogs.Confirm = DialogChoice.No;
        await sidebar.RemoveGroupAsync(team.GroupId, cancellation);
        Assert.Equal(2, agent.Groups.Count);

        dialogs.Confirm = DialogChoice.Yes;
        await sidebar.RemoveGroupAsync(team.GroupId, cancellation);
        Assert.Equal(["Lab", Ui.Connections.Ungrouped], sidebar.Groups.Select(static g => g.Name));
        Assert.Equal(["Studio Assets"], sidebar.Groups[1].Connections.Select(static r => r.Name));
    }

    /// <summary>
    /// Every connection reaches the panel as 1.x's card: a tile in its colour, and a line saying
    /// what it is. The group is left off that line inside the group, whose heading says it.
    /// </summary>
    [AvaloniaFact]
    public async Task EveryConnectionIsDrawnWithItsTile()
    {
        var agent = new GroupAgent(Summary("Scratch"));
        var team = agent.AddGroup("Team");
        agent.File(Summary("Studio Assets"), team.GroupId);
        agent.File(Summary("build-box", provider: StorageConnectionProvider.Ssh, client: true), team.GroupId);
        var window = await PanelAsync(agent);

        var tiles = window.GetVisualDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("icon-tile"))
            .ToArray();
        var rows = tiles.Select(static tile => (ConnectionRowModel)tile.DataContext!).ToArray();

        Assert.Equal(3, tiles.Length);
        Assert.All(tiles, static tile => Assert.IsType<global::Avalonia.Media.SolidColorBrush>(tile.Background));
        Assert.Single(rows, static row => row.IsClient);
        // Never tested, so the line does not say "Not tested"; and it sits under Team's heading,
        // so it does not say Team either.
        Assert.Equal(
            ConnectionProviderCatalog.Get(StorageProviderKind.Ssh).DisplayName,
            rows.Single(static row => row.IsClient).Subtitle);
    }

    /// <summary>
    /// A new connection starts in the group open in the panel, and the editor's Group field lists
    /// the agent's groups in the panel's order, Ungrouped, and New group, which makes one there.
    /// </summary>
    [AvaloniaFact]
    public async Task ANewConnectionStartsInTheGroupOpenInThePanelAndCanStartANewOne()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var agent = new GroupAgent(Summary("Scratch"));
        agent.AddGroup("Team");
        var lab = agent.AddGroup("Lab");
        var sidebar = Sidebar(agent);
        await sidebar.RefreshAsync(cancellation);
        Assert.Null(sidebar.SuggestedGroupId);

        var opened = sidebar.Groups.Single(static g => g.Name == "Lab");
        opened.IsExpanded = false;
        opened.IsExpanded = true;
        Assert.Equal(lab.GroupId, sidebar.SuggestedGroupId);

        // A selected card wins: its group, here none.
        sidebar.Select(sidebar.Groups.Single(static g => g.IsUngrouped).Connections[0]);
        Assert.Null(sidebar.SuggestedGroupId);
        sidebar.Select(null);

        var manager = new ConnectionManagerModel(
            () => new ConnectionManagerController(agent, new NoVault()),
            dialogs: new AnswerDialogs("Clients"));
        await manager.OpenAsync(null, StorageProviderKind.Local, sidebar.SuggestedGroupId, cancellation);
        var editor = manager.Editor;
        Assert.Equal(
            ["Team", "Lab", Ui.Connections.Ungrouped, Ui.Connections.NewGroupChoice],
            editor.GroupChoices.Select(static c => c.Label));
        Assert.Equal("Lab", editor.SelectedGroup?.Label);

        await editor.CreateGroupAsync(cancellation);
        Assert.Equal("Clients", editor.SelectedGroup?.Label);
        Assert.Equal(["Team", "Lab", "Clients"], agent.Groups.Select(static g => g.Name));

        foreach (var field in editor.Sections.SelectMany(static s => s.Fields))
        {
            if (field.Required && field.Value.Trim().Length == 0) field.Value = "sample";
        }

        var savedId = Guid.Empty;
        editor.Saved += (_, id) => savedId = id;
        await editor.SaveAsync(cancellation);
        Assert.Equal(agent.Groups[2].GroupId, agent.LastDraft?.Metadata.GroupId);

        // Opened again for editing with the window already up, as the app does: the drop-down is
        // live while the connection loads before the groups, and it used to write Ungrouped back.
        var again = new ConnectionManagerModel(() => new ConnectionManagerController(agent, new NoVault()));
        var window = new ConnectionManagerWindow { DataContext = again };
        window.Show();
        // In two steps with the window drawn between them, as the agent's answers arrive in the
        // app: the connection first, then the groups.
        await again.Editor.OpenAsync(savedId, cancellation);
        window.UpdateLayout();
        await again.Editor.LoadGroupsAsync(cancellation);
        window.UpdateLayout();
        Assert.Equal("Clients", again.Editor.SelectedGroup?.Label);
        Assert.Equal(agent.Groups[2].GroupId, again.Editor.GroupId);
        var shown = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(box => box.IsEffectivelyVisible && box.SelectedItem is ConnectionGroupChoice);
        Assert.Equal("Clients", (shown.SelectedItem as ConnectionGroupChoice)?.Label);
        window.Close();
    }

    /// <summary>
    /// Photographs the panel and the editor's General tab in both appearances, for a human to look at.
    /// </summary>
    /// <remarks>
    /// The panel with Favorites over groups that have an icon, a colour, both or neither, an empty
    /// group, and Ungrouped; narrow, where the details' actions are icons, and wide, where they are
    /// labelled. Set STORAGEHUB_SHOT_DIR to keep the files.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThePanelCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));

        // Studio Assets is a favourite, as in ui-reference 09, so Favorites is over the groups.
        // Lab SFTP sync2 is one too, and first, so the selected card is one whose name has to
        // share its line with edit and delete.
        var agent = new GroupAgent(Summary("Site Backups"), Summary("Old NAS"));
        var team = agent.AddGroup("Team", "layers", "#2563EB");
        var lab = agent.AddGroup("Lab", colorKey: "#16A34A");
        agent.AddGroup("Archive");
        var clients = agent.AddGroup("Clients", "server");
        agent.File(Summary("Studio Assets") with { IsFavorite = true }, team.GroupId);
        agent.File(Summary("Renders"), team.GroupId);
        agent.File(Summary("build-box", provider: StorageConnectionProvider.Ssh, client: true), clients.GroupId);
        agent.File(Summary("Lab SFTP sync2", provider: StorageConnectionProvider.Sftp) with { IsFavorite = true }, lab.GroupId);
        var window = await PanelAsync(agent, height: 1180);

        // With one card selected, so the photograph shows its border, its edit and delete, and a
        // details panel with something in it.
        var sidebar = (ConnectionsSidebar)((ConnectionsPanelView)window.Content!).DataContext!;
        sidebar.EditConnection = static (_, _) => { };
        sidebar.DeleteConnection = static _ => Task.FromResult(false);
        sidebar.Select(sidebar.Favorites!.Connections.First(static row => row.Name == "Lab SFTP sync2"));
        window.UpdateLayout();

        // The empty group says so on one line, and the details' actions drop their labels together
        // where the panel is too narrow for them.
        var empty = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(static text => text.Classes.Contains("group-empty") && text.IsEffectivelyVisible);
        Assert.Equal(Ui.Connections.GroupEmpty, empty.Text);
        var actions = window.GetVisualDescendants().OfType<DockPanel>()
            .Single(static panel => panel.Classes.Contains("detail-actions"));
        Assert.Contains("compact", actions.Classes);

        // The labels they dropped are their tooltips, which a style hiding the labels once hid too.
        var edit = actions.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(ToolTip.GetTip(button), sidebar.EditLabel));
        ToolTip.SetIsOpen(edit, true);
        window.UpdateLayout();
        // The ToolTip is held in an internal attached property and only points up at the button,
        // which is exactly why a "Button TextBlock" style reaches it; read it the same way.
        var held = (AvaloniaProperty)typeof(ToolTip)
            .GetField("ToolTipProperty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        var tip = Assert.IsType<ToolTip>(edit.GetValue(held));
        tip.UpdateLayout();
        var tipText = tip.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == sidebar.EditLabel);
        Assert.True(tipText.IsVisible);
        ToolTip.SetIsOpen(edit, false);

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        var theme = dark ? "dark" : "light";
        Save(frame!, $"connections-{theme}.png");

        var wide = Show(sidebar, 340, 1180);
        var labelled = wide.GetVisualDescendants().OfType<DockPanel>()
            .Single(static panel => panel.Classes.Contains("detail-actions"));
        Assert.DoesNotContain("compact", labelled.Classes);
        Save(wide.CaptureRenderedFrame()!, $"connections-wide-{theme}.png");

        // And the editor's General tab, on a new connection in the group the panel suggests.
        sidebar.Select(null);
        var opened = sidebar.Groups.Single(static g => g.Name == "Lab");
        opened.IsExpanded = false;
        opened.IsExpanded = true;
        var manager = new ConnectionManagerModel(() => new ConnectionManagerController(agent, new NoVault()));
        await manager.OpenAsync(null, StorageProviderKind.S3, sidebar.SuggestedGroupId, TestContext.Current.CancellationToken);
        var editor = new ConnectionManagerWindow { DataContext = manager };
        editor.Show();
        editor.UpdateLayout();
        Assert.Equal("Lab", manager.Editor.SelectedGroup?.Label);
        Save(editor.CaptureRenderedFrame()!, $"connection-editor-group-{theme}.png");

        void Save(WriteableBitmap shot, string name)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);
            using var stream = File.Create(Path.Combine(directory, name));
            shot.Save(stream, new PngBitmapEncoderOptions());
        }
    }

    /// <summary>
    /// Favorites, every group and Ungrouped are drawn whether or not anything is in them, in that
    /// order, each saying so; Favorites and Ungrouped offer no menu, and an empty Ungrouped is
    /// still somewhere to drop a connection to take it out of its group.
    /// </summary>
    [AvaloniaFact]
    public async Task EveryGroupIsDrawnEvenWithNothingInIt()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var studio = Summary("Studio Assets");
        var agent = new GroupAgent();
        var team = agent.AddGroup("Team");
        agent.AddGroup("Archive");
        agent.File(studio, team.GroupId);
        var sidebar = Sidebar(agent);
        await sidebar.RefreshAsync(cancellation);

        Assert.True(sidebar.HasGroups);
        Assert.True(sidebar.Favorites!.IsFavorites);
        Assert.True(sidebar.Favorites.ShowsEmptyLine);
        Assert.Equal(Ui.Shell.NoFavoriteConnections, sidebar.Favorites.EmptyLabel);
        Assert.Equal(Ui.Shell.FavoritesEmptyHint, sidebar.Favorites.EmptyHint);
        Assert.False(sidebar.Favorites.HasMenu);
        Assert.Equal(["Team", "Archive", Ui.Connections.Ungrouped], sidebar.Groups.Select(static g => g.Name));
        Assert.True(sidebar.Groups[1].ShowsEmptyLine);
        var ungrouped = sidebar.Groups[2];
        Assert.True(ungrouped.IsUngrouped);
        Assert.True(ungrouped.ShowsEmptyLine);
        Assert.Equal(Ui.Connections.GroupEmpty, ungrouped.EmptyLabel);
        Assert.False(ungrouped.HasMenu);
        Assert.Null(ungrouped.RenameCommand);
        Assert.Null(ungrouped.RemoveCommand);

        // Selection is untouched by the empty groups, and a drop on the empty Ungrouped files out.
        sidebar.Select(sidebar.Groups[0].Connections[0]);
        Assert.Equal(studio.ConnectionId, sidebar.Selected?.Id);
        await sidebar.MoveToGroupAsync(studio.ConnectionId, ungrouped, cancellation);
        Assert.Equal(["Studio Assets"], sidebar.Groups[2].Connections.Select(static r => r.Name));
        Assert.True(sidebar.Groups[0].ShowsEmptyLine);
        Assert.Equal(studio.ConnectionId, sidebar.Selected?.Id);

        // Not even an agent with no connections at all empties the panel of its groups.
        var bare = Sidebar(new GroupAgent());
        await bare.RefreshAsync(cancellation);
        Assert.True(bare.IsEmpty);
        Assert.True(bare.HasGroups);
        Assert.Equal([Ui.Connections.Ungrouped], bare.Groups.Select(static g => g.Name));
    }

    /// <summary>
    /// "Hide empty groups" leaves out Favorites, a group somebody made and Ungrouped while each has
    /// nothing in it, and brings them back when they have something or the setting goes off.
    /// </summary>
    [AvaloniaFact]
    public async Task HideEmptyGroupsLeavesOutTheOnesWithNothingInThem()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var agent = new GroupAgent();
        var team = agent.AddGroup("Team");
        agent.AddGroup("Archive");
        agent.File(Summary("Studio Assets"), team.GroupId);
        var sidebar = Sidebar(agent);
        sidebar.HideEmptyGroups = true;
        await sidebar.RefreshAsync(cancellation);

        Assert.Null(sidebar.Favorites);
        Assert.Equal(["Team"], sidebar.Groups.Select(static g => g.Name));
        Assert.True(sidebar.HasGroups);

        // Something in Favorites and Ungrouped brings each back, Favorites first and Ungrouped last.
        agent.Connections.Add(Summary("Scratch") with { IsFavorite = true });
        await sidebar.RefreshAsync(cancellation);
        Assert.Equal(["Scratch"], sidebar.Favorites!.Connections.Select(static r => r.Name));
        Assert.Equal(["Team", Ui.Connections.Ungrouped], sidebar.Groups.Select(static g => g.Name));

        // Turned off, it redraws at once with the empty one back.
        sidebar.HideEmptyGroups = false;
        Assert.Equal(["Team", "Archive", Ui.Connections.Ungrouped], sidebar.Groups.Select(static g => g.Name));

        // With nothing at all and the setting on, the panel is bare again, as it used to be.
        var bare = Sidebar(new GroupAgent());
        bare.HideEmptyGroups = true;
        await bare.RefreshAsync(cancellation);
        Assert.False(bare.HasGroups);
        Assert.Empty(bare.Groups);
        Assert.Null(bare.Favorites);
    }

    /// <summary>A search keeps the empty groups, which say so, unless they are being hidden.</summary>
    [AvaloniaFact]
    public async Task ASearchKeepsTheGroupsItFoundNothingIn()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var agent = new GroupAgent(Summary("Scratch"));
        agent.File(Summary("Studio Assets"), agent.AddGroup("Team").GroupId);
        agent.AddGroup("Archive");
        var sidebar = Sidebar(agent);
        await sidebar.RefreshAsync(cancellation);

        sidebar.Search = "Studio";
        Assert.NotNull(sidebar.Favorites);
        Assert.Equal(["Team", "Archive", Ui.Connections.Ungrouped], sidebar.Groups.Select(static g => g.Name));
        Assert.True(sidebar.Groups[1].ShowsEmptyLine);
        Assert.True(sidebar.Groups[2].ShowsEmptyLine);
        Assert.Single(sidebar.Groups[0].Connections);

        // Nothing matching anywhere still says so, above the groups.
        sidebar.Search = "zzz";
        Assert.True(sidebar.IsEmpty);
        Assert.Equal(Ui.Connections.NoMatches, sidebar.Status);
        Assert.Equal(3, sidebar.Groups.Count);

        sidebar.HideEmptyGroups = true;
        Assert.Empty(sidebar.Groups);
        Assert.Null(sidebar.Favorites);
        sidebar.Search = "Studio";
        Assert.Equal(["Team"], sidebar.Groups.Select(static g => g.Name));
    }

    /// <summary>
    /// Settings' "Connection groups at startup": as they were left opens the ones closed last time
    /// closed and saves each change, and all collapsed closes every group once the agent has listed them.
    /// </summary>
    [AvaloniaFact]
    public async Task TheGroupsOpenAtStartupAsSettingsSays()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var agent = new GroupAgent();
        var team = agent.AddGroup("Team");
        var lab = agent.AddGroup("Lab");
        agent.File(Summary("Studio Assets") with { IsFavorite = true }, team.GroupId);
        bool Open(ConnectionsSidebar panel, string name) => panel.Groups.Single(g => g.Name == name).IsExpanded;

        var saved = DesktopUpdatePreferences.Defaults with
        {
            ConnectionGroupsAtStartup = ConnectionGroupsAtStartup.Remember,
            CollapsedConnectionGroups = [lab.GroupId],
            FavoritesCollapsed = true
        };
        var remembered = Sidebar(agent);
        remembered.RestoreGroups(saved);
        remembered.Persist = change => saved = change(saved);
        await remembered.RefreshAsync(cancellation);
        Assert.False(remembered.Favorites!.IsExpanded);
        Assert.True(Open(remembered, "Team"));
        Assert.False(Open(remembered, "Lab"));

        remembered.Groups.Single(static g => g.Name == "Team").IsExpanded = false;
        remembered.Favorites!.IsExpanded = true;
        Assert.Equal(new HashSet<Guid> { team.GroupId, lab.GroupId }, saved.CollapsedConnectionGroups!.ToHashSet());
        Assert.False(saved.FavoritesCollapsed);

        var collapsed = Sidebar(agent);
        collapsed.RestoreGroups(DesktopUpdatePreferences.Defaults with
        {
            ConnectionGroupsAtStartup = ConnectionGroupsAtStartup.Collapsed
        });
        await collapsed.RefreshAsync(cancellation);
        Assert.False(collapsed.Favorites!.IsExpanded);
        Assert.All(collapsed.Groups, static group => Assert.False(group.IsExpanded));

        // Only the first listing: a group opened afterwards stays open when the agent lists again.
        collapsed.Groups.Single(static g => g.Name == "Team").IsExpanded = true;
        await collapsed.RefreshAsync(cancellation);
        Assert.True(Open(collapsed, "Team"));
    }

    /// <summary>Photographs the panel with empty Favorites, an empty group and an empty Ungrouped.</summary>
    /// <remarks>Set STORAGEHUB_SHOT_DIR to keep connections-empty-groups-{dark,light}.png.</remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyGroupsCanBePhotographed(bool dark)
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: dark));
        var agent = new GroupAgent();
        agent.File(Summary("Studio Assets"), agent.AddGroup("Team", "layers", "#2563EB").GroupId);
        agent.AddGroup("Archive");
        var window = await PanelAsync(agent, height: 760);

        var empties = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(static text => text.Classes.Contains("group-empty") && text.IsEffectivelyVisible)
            .Select(static text => text.Text!)
            .ToArray();
        Assert.Equal([Ui.Shell.NoFavoriteConnections, Ui.Connections.GroupEmpty, Ui.Connections.GroupEmpty], empties);

        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"connections-empty-groups-{(dark ? "dark" : "light")}.png"));
        window.CaptureRenderedFrame()!.Save(stream, new PngBitmapEncoderOptions());
    }

    /// <summary>
    /// A list longer than the panel scrolls beside the cards, not over them: no card, and no
    /// group's menu button, runs under the scroll bar, which is slimmer than the theme's and clear
    /// of their edge.
    /// </summary>
    /// <remarks>Set STORAGEHUB_SHOT_DIR to keep connections-scrolled.png.</remarks>
    [AvaloniaFact]
    public async Task TheScrollBarDoesNotCoverTheCards()
    {
        var agent = new GroupAgent();
        foreach (var name in new[] { "Team", "Lab", "Archive" })
        {
            var group = agent.AddGroup(name);
            for (var i = 1; i <= 3; i++) agent.File(Summary($"{name} {i}"), group.GroupId);
        }
        var window = await PanelAsync(agent, height: 520);

        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(static viewer => viewer.Name == "PART_Connections");
        var bar = scroller.GetVisualDescendants().OfType<ScrollBar>()
            .Single(static bar => bar.Orientation == Orientation.Vertical);
        Assert.True(bar.IsEffectivelyVisible);
        var barLeft = bar.TranslatePoint(default, window)!.Value.X;
        var size = (double)window.FindResource("SidebarScrollBarSize")!;
        var gap = ((Thickness)window.FindResource("SidebarScrollBarGap")!).Left;
        Assert.Equal(size, bar.Bounds.Width);
        Assert.Equal(size, bar.GetVisualDescendants().OfType<Thumb>().Single().Bounds.Width);

        var cards = scroller.GetVisualDescendants().OfType<Control>()
            .Where(static control => control.Classes.Contains("connection-row") || control is Button { Flyout: not null })
            .Where(static control => control.IsEffectivelyVisible)
            .ToArray();
        Assert.NotEmpty(cards);
        Assert.All(cards, card => Assert.True(
            card.TranslatePoint(new Point(card.Bounds.Width, 0), window)!.Value.X + gap <= barLeft,
            $"{card.GetType().Name} reaches under, or up against, the scroll bar"));

        if (Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var stream = File.Create(Path.Combine(directory, "connections-scrolled.png"));
            window.CaptureRenderedFrame()!.Save(stream, new PngBitmapEncoderOptions());
        }
    }

    /// <summary>
    /// Clicking a card selects it and fills the details panel, as 1.x's sidebar did (ui-reference 09),
    /// with the group it is in among its facts.
    /// </summary>
    [AvaloniaFact]
    public async Task SelectingACardFillsTheDetailsAndKeepsItAcrossASearch()
    {
        var agent = new GroupAgent(Summary("Scratch"));
        agent.File(Summary("Studio Assets"), agent.AddGroup("Team").GroupId);
        var sidebar = Sidebar(agent);
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);
        var row = sidebar.Groups[0].Connections[0];

        sidebar.Select(row);

        Assert.True(row.IsSelected);
        Assert.Contains(new ConnectionDetailRow(Ui.Connections.FieldGroup, "Team"), sidebar.Details);

        // A search that still shows the card keeps it selected, on the new row object; the groups
        // it found nothing in stay, empty, rather than vanishing.
        sidebar.Search = "Studio";
        Assert.Equal(row.Id, sidebar.Selected?.Id);
        Assert.True(sidebar.Selected!.IsSelected);
        Assert.Equal(["Team", Ui.Connections.Ungrouped], sidebar.Groups.Select(static g => g.Name));

        // One that hides it lets the selection go, rather than describing something off screen.
        sidebar.Search = "Scratch";
        Assert.False(sidebar.HasSelection);
    }

    /// <summary>Edit and Delete act on the card they are pressed on, or on the selected one.</summary>
    [AvaloniaFact]
    public async Task EditAndDeleteNameTheConnectionTheyAreFor()
    {
        var sidebar = Sidebar(new GroupAgent(Summary("Studio Assets")));
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);
        var row = sidebar.Groups[0].Connections[0];
        Guid? edited = null;
        Guid? deleted = null;
        sidebar.EditConnection = (id, _) => edited = id;
        sidebar.DeleteConnection = id =>
        {
            deleted = id;
            return Task.FromResult(false);
        };

        sidebar.EditSelectedCommand.Execute(row);
        sidebar.Select(row);
        sidebar.DeleteSelectedCommand.Execute(null);

        Assert.Equal(row.Id, edited);
        Assert.Equal(row.Id, deleted);
    }

    /// <summary>
    /// A card says when its connection is retrying or lost for as long as that stands, and
    /// "connected again" for a while after; the panel goes back to how it was on its own.
    /// </summary>
    /// <remarks>Set STORAGEHUB_SHOT_DIR to keep connections-link.png.</remarks>
    [AvaloniaFact]
    public async Task ACardSaysWhenItsConnectionIsRetryingLostOrBack()
    {
        ColorSchemeApplier.Apply(
            global::Avalonia.Application.Current!,
            ColorSchemeCatalog.Resolve(id: null, preferDark: false));
        var cancellation = TestContext.Current.CancellationToken;
        var noon = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var agent = new GroupAgent(Summary("Studio Assets"), Summary("Renders"), Summary("Site Backups"), Summary("Old NAS"));
        Link(agent, "Studio Assets", ConnectionLinkEventKind.Lost, noon.AddMinutes(-9), code: "storage.timeout");
        Link(agent, "Renders", ConnectionLinkEventKind.Retrying, noon.AddSeconds(-20), attempt: 2);
        Link(agent, "Site Backups", ConnectionLinkEventKind.Back, noon.AddSeconds(-30));
        var sidebar = Sidebar(agent);
        sidebar.Clock = () => noon;
        await sidebar.RefreshAsync(cancellation);
        await sidebar.RefreshLinksAsync(cancellation);
        Dispatcher.UIThread.RunJobs();

        ConnectionRowModel Row(string name) => sidebar.Groups.SelectMany(static g => g.Connections).Single(r => r.Name == name);
        Assert.Equal(Ui.Connections.LinkLost, Row("Studio Assets").LinkText);
        Assert.True(Row("Studio Assets").LinkIsDanger);
        Assert.Equal(Ui.Format(Ui.Connections.LinkRetryingAttemptFormat, 2), Row("Renders").LinkText);
        Assert.True(Row("Renders").LinkIsWarning);
        Assert.Equal(Ui.Connections.LinkBack, Row("Site Backups").LinkText);
        Assert.True(Row("Site Backups").LinkIsSuccess);
        Assert.False(Row("Old NAS").HasLink);

        var window = Show(sidebar, 260, 900);
        var lines = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(static text => text.IsEffectivelyVisible)
            .Select(static text => text.Text!)
            .ToArray();
        Assert.Contains(Ui.Connections.LinkLost, lines);
        Assert.Contains(Ui.Connections.LinkBack, lines);
        if (Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var stream = File.Create(Path.Combine(directory, "connections-link.png"));
            window.CaptureRenderedFrame()!.Save(stream, new PngBitmapEncoderOptions());
        }

        // A rebuild keeps them, and "connected again" goes on its own once it is old news.
        await sidebar.RefreshAsync(cancellation);
        Assert.True(Row("Studio Assets").HasLink);
        sidebar.Clock = () => noon.AddMinutes(3);
        await sidebar.RefreshLinksAsync(cancellation);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Row("Site Backups").HasLink);
        Assert.True(Row("Renders").HasLink);

        // The connection worked again: its card says so.
        Link(agent, "Studio Assets", ConnectionLinkEventKind.Back, noon.AddMinutes(3));
        await sidebar.RefreshLinksAsync(cancellation);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Ui.Connections.LinkBack, Row("Studio Assets").LinkText);
    }

    private static void Link(
        GroupAgent agent,
        string name,
        ConnectionLinkEventKind kind,
        DateTimeOffset at,
        int? attempt = null,
        string? code = null)
    {
        var connection = agent.Connections.Single(c => c.DisplayName == name);
        agent.Links.RemoveAll(change => change.ConnectionId == connection.ConnectionId);
        agent.Links.Insert(0, new ConnectionLinkEvent(agent.Links.Count + 1, connection.ConnectionId, name, kind, at, attempt, code));
    }

    /// <summary>
    /// The panel on its own, with connections in it, 240 wide: narrower than it opens, which is
    /// where a selected card's name has to make room for edit and delete.
    /// </summary>
    private static async Task<Window> PanelAsync(GroupAgent agent, double height = 620)
    {
        var sidebar = Sidebar(agent);
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);
        return Show(sidebar, 240, height);
    }

    private static Window Show(ConnectionsSidebar sidebar, double width, double height)
    {
        var window = new Window
        {
            Content = new ConnectionsPanelView { DataContext = sidebar },
            Width = width,
            Height = height
        };
        window.Show();
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
        window.UpdateLayout();
        window.UpdateLayout();
        return window;
    }

    /// <summary>A sidebar over an agent that keeps groups, with no old arrangement to bring in.</summary>
    private static ConnectionsSidebar Sidebar(GroupAgent agent) => new(
        new RelayCommand(static _ => { }),
        () => agent,
        profiles: () => agent);

    /// <summary>Answers every prompt with the same text, and every question as told.</summary>
    private sealed class AnswerDialogs(string answer) : IDialogService
    {
        internal DialogChoice Confirm { get; set; } = DialogChoice.Yes;

        public Task ShowAsync(DialogRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<DialogChoice> ConfirmAsync(DialogRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Confirm);

        public Task<string?> PromptAsync(DialogPromptRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(answer);
    }

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

    private sealed class NoVault : IRemoteSecretVaultClient
    {
        public Task<SecretVaultResponse> EnrollAsync(
            SecretMaterialPurpose purpose,
            ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SecretVaultResponse> UpdateAsync(
            string reference,
            SecretMaterialPurpose purpose,
            ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SecretVaultResponse> DeleteAsync(
            string reference,
            SecretMaterialPurpose purpose,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// The agent's connections and groups, kept as its database keeps them: a connection names its
    /// group, every change to that moves it to a new version, removing a group sends its
    /// connections to Ungrouped, and the old arrangement is brought in once.
    /// </summary>
    private sealed class GroupAgent(params ConnectionSummary[] connections) : IRemoteStorageAgentClient, IRemoteConnectionProfileClient
    {
        private bool _imported;

        internal List<ConnectionSummary> Connections { get; } = [.. connections];

        internal List<ConnectionGroupDocument> Groups { get; } = [];

        internal int ImportsApplied { get; private set; }

        internal ConnectionProfileDraft? LastDraft { get; private set; }

        /// <summary>Each connection's latest link change, newest first.</summary>
        internal List<ConnectionLinkEvent> Links { get; } = [];

        public Task<ConnectionLinkEventsResponse> ListLinkEventsAsync(
            ConnectionLinkEventsRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionLinkEventsResponse(
                request.ContractVersion, [.. Links.Take(request.Limit)], [.. Links]));

        internal ConnectionGroupDocument AddGroup(string name, string? iconKey = null, string? colorKey = null)
        {
            var group = new ConnectionGroupDocument(
                Guid.NewGuid(), name, Groups.Count, iconKey, colorKey, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            Groups.Add(group);
            return group;
        }

        internal void File(ConnectionSummary connection, Guid groupId) => Connections.Add(connection with { GroupId = groupId });

        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionListResponse(StorageIpcContract.CurrentVersion, [.. Connections]));

        public Task<ConnectionGroupListResponse> ListGroupsAsync(
            ConnectionGroupListRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionGroupListResponse(request.ContractVersion, [.. Ordered()]));

        public Task<ConnectionGroupWriteResponse> CreateGroupAsync(
            ConnectionGroupCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Groups.Any(group => string.Equals(group.Name, request.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return Answer(ConnectionGroupWriteStatus.NameConflict);
            }

            var created = AddGroup(request.Name, request.IconKey, request.ColorKey);
            return Answer(ConnectionGroupWriteStatus.Succeeded, created);
        }

        public Task<ConnectionGroupWriteResponse> UpdateGroupAsync(
            ConnectionGroupUpdateRequest request,
            CancellationToken cancellationToken = default)
        {
            var index = Groups.FindIndex(group => group.GroupId == request.GroupId);
            Groups[index] = Groups[index] with
            {
                Name = request.Name,
                IconKey = request.IconKey,
                ColorKey = request.ColorKey,
                Version = Groups[index].Version + 1
            };
            return Answer(ConnectionGroupWriteStatus.Succeeded, Groups[index]);
        }

        public Task<ConnectionGroupWriteResponse> MoveGroupAsync(
            ConnectionGroupMoveRequest request,
            CancellationToken cancellationToken = default)
        {
            var ordered = Ordered().ToList();
            var moved = ordered.Single(group => group.GroupId == request.GroupId);
            ordered.Remove(moved);
            ordered.Insert(request.Index, moved);
            Groups.Clear();
            Groups.AddRange(ordered.Select(static (group, index) => group with { SortOrder = index }));
            return Answer(ConnectionGroupWriteStatus.Succeeded);
        }

        public Task<ConnectionGroupWriteResponse> DeleteGroupAsync(
            ConnectionGroupDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            Groups.RemoveAll(group => group.GroupId == request.GroupId);
            for (var index = 0; index < Connections.Count; index++)
            {
                if (Connections[index].GroupId != request.GroupId) continue;
                Connections[index] = Connections[index] with { GroupId = null, Version = Connections[index].Version + 1 };
            }

            return Answer(ConnectionGroupWriteStatus.Succeeded);
        }

        public Task<ConnectionGroupWriteResponse> AssignGroupAsync(
            ConnectionGroupAssignRequest request,
            CancellationToken cancellationToken = default)
        {
            var index = Connections.FindIndex(connection => connection.ConnectionId == request.ConnectionId);
            Connections[index] = Connections[index] with { GroupId = request.GroupId, Version = Connections[index].Version + 1 };
            return Task.FromResult(new ConnectionGroupWriteResponse(
                request.ContractVersion, ConnectionGroupWriteStatus.Succeeded, Groups: [.. Ordered()],
                ConnectionVersion: Connections[index].Version));
        }

        public Task<ConnectionGroupWriteResponse> ImportGroupsAsync(
            ConnectionGroupImportRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_imported) return Answer(ConnectionGroupWriteStatus.AlreadyImported);
            _imported = true;
            ImportsApplied++;
            foreach (var entry in request.Groups)
            {
                var group = Groups.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, entry.Name, StringComparison.OrdinalIgnoreCase))
                    ?? AddGroup(entry.Name, entry.IconKey, entry.ColorKey);
                for (var index = 0; index < Connections.Count; index++)
                {
                    if (!entry.Members.Contains(Connections[index].ConnectionId) || Connections[index].GroupId is not null) continue;
                    Connections[index] = Connections[index] with { GroupId = group.GroupId, Version = Connections[index].Version + 1 };
                }
            }

            return Answer(ConnectionGroupWriteStatus.Succeeded);
        }

        public Task<ConnectionProfileWriteResponse> CreateAsync(
            ConnectionProfileCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            LastDraft = request.Draft;
            var id = Guid.NewGuid();
            _created = new ConnectionProfileDocument(id, 1, request.Draft, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            return Task.FromResult(new ConnectionProfileWriteResponse(
                request.ContractVersion,
                ConnectionProfileWriteStatus.Succeeded,
                _created,
                ActualVersion: 1));
        }

        /// <summary>The connection made by the last create, which is the one that can be read back.</summary>
        private ConnectionProfileDocument? _created;

        public Task<ConnectionProfileGetResponse> GetAsync(
            ConnectionProfileGetRequest request,
            CancellationToken cancellationToken = default) =>
            _created is { } created && created.ConnectionId == request.ConnectionId
                ? Task.FromResult(new ConnectionProfileGetResponse(request.ContractVersion, created))
                : throw new NotSupportedException();

        public Task<ConnectionProfileWriteResponse> UpdateAsync(
            ConnectionProfileUpdateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConnectionProfileWriteResponse> DeleteAsync(
            ConnectionProfileDeleteRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConnectionTrustGetResponse> GetTrustAsync(
            ConnectionTrustGetRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConnectionTrustMutationResponse> DecideTrustAsync(
            ConnectionTrustDecisionRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConnectionTrustMutationResponse> RolloverTrustAsync(
            ConnectionTrustRolloverRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private IEnumerable<ConnectionGroupDocument> Ordered() => Groups.OrderBy(static group => group.SortOrder);

        private Task<ConnectionGroupWriteResponse> Answer(
            ConnectionGroupWriteStatus status,
            ConnectionGroupDocument? group = null) =>
            Task.FromResult(new ConnectionGroupWriteResponse(
                ConnectionProfileIpcContract.CurrentVersion,
                status,
                group,
                [.. Ordered()],
                Failure: status == ConnectionGroupWriteStatus.Succeeded || status == ConnectionGroupWriteStatus.AlreadyImported
                    ? null
                    : new StorageIpcFailure("test.refused", StorageIpcFailureCategory.Conflict, "Refused.", false)));
    }
}
