using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;
using static StorageHub.Desktop.Tests.WorkspaceFakes;
using Page = StorageHub.Desktop.Tests.WorkspaceFakes.Page;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The View, Go and Connections entries 1.4 declared and never wired, each invoked as the menu
/// invokes it and checked for what it does.
/// </summary>
public class ViewGoConnectionsCommandTests
{
    private static readonly string[] Wired =
    [
        UiCommandIds.ViewDirectoryTree, UiCommandIds.ViewTransferQueue, UiCommandIds.ViewSessionLog,
        UiCommandIds.ViewHiddenFiles, UiCommandIds.ViewTheme, UiCommandIds.GoHome, UiCommandIds.GoHistory,
        UiCommandIds.GoFavorites, UiCommandIds.ConnectionsQuickConnect, UiCommandIds.ConnectionsReconnect,
        UiCommandIds.ConnectionsDisconnect, UiCommandIds.ConnectionsTestConnection,
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A shell over a fake agent, with one workspace of two panes showing.</summary>
    private sealed class Shell : IAsyncDisposable
    {
        internal Shell(ConnectionSummary[] connections, bool withWorkspace = true)
        {
            Agent = new FakeBrowsingAgent(connections);
            Queue = new TransferQueueModel(static () => new FakeTransferQueue());
            Model = new ShellPreviewModel(new ShellCommandRouter())
            {
                Sidebar = new ConnectionsSidebar(new RelayCommand(static _ => { }), () => Agent),
                Queue = Queue,
                ViewToggles = Toggles,
            };
            if (withWorkspace)
            {
                Workspace = new WorkspaceModel(
                    () => new BrowserPaneModel(Agent),
                    static () => new FakeTransferQueue(),
                    () => Agent,
                    static () => new FakeInspector(),
                    preset: WorkspaceModel.Presets[1]);
                Model.Workspaces.Add(new WorkspaceTab(Workspace));
                Model.SelectedWorkspace = 0;
            }

            ShellViewCommands.Wire(Model, Toggles);
        }

        internal FakeBrowsingAgent Agent { get; }

        internal TransferQueueModel Queue { get; }

        internal ShellViewToggles Toggles { get; } = new();

        internal ShellPreviewModel Model { get; }

        internal WorkspaceModel? Workspace { get; }

        internal BrowserPaneModel Pane => Model.ActivePane()!;

        internal ShellCommand Command(string id) => Model.Router.For(id);

        internal void Run(string id)
        {
            Assert.True(Command(id).CanExecute(null), $"{id} is dimmed.");
            Command(id).Execute(null);
        }

        /// <summary>Points the active pane at a connection and waits for its listing.</summary>
        internal async Task OpenAsync(ConnectionSummary connection)
        {
            await Pane.LoadConnectionsAsync(Cancellation);
            await Pane.OpenConnectionAsync(connection.ConnectionId, Cancellation);
        }

        public async ValueTask DisposeAsync()
        {
            if (Workspace is not null) await Workspace.DisposeAsync();
            await Queue.DisposeAsync();
        }
    }

    /// <summary>Waits for what a command started without awaiting, as a click does.</summary>
    private static async Task Until(Func<bool> done)
    {
        for (var attempt = 0; attempt < 200 && !done(); attempt++)
        {
            await Task.Delay(10, Cancellation);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        Assert.True(done());
    }

    [AvaloniaFact]
    public async Task EveryOneIsOfferedAndHandled()
    {
        await using var shell = new Shell([Summary("Studio Assets")]);

        Assert.All(Wired, id =>
        {
            Assert.True(UiCommandCatalog.IsAvailable(id), $"{id} is left out of the menus.");
            Assert.True(shell.Model.Router.IsHandled(id), $"{id} has no handler.");
        });
    }

    [AvaloniaFact]
    public async Task DirectoryTreeHidesTheActivePanesTreeAndTheWorkspaceRemembersIt()
    {
        var studio = Summary("Studio Assets");
        await using var shell = new Shell([studio]);
        await shell.OpenAsync(studio);
        Assert.True(shell.Toggles.TreeCheck.IsChecked);

        shell.Run(UiCommandIds.ViewDirectoryTree);

        Assert.False(shell.Pane.ShowTree);
        Assert.False(shell.Toggles.TreeCheck.IsChecked);
        Assert.True(shell.Pane.CaptureState().TreeHidden);

        // Only the active pane's.
        Assert.True(shell.Workspace!.Panes.Single(pane => !pane.IsActive).ShowTree);

        shell.Run(UiCommandIds.ViewDirectoryTree);
        Assert.True(shell.Pane.ShowTree);
        Assert.True(shell.Toggles.TreeCheck.IsChecked);
    }

    /// <summary>The tree's column goes with it, so the list takes the room, and comes back as wide.</summary>
    [AvaloniaFact]
    public async Task AHiddenTreeGivesItsWidthToTheList()
    {
        var studio = Summary("Studio Assets");
        var agent = new FakeBrowsingAgent([studio]);
        agent.Listings[(studio.ConnectionId, "")] = new Page([Entry("renders", container: true), Entry("brief.pdf", 2048)]);
        await using var pane = new BrowserPaneModel(agent);
        await pane.LoadConnectionsAsync(Cancellation);
        await pane.OpenConnectionAsync(studio.ConnectionId, Cancellation);
        var view = new BrowserPaneView { DataContext = pane };
        var window = new Window { Content = view, Width = 700, Height = 360 };
        window.Show();

        var listing = view.FindControl<Grid>("PART_Listing")!;
        Assert.Equal(185, listing.ColumnDefinitions[0].Width.Value);

        pane.ShowTree = false;
        window.UpdateLayout();
        Assert.Equal(0, listing.ColumnDefinitions[0].Width.Value);
        Assert.False(view.FindControl<TreeView>("PART_Tree")!.IsVisible);
        Assert.False(view.FindControl<GridSplitter>("PART_TreeSplitter")!.IsVisible);
        Photograph(window, "pane-tree-hidden");

        pane.ShowTree = true;
        Assert.Equal(185, listing.ColumnDefinitions[0].Width.Value);
        Assert.True(view.FindControl<TreeView>("PART_Tree")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task TransferQueueHidesAndShowsTheQueueAndSavesIt()
    {
        await using var shell = new Shell([Summary("Studio Assets")]);
        var saved = DesktopUpdatePreferences.Defaults;
        shell.Toggles.Persist = change => saved = change(saved);
        Assert.True(shell.Toggles.QueueCheck.IsChecked);

        shell.Run(UiCommandIds.ViewTransferQueue);

        Assert.False(shell.Toggles.QueueVisible);
        Assert.False(shell.Toggles.QueueCheck.IsChecked);
        Assert.False(saved.TransferQueueVisible);

        shell.Run(UiCommandIds.ViewTransferQueue);
        Assert.True(shell.Toggles.QueueVisible);
        Assert.True(saved.TransferQueueVisible);
    }

    /// <summary>In the window: the queue and its splitter go, and come back at their height.</summary>
    [AvaloniaFact]
    public void TheWindowHidesTheQueueRow()
    {
        var model = ShellPreview.CreateOnWorkspace();
        var window = new MainWindow { DataContext = model, Width = 1500, Height = 920 };
        window.Show();
        window.UpdateLayout();
        var row = window.FindControl<Grid>("PART_WorkArea")!.RowDefinitions[2];
        var height = row.ActualHeight;
        Assert.True(height > 0);

        model.Router.For(UiCommandIds.ViewTransferQueue).Execute(null);
        window.UpdateLayout();

        Assert.False(window.FindControl<Border>("PART_Queue")!.IsVisible);
        Assert.False(window.FindControl<GridSplitter>("PART_QueueSplitter")!.IsVisible);
        Assert.Equal(0, row.ActualHeight);
        Photograph(window, "shell-queue-hidden");

        model.Router.For(UiCommandIds.ViewTransferQueue).Execute(null);
        window.UpdateLayout();
        Assert.True(window.FindControl<Border>("PART_Queue")!.IsVisible);
        Assert.Equal(height, row.ActualHeight, 0.5);
    }

    [AvaloniaFact]
    public async Task SessionLogOpensTheLogsTabAndShowsTheQueueToDoIt()
    {
        await using var shell = new Shell([Summary("Studio Assets")]);
        var logs = shell.Queue.Tabs.IndexOf(shell.Queue.Tabs.Single(static tab => tab.IsLog));
        shell.Queue.SelectedTab = 2;
        shell.Run(UiCommandIds.ViewTransferQueue);
        Assert.False(shell.Toggles.LogCheck.IsChecked);

        shell.Run(UiCommandIds.ViewSessionLog);

        Assert.True(shell.Toggles.QueueVisible);
        Assert.Equal(logs, shell.Queue.SelectedTab);
        Assert.True(shell.Toggles.LogCheck.IsChecked);

        // And off again goes back to the tab it was opened from, leaving the queue showing.
        shell.Run(UiCommandIds.ViewSessionLog);
        Assert.Equal(2, shell.Queue.SelectedTab);
        Assert.True(shell.Toggles.QueueVisible);
        Assert.False(shell.Toggles.LogCheck.IsChecked);

        // Choosing the tab by hand ticks it too.
        shell.Queue.SelectedTab = logs;
        Assert.True(shell.Toggles.LogCheck.IsChecked);
    }

    [AvaloniaFact]
    public async Task HiddenFilesLeavesOutDotFilesInEveryPaneAndBringsThemBack()
    {
        var studio = Summary("Studio Assets");
        await using var shell = new Shell([studio]);
        shell.Agent.Listings[(studio.ConnectionId, "")] = new Page(
            [Entry(".git", container: true), Entry("renders", container: true), Entry(".env", 12), Entry("brief.pdf", 2048)]);
        var saved = DesktopUpdatePreferences.Defaults;
        shell.Toggles.Persist = change => saved = change(saved);
        await shell.OpenAsync(studio);
        Assert.Contains(shell.Pane.Rows, static row => row.Name == ".env");

        shell.Run(UiCommandIds.ViewHiddenFiles);

        Assert.Equal(["renders", "brief.pdf"], shell.Pane.Rows.Select(static row => row.Name));
        Assert.DoesNotContain(shell.Pane.Tree.Roots[0].Children, static node => node.Name == ".git");
        Assert.False(shell.Toggles.HiddenCheck.IsChecked);
        Assert.False(saved.ShowHiddenFiles);
        Assert.Equal(Ui.Shell.HiddenFilesNotShown, shell.Model.ShellStatus.Location);

        // A pane made afterwards follows it too.
        var added = shell.Workspace!.SplitActive(WorkspaceDockEdge.Right);
        Assert.NotNull(added);
        Assert.False(added.ShowHiddenItems);

        shell.Run(UiCommandIds.ViewHiddenFiles);
        Assert.Equal([".git", "renders", ".env", "brief.pdf"], shell.Pane.Rows.Select(static row => row.Name));
        Assert.True(added.ShowHiddenItems);
    }

    /// <summary>A file this computer marks hidden is hidden, and so is a dot file on any system.</summary>
    [Fact]
    public void ARowIsHiddenByItsNameOrByThisComputersMark()
    {
        Assert.True(BrowserRowFactory.FromRemote(new StorageListItem(
            ".cache", ".cache", StorageItemKind.Directory, null, null, null, true)).IsHidden);
        Assert.False(BrowserRowFactory.FromRemote(new StorageListItem(
            "cache", "cache", StorageItemKind.Directory, null, null, null, true)).IsHidden);
        Assert.True(BrowserRowFactory.FromLocal(new LocalBrowserEntry(
            "desktop.ini", @"C:\desktop.ini", false, 1, null, "", "", IsHidden: true)).IsHidden);
    }

    [AvaloniaFact]
    public async Task ThemeGoesLightDarkAndBackToTheSystemAndSavesEachOne()
    {
        await using var shell = new Shell([Summary("Studio Assets")]);
        var saved = DesktopUpdatePreferences.Defaults;
        var applied = new List<DesktopAppearance>();
        shell.Toggles.Persist = change => saved = change(saved);
        shell.Toggles.ApplyAppearance = applied.Add;

        shell.Run(UiCommandIds.ViewTheme);
        Assert.Equal(DesktopAppearance.Light, saved.Appearance);
        Assert.Equal(Ui.Format(Ui.Shell.ThemeNowFormat, Ui.Settings.ThemeLight), shell.Model.ShellStatus.Location);

        shell.Run(UiCommandIds.ViewTheme);
        shell.Run(UiCommandIds.ViewTheme);

        Assert.Equal([DesktopAppearance.Light, DesktopAppearance.Dark, DesktopAppearance.System], applied);
        Assert.Equal(DesktopAppearance.System, saved.Appearance);

        // And it starts from what Settings last saved, not from where it was.
        shell.Model.FollowSettings(saved with { Appearance = DesktopAppearance.Dark });
        shell.Run(UiCommandIds.ViewTheme);
        Assert.Equal(DesktopAppearance.System, applied[^1]);
    }

    [AvaloniaFact]
    public async Task HomeGoesToTheTopOfTheConnection()
    {
        var studio = Summary("Studio Assets");
        await using var shell = new Shell([studio]);
        shell.Agent.Listings[(studio.ConnectionId, "")] = new Page([Entry("renders", container: true)]);
        shell.Agent.Listings[(studio.ConnectionId, "renders")] = new Page([Entry("final", container: true, parent: "renders")]);
        await shell.OpenAsync(studio);
        await shell.Pane.NavigateAsync("renders", Cancellation);
        Assert.Contains("renders", shell.Pane.Path, StringComparison.Ordinal);

        shell.Run(UiCommandIds.GoHome);

        await Until(() => shell.Pane.Rows.Any(static row => row.Name == "renders"));
        Assert.DoesNotContain("renders", shell.Pane.Path, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task HomeOnThisComputerIsTheUsersOwnFolder()
    {
        var home = Directory.CreateTempSubdirectory("storagehub-home-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(home.FullName, "notes.txt"), "x", Cancellation);
            await using var shell = new Shell([]);
            await shell.Pane.LoadConnectionsAsync(Cancellation);
            await shell.Pane.OpenAsync(shell.Pane.Connections.Single(static c => c.Kind == PaneContentKind.ThisPc), Cancellation);
            shell.Pane.LocalHome = () => home.FullName;

            shell.Run(UiCommandIds.GoHome);

            await Until(() => shell.Pane.Rows.Any(static row => row.Name == "notes.txt"));
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task HistoryListsTheFoldersVisitedAndGoesBackEvenToAnotherConnection()
    {
        var studio = Summary("Studio Assets");
        var backups = Summary("Site Backups");
        await using var shell = new Shell([studio, backups]);
        shell.Agent.Listings[(studio.ConnectionId, "")] = new Page([Entry("renders", container: true)]);
        shell.Agent.Listings[(studio.ConnectionId, "renders")] = new Page([Entry("shot.exr", 10, parent: "renders")]);
        shell.Agent.Listings[(backups.ConnectionId, "")] = new Page([Entry("2026", container: true)]);
        await shell.OpenAsync(studio);
        await shell.Pane.NavigateAsync("renders", Cancellation);
        await shell.Pane.OpenConnectionAsync(backups.ConnectionId, Cancellation);
        PaneChoices? shown = null;
        shell.Pane.ChoicesRequested += (_, choices) => shown = choices;

        shell.Run(UiCommandIds.GoHistory);

        Assert.NotNull(shown);
        Assert.Equal(Ui.Commands.GoHistory, shown.Heading);

        // Newest first, and not the folder showing.
        Assert.Equal([studio.DisplayName, studio.DisplayName], shown.Items.Select(static item => item.Detail));
        Assert.Contains("renders", shown.Items[0].Label, StringComparison.Ordinal);

        await shown.Items[0].Go!();

        Assert.Equal(studio.ConnectionId, shell.Pane.Connection?.Id);
        Assert.Contains(shell.Pane.Rows, static row => row.Name == "shot.exr");
    }

    [AvaloniaFact]
    public async Task HistoryWithNothingToGoBackToSaysSo()
    {
        var studio = Summary("Studio Assets");
        await using var shell = new Shell([studio]);
        await shell.OpenAsync(studio);

        var choices = shell.Pane.HistoryChoices();

        Assert.Empty(choices.Items);
        Assert.Equal(Ui.Shell.NoOtherLocationsVisited, choices.Empty.Label);
        Assert.Null(choices.Empty.Go);
    }

    [AvaloniaFact]
    public async Task FavoritesListsTheFavouriteConnectionsAndOpensOneInThePane()
    {
        var studio = Summary("Studio Assets") with { IsFavorite = true };
        var archive = Summary("Archive") with { IsFavorite = true };
        var scratch = Summary("Scratch");
        await using var shell = new Shell([studio, archive, scratch]);
        await shell.Pane.LoadConnectionsAsync(Cancellation);
        PaneChoices? shown = null;
        shell.Pane.ChoicesRequested += (_, choices) => shown = choices;

        shell.Run(UiCommandIds.GoFavorites);

        Assert.NotNull(shown);
        Assert.Equal(["Archive", "Studio Assets"], shown.Items.Select(static item => item.Label));

        await shown.Items[1].Go!();
        await Until(() => shell.Pane.Connection?.Id == studio.ConnectionId && !shell.Pane.IsBusy);
    }

    [AvaloniaFact]
    public async Task FavoritesWithNoneSaysHowToMakeOne()
    {
        await using var shell = new Shell([Summary("Scratch")]);
        await shell.Pane.LoadConnectionsAsync(Cancellation);

        var choices = shell.Pane.FavoriteChoices();

        Assert.Empty(choices.Items);
        Assert.Equal(Ui.Shell.NoFavoriteConnections, choices.Empty.Label);
        Assert.Equal(Ui.Shell.FavoritesEmptyHint, choices.Empty.Detail);
    }

    /// <summary>The list is drawn under the pane's address bar, with an entry for each choice.</summary>
    [AvaloniaFact]
    public async Task TheListIsShownAtThePane()
    {
        var studio = Summary("Studio Assets");
        var agent = new FakeBrowsingAgent([studio]);
        agent.Listings[(studio.ConnectionId, "")] = new Page([Entry("renders", container: true)]);
        await using var pane = new BrowserPaneModel(agent);
        await pane.LoadConnectionsAsync(Cancellation);
        await pane.OpenConnectionAsync(studio.ConnectionId, Cancellation);
        await pane.NavigateAsync("renders", Cancellation);
        var view = new BrowserPaneView { DataContext = pane };
        var window = new Window { Content = view, Width = 700, Height = 360 };
        window.Show();

        pane.ShowHistory();

        var flyout = view.LastChoices;
        Assert.NotNull(flyout);
        var entries = flyout.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(Ui.Commands.GoHistory, entries[0].Header);
        Assert.False(entries[0].IsEnabled);
        Assert.True(entries[1].IsEnabled);
        Photograph(window, "pane-history");
    }

    [AvaloniaFact]
    public async Task QuickConnectOpensTheActivePanesPicker()
    {
        await using var shell = new Shell([Summary("Studio Assets")]);
        var asked = 0;
        shell.Pane.ConnectionPickerRequested += (_, _) => asked++;

        shell.Run(UiCommandIds.ConnectionsQuickConnect);

        Assert.Equal(1, asked);
    }

    /// <summary>And in the pane, the picker opens with its search box ready, as a click opens it.</summary>
    [AvaloniaFact]
    public async Task ThePickerOpensFromTheKeyboard()
    {
        var studio = Summary("Studio Assets");
        await using var pane = new BrowserPaneModel(new FakeBrowsingAgent([studio]));
        await pane.LoadConnectionsAsync(Cancellation);
        var view = new BrowserPaneView { DataContext = pane };
        var window = new Window { Content = view, Width = 700, Height = 360 };
        window.Show();
        var flyout = (Flyout)view.FindControl<Button>("PART_ConnectionButton")!.Flyout!;

        pane.ShowConnectionPicker();

        Assert.True(flyout.IsOpen);
        var picker = Assert.IsType<ConnectionPickerModel>(((Control)flyout.Content!).DataContext);
        Assert.Contains(picker.Rows, static row => row.Name == "Studio Assets");
        flyout.Hide();
    }

    [AvaloniaFact]
    public async Task ReconnectOpensTheConnectionAgainAndGoesBackToTheFolder()
    {
        var studio = Summary("Studio Assets");
        await using var shell = new Shell([studio]);
        shell.Agent.Listings[(studio.ConnectionId, "")] = new Page([Entry("renders", container: true)]);
        shell.Agent.Listings[(studio.ConnectionId, "renders")] = new Page([Entry("shot.exr", 10, parent: "renders")]);
        await shell.OpenAsync(studio);
        await shell.Pane.NavigateAsync("renders", Cancellation);
        var asked = new List<string>();
        shell.Agent.BeforeListing = (request, _) =>
        {
            lock (asked) asked.Add(request.RelativePath);
            return Task.CompletedTask;
        };

        shell.Run(UiCommandIds.ConnectionsReconnect);

        await Until(() => asked.Count >= 2 && !shell.Pane.IsBusy);
        Assert.Equal(["", "renders"], asked);
        Assert.Contains(shell.Pane.Rows, static row => row.Name == "shot.exr");
    }

    [AvaloniaFact]
    public async Task DisconnectLeavesTheConnectionForConnectionsHome()
    {
        var studio = Summary("Studio Assets");
        await using var shell = new Shell([studio]);
        await shell.OpenAsync(studio);
        Assert.True(shell.Command(UiCommandIds.ConnectionsReconnect).CanExecute(null));

        shell.Run(UiCommandIds.ConnectionsDisconnect);

        await Until(() => shell.Pane.ContentKind == PaneContentKind.ConnectionsHome && !shell.Pane.IsBusy);
        Assert.Equal(Ui.Format(Ui.Shell.DisconnectedFromFormat, studio.DisplayName), shell.Model.ShellStatus.Location);

        // Nothing open is nothing to reconnect or disconnect.
        Assert.False(shell.Command(UiCommandIds.ConnectionsDisconnect).CanExecute(null));
        Assert.False(shell.Command(UiCommandIds.ConnectionsReconnect).CanExecute(null));
    }

    [AvaloniaFact]
    public async Task TestConnectionTestsTheActivePanesConnectionAndSaysWhatItCameTo()
    {
        var studio = Summary("Studio Assets");
        var backups = Summary("Site Backups");
        await using var shell = new Shell([studio, backups]);
        await shell.Model.Sidebar.RefreshAsync(Cancellation);
        Assert.False(shell.Command(UiCommandIds.ConnectionsTestConnection).CanExecute(null));
        await shell.OpenAsync(backups);
        Assert.True(shell.Command(UiCommandIds.ConnectionsTestConnection).CanExecute(null));

        shell.Run(UiCommandIds.ConnectionsTestConnection);

        var expected = Ui.Format(Ui.Shell.TestedConnectionFormat, backups.DisplayName, Ui.Connections.ConnectionReachable);
        await Until(() => shell.Model.ShellStatus.Location == expected);
        Assert.Equal(backups.ConnectionId, shell.Model.Sidebar.Selected?.Id);
        Assert.Equal(Ui.Connections.ConnectionReachable, shell.Model.Sidebar.DetailStatus);
    }

    /// <summary>The connections panel's selection is "the selected profile" the entry speaks of.</summary>
    [AvaloniaFact]
    public async Task TestConnectionPrefersTheConnectionSelectedInThePanel()
    {
        var studio = Summary("Studio Assets");
        var backups = Summary("Site Backups");
        await using var shell = new Shell([studio, backups]);
        await shell.Model.Sidebar.RefreshAsync(Cancellation);
        await shell.OpenAsync(backups);
        Assert.True(shell.Model.Sidebar.SelectConnection(studio.ConnectionId));

        Assert.Equal(studio.ConnectionId, ShellViewCommands.TestTarget(shell.Model));
    }

    /// <summary>
    /// With no pane, as on Welcome, the pane's entries dim and the shell's stay lit.
    /// </summary>
    [AvaloniaFact]
    public async Task WithoutAPaneThePaneEntriesDim()
    {
        await using var shell = new Shell([Summary("Studio Assets")], withWorkspace: false);

        foreach (var id in new[]
        {
            UiCommandIds.ViewDirectoryTree, UiCommandIds.GoHome, UiCommandIds.GoHistory, UiCommandIds.GoFavorites,
            UiCommandIds.ConnectionsQuickConnect, UiCommandIds.ConnectionsReconnect,
            UiCommandIds.ConnectionsDisconnect, UiCommandIds.ConnectionsTestConnection,
        })
        {
            Assert.False(shell.Command(id).CanExecute(null), $"{id} is offered with nothing to act on.");
        }

        foreach (var id in new[]
        {
            UiCommandIds.ViewTransferQueue, UiCommandIds.ViewSessionLog, UiCommandIds.ViewHiddenFiles, UiCommandIds.ViewTheme,
        })
        {
            Assert.True(shell.Command(id).CanExecute(null), $"{id} is dimmed.");
        }
    }

    /// <summary>Kept for a human to look at when STORAGEHUB_SHOT_DIR names a folder.</summary>
    private static void Photograph(Window window, string name)
    {
        window.UpdateLayout();
        var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
        Assert.NotNull(frame);
        var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        frame!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
