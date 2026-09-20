using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// A browser pane driven through a whole navigation, against an agent that answers from memory.
/// </summary>
/// <remarks>
/// This is the thing BrowserPaneControl could never be tested for. It was 3,621 lines with the
/// listing, the history, the tree, the icons and the drawing in one class, so "open a connection,
/// enter a folder, go up" could only be checked by doing it. The pane is a view model over
/// RemoteBrowserController now, and the whole sequence runs here in milliseconds.
/// </remarks>
public class BrowserPaneTests
{
    [AvaloniaFact]
    public async Task OnlyConnectionsThatCanBeBrowsedAreOffered()
    {
        var agent = new FakeBrowsingAgent();
        agent.Connections.Add(Connection("Studio Assets", StorageConnectionProvider.S3));
        agent.Connections.Add(Connection("Build Box", StorageConnectionProvider.Ssh, ConnectionProfileType.Client));
        agent.Connections.Add(Connection("Disabled", StorageConnectionProvider.Sftp, enabled: false));

        await using var pane = new BrowserPaneModel(agent);
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);

        // The SSH client can be browsed; the disabled one cannot, and offering it would produce a
        // pane that fails the moment somebody chose it.
        Assert.Equal(
            ["Build Box", "Studio Assets"],
            pane.Connections.Select(c => c.Name).Order(StringComparer.Ordinal));
    }

    [AvaloniaFact]
    public async Task OpeningAConnectionListsItsRoot()
    {
        var agent = new FakeBrowsingAgent();
        var connection = Connection("Studio Assets", StorageConnectionProvider.S3);
        agent.Connections.Add(connection);
        agent.Listings[string.Empty] =
            [Entry("reports", container: true), Entry("render.exr", size: 193_204_224)];

        await using var pane = new BrowserPaneModel(agent);
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);

        Assert.Equal("/", pane.Path);
        Assert.Equal(["reports", "render.exr"], pane.Rows.Select(row => row.Name));
        Assert.False(pane.HasStatus);

        // The row projection is BrowserRowFactory's, which the WinForms pane uses too, so a size
        // reads the same in both shells.
        Assert.Equal(Ui.Pane.Folder, pane.Rows[0].Type);
        Assert.False(string.IsNullOrWhiteSpace(pane.Rows[1].Size));
    }

    /// <summary>
    /// A listing below the root is headed by "..", and opening it goes up.
    /// </summary>
    /// <remarks>
    /// Part of the listing rather than a button beside it, which is how every file manager since
    /// Norton Commander has done it and what makes a double-click enough.
    /// </remarks>
    [AvaloniaFact]
    public async Task AFolderBelowTheRootIsHeadedByTheParentRow()
    {
        var pane = await OpenedAsync();

        await pane.NavigateAsync("reports", TestContext.Current.CancellationToken);

        Assert.Equal("/reports", pane.Path);
        Assert.True(pane.Rows[0].IsParentNavigation);
        Assert.Equal(["..", "q1.pdf"], pane.Rows.Select(row => row.Name));

        pane.Selected = pane.Rows[0];
        await pane.OpenSelectedAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/", pane.Path);
        Assert.DoesNotContain(pane.Rows, row => row.IsParentNavigation);
        await pane.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task BackAndForwardWalkTheHistory()
    {
        var pane = await OpenedAsync();
        await pane.NavigateAsync("reports", TestContext.Current.CancellationToken);

        await pane.BackAsync(TestContext.Current.CancellationToken);
        Assert.Equal("/", pane.Path);

        await pane.ForwardAsync(TestContext.Current.CancellationToken);
        Assert.Equal("/reports", pane.Path);
        await pane.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task TheCommandsFollowWhereThePaneIs()
    {
        var pane = await OpenedAsync();

        Assert.False(pane.UpCommand.CanExecute(null));
        Assert.False(pane.BackCommand.CanExecute(null));

        await pane.NavigateAsync("reports", TestContext.Current.CancellationToken);

        Assert.True(pane.UpCommand.CanExecute(null));
        Assert.True(pane.BackCommand.CanExecute(null));
        Assert.False(pane.ForwardCommand.CanExecute(null));
        await pane.DisposeAsync();
    }

    /// <summary>Only a container opens. A file is what a transfer acts on, not a destination.</summary>
    [AvaloniaFact]
    public async Task AFileIsNotSomethingToOpen()
    {
        var pane = await OpenedAsync();

        pane.Selected = pane.Rows.Single(row => row.Name == "render.exr");

        Assert.False(pane.OpenCommand.CanExecute(null));
        await pane.DisposeAsync();
    }

    /// <summary>
    /// A folder that is gone lands on the nearest one that is not, and says so.
    /// </summary>
    /// <remarks>
    /// RemoteBrowserController walks up until it finds a container that lists, and reports success
    /// with a note. The note is the only thing that explains why the listing is not the one that
    /// was asked for, so a pane that showed the rows and dropped the message would be worse than
    /// one that failed outright.
    /// </remarks>
    [AvaloniaFact]
    public async Task AFolderThatIsGoneLandsOnItsParentAndSaysWhy()
    {
        var pane = await OpenedAsync();

        await pane.NavigateAsync("no-such-folder", TestContext.Current.CancellationToken);

        Assert.Equal("/", pane.Path);
        Assert.Equal(["reports", "empty", "render.exr"], pane.Rows.Select(row => row.Name));
        Assert.True(pane.HasStatus);
        await pane.DisposeAsync();
    }

    /// <summary>A refusal that is not "missing" keeps the listing and reports the reason.</summary>
    /// <remarks>
    /// The old pane cleared its rows, so a path that could not be read lost what somebody was
    /// looking at and cost another round trip to get back to it.
    /// </remarks>
    [AvaloniaFact]
    public async Task ARefusedNavigationKeepsTheListing()
    {
        var pane = await OpenedAsync();
        var before = pane.Rows.Select(row => row.Name).ToArray();

        await pane.NavigateAsync("forbidden", TestContext.Current.CancellationToken);

        Assert.Equal(before, pane.Rows.Select(row => row.Name));
        Assert.True(pane.HasStatus);
        Assert.Equal("/", pane.Path);
        await pane.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task AnEmptyFolderSaysSoRatherThanLookingBroken()
    {
        var pane = await OpenedAsync();

        await pane.NavigateAsync("empty", TestContext.Current.CancellationToken);

        // The parent row is there, so "empty" has to mean "nothing but that".
        Assert.Single(pane.Rows);
        Assert.True(pane.Rows[0].IsParentNavigation);
        await pane.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task AnAgentThatRefusesLeavesTheMessageRatherThanThrowing()
    {
        var agent = new FakeBrowsingAgent { Throw = true };

        await using var pane = new BrowserPaneModel(agent);
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);

        Assert.Empty(pane.Connections);
        Assert.True(pane.HasStatus);
    }

    /// <summary>
    /// Marking a pane active must not move its rows.
    /// </summary>
    /// <remarks>
    /// It did: the active style set a 2px border against the inactive 1px, so every row in the
    /// active pane sat one pixel lower than the same row beside it. Two panes exist to be compared,
    /// so they stop lining up exactly when somebody is looking hardest. Colour marks the active
    /// one now, and this is what says the thickness may not follow.
    /// </remarks>
    [AvaloniaFact]
    public void MarkingAPaneActiveChangesItsColourAndNotItsLayout()
    {
        var view = new BrowserPaneView { DataContext = new BrowserPaneModel() };
        var window = new global::Avalonia.Controls.Window { Content = view, Width = 400, Height = 300 };
        window.Show();
        window.UpdateLayout();

        var border = view.GetVisualDescendants().OfType<global::Avalonia.Controls.Border>()
            .First(candidate => candidate.Classes.Contains("pane"));
        var inactive = border.BorderThickness;

        ((BrowserPaneModel)view.DataContext!).IsActive = true;
        window.UpdateLayout();

        Assert.Equal(inactive, border.BorderThickness);
        Assert.NotNull(border.BorderBrush);
    }

    private static async Task<BrowserPaneModel> OpenedAsync()
    {
        var agent = new FakeBrowsingAgent();
        var connection = Connection("Studio Assets", StorageConnectionProvider.S3);
        agent.Connections.Add(connection);
        agent.Listings[string.Empty] =
            [Entry("reports", container: true), Entry("empty", container: true), Entry("render.exr", size: 1024)];
        agent.Listings["reports"] = [Entry("q1.pdf", size: 2048)];
        agent.Listings["empty"] = [];

        var pane = new BrowserPaneModel(agent);
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);
        return pane;
    }

    private static ConnectionSummary Connection(
        string name,
        StorageConnectionProvider provider,
        ConnectionProfileType type = ConnectionProfileType.Storage,
        bool enabled = true) =>
        new(
            Guid.NewGuid(),
            name,
            provider,
            FolderPath: null,
            Tags: [],
            IsFavorite: false,
            IsEnabled: enabled,
            IconKey: null,
            AccentColor: null,
            Version: 1,
            type);

    private static StorageListItem Entry(string name, bool container = false, long? size = null) =>
        new(
            name,
            name,
            container ? StorageItemKind.Directory : StorageItemKind.File,
            container ? null : size ?? 0,
            DateTimeOffset.UtcNow,
            ContentType: null,
            container);

    /// <summary>An agent with a directory tree in a dictionary.</summary>
    private sealed class FakeBrowsingAgent : IRemoteStorageAgentClient
    {
        internal List<ConnectionSummary> Connections { get; } = [];

        internal Dictionary<string, StorageListItem[]> Listings { get; } =
            new(StringComparer.Ordinal);

        internal bool Throw { get; set; }

        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Throw) throw new IOException("The agent is not listening.");
            return Task.FromResult(new ConnectionListResponse(
                StorageIpcContract.CurrentVersion, [.. Connections]));
        }

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Throw) throw new IOException("The agent is not listening.");

            if (string.Equals(request.RelativePath, "forbidden", StringComparison.Ordinal))
            {
                return Task.FromResult(new StorageListPageResponse(
                    StorageIpcContract.CurrentVersion,
                    request.ConnectionId,
                    request.RelativePath,
                    [],
                    ContinuationToken: null,
                    new StorageIpcFailure(
                        "unauthorized",
                        StorageIpcFailureCategory.Unauthorized,
                        "You may not read that folder.",
                        IsTransient: false)));
            }

            if (!Listings.TryGetValue(request.RelativePath, out var entries))
            {
                return Task.FromResult(new StorageListPageResponse(
                    StorageIpcContract.CurrentVersion,
                    request.ConnectionId,
                    request.RelativePath,
                    [],
                    ContinuationToken: null,
                    new StorageIpcFailure(
                        "not-found",
                        StorageIpcFailureCategory.NotFound,
                        "That folder is not there.",
                        IsTransient: false)));
            }

            return Task.FromResult(new StorageListPageResponse(
                StorageIpcContract.CurrentVersion,
                request.ConnectionId,
                request.RelativePath,
                entries,
                ContinuationToken: null));
        }

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Throw) throw new IOException("The agent is not listening.");
            return Task.FromResult(new ConnectionTestResponse(
                StorageIpcContract.CurrentVersion, request.ConnectionId, Succeeded: true, 1));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
