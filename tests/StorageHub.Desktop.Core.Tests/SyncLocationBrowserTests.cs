using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Browsing a connection's folders for a sync location.
/// </summary>
/// <remarks>
/// This was 1.x's SyncLocationPickerForm, and none of it was tested: reaching it meant showing the
/// Form. What matters is that only folders are offered, that a page is added to rather than
/// replacing what is listed, and that only a folder that was listed can be chosen.
/// </remarks>
public sealed class SyncLocationBrowserTests
{
    private static readonly Guid ConnectionId = Guid.NewGuid();

    [Fact]
    public async Task OnlyFoldersAreListed()
    {
        var agent = new FolderAgent { [""] = [Page(null, Folder("photos"), File("readme.txt"), Folder("music"))] };
        var browser = new SyncLocationBrowser(() => agent, ConnectionId);

        await browser.NavigateAsync(string.Empty, CancellationToken.None);

        Assert.Equal(["photos", "music"], browser.Folders.Select(static folder => folder.Name));
        Assert.True(browser.HasLoaded);
        Assert.False(browser.CanGoUp);
        Assert.Equal(Ui.Format(Ui.Sync.PickerRootCountFormat, 2), browser.Status);
    }

    [Fact]
    public async Task OpeningAFolderAndGoingUpComeBackToTheRoot()
    {
        var agent = new FolderAgent
        {
            [""] = [Page(null, Folder("photos"))],
            ["photos"] = [Page(null, Folder("photos/2019"))]
        };
        var browser = new SyncLocationBrowser(() => agent, ConnectionId);

        await browser.NavigateAsync("photos", CancellationToken.None);
        Assert.Equal("photos", browser.CurrentPath);
        Assert.Equal(Ui.Format(Ui.Sync.PickerPathCountFormat, "photos", 1), browser.Status);

        await browser.UpAsync(CancellationToken.None);
        Assert.Equal(string.Empty, browser.CurrentPath);
        Assert.Equal(["photos"], browser.Folders.Select(static folder => folder.Name));
    }

    /// <summary>A second page is added to the first, and the continuation goes with the request.</summary>
    [Fact]
    public async Task LoadMoreAppendsTheNextPage()
    {
        var agent = new FolderAgent { [""] = [Page("next", Folder("a")), Page(null, Folder("b"))] };
        var browser = new SyncLocationBrowser(() => agent, ConnectionId);
        await browser.NavigateAsync(string.Empty, CancellationToken.None);
        Assert.True(browser.CanLoadMore);

        await browser.LoadMoreAsync(CancellationToken.None);

        Assert.Equal(["a", "b"], browser.Folders.Select(static folder => folder.Name));
        Assert.False(browser.CanLoadMore);
        Assert.Equal([null, "next"], agent.Tokens);
    }

    /// <summary>
    /// A provider's refusal is shown in its own words, and the folder cannot then be chosen.
    /// </summary>
    [Fact]
    public async Task AFolderThatCouldNotBeListedCannotBeChosen()
    {
        var agent = new FolderAgent
        {
            Failure = new StorageIpcFailure("gone", StorageIpcFailureCategory.NotFound, "not found", false)
        };
        var browser = new SyncLocationBrowser(() => agent, ConnectionId);

        await browser.NavigateAsync("missing", CancellationToken.None);

        Assert.False(browser.HasLoaded);
        Assert.True(browser.IsFailure);
        Assert.Equal(Ui.Validation.TheRemoteFolderOrSavedConnectionWas, browser.Status);
    }

    [Fact]
    public async Task AnAgentThatIsNotRunningIsReportedNotThrown()
    {
        var browser = new SyncLocationBrowser(
            static () => throw new IOException("the agent is not listening"), ConnectionId);

        await browser.NavigateAsync(string.Empty, CancellationToken.None);

        Assert.True(browser.IsFailure);
        Assert.False(browser.HasLoaded);
    }

    /// <summary>A typed path that is not one is refused before anything is asked.</summary>
    [Fact]
    public async Task AnInvalidPathIsRefusedWithoutAsking()
    {
        var agent = new FolderAgent();
        var browser = new SyncLocationBrowser(() => agent, ConnectionId);

        await browser.NavigateAsync("../outside", CancellationToken.None);

        Assert.True(browser.IsFailure);
        Assert.Empty(agent.Tokens);
    }

    /// <summary>Past the limit the listing stops, says why, and offers no more pages.</summary>
    [Fact]
    public async Task AHugeFolderStopsAtTheLimit()
    {
        var many = Enumerable.Range(0, SyncLocationBrowser.MaximumFolders + 5)
            .Select(static index => Folder($"f{index}"))
            .ToArray();
        var agent = new FolderAgent { [""] = [Page("more", many)] };
        var browser = new SyncLocationBrowser(() => agent, ConnectionId);

        await browser.NavigateAsync(string.Empty, CancellationToken.None);

        Assert.Equal(SyncLocationBrowser.MaximumFolders, browser.Folders.Count);
        Assert.False(browser.CanLoadMore);
        Assert.Equal(Ui.Sync.PickerDisplayLimit, browser.Status);
    }

    private static StorageListItem Folder(string path) =>
        new(path[(path.LastIndexOf('/') + 1)..], path, StorageItemKind.Directory, null, null, null, IsContainer: true);

    private static StorageListItem File(string path) =>
        new(path, path, StorageItemKind.File, 10, null, null, IsContainer: false);

    private static (string? Next, StorageListItem[] Entries) Page(string? next, params StorageListItem[] entries) =>
        (next, entries);

    /// <summary>Folders by path, one page per request, with the tokens it was asked with.</summary>
    private sealed class FolderAgent : IRemoteStorageAgentClient
    {
        private readonly Dictionary<string, (string? Next, StorageListItem[] Entries)[]> _pages = [];

        internal StorageIpcFailure? Failure { get; init; }

        internal List<string?> Tokens { get; } = [];

        public (string? Next, StorageListItem[] Entries)[] this[string path]
        {
            set => _pages[path] = value;
        }

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default)
        {
            Tokens.Add(request.ContinuationToken);
            if (Failure is not null)
            {
                return Task.FromResult(new StorageListPageResponse(
                    StorageIpcContract.CurrentVersion, request.ConnectionId, request.RelativePath, [], null, Failure));
            }

            var pages = _pages[request.RelativePath];
            var (next, entries) = pages[request.ContinuationToken is null ? 0 : 1];
            return Task.FromResult(new StorageListPageResponse(
                StorageIpcContract.CurrentVersion, request.ConnectionId, request.RelativePath, entries, next));
        }

        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
