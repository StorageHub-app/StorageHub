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
        Assert.Equal(["Lab", "Crew"], sidebar.Groups.Select(static g => g.Name));

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
    /// what it is, and which group it is in.
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
        Assert.StartsWith(
            ConnectionProviderCatalog.Get(StorageProviderKind.Ssh).DisplayName + " · Team",
            rows.Single(static row => row.IsClient).Subtitle,
            StringComparison.Ordinal);
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

        await editor.SaveAsync(cancellation);
        Assert.Equal(agent.Groups[2].GroupId, agent.LastDraft?.Metadata.GroupId);
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

        // A search that still shows the card keeps it selected, on the new row object, and shows
        // only the groups it found something in.
        sidebar.Search = "Studio";
        Assert.Equal(row.Id, sidebar.Selected?.Id);
        Assert.True(sidebar.Selected!.IsSelected);
        Assert.Equal(["Team"], sidebar.Groups.Select(static g => g.Name));

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
            return Task.FromResult(new ConnectionProfileWriteResponse(
                request.ContractVersion,
                ConnectionProfileWriteStatus.Succeeded,
                new ConnectionProfileDocument(id, 1, request.Draft, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                ActualVersion: 1));
        }

        public Task<ConnectionProfileGetResponse> GetAsync(
            ConnectionProfileGetRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

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
