using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Avalonia.Tests;

/// <summary>
/// A pane pointed at this computer, over a filesystem that only exists in memory.
/// </summary>
/// <remarks>
/// The same pane type as a connection, driven the same way, which is the point of
/// <see cref="IPaneSource"/>: browsing a bucket and browsing a disk differ in one object, not in
/// every method. The old pane branched on which browser it was holding at more or less every call
/// site, and is 3,913 lines partly because of it.
/// </remarks>
public class LocalPaneTests
{
    [AvaloniaFact]
    public async Task ThisPcListsTheDrives()
    {
        await using var pane = await OpenedAsync();

        Assert.Equal(Ui.Pane.ThisPc, pane.Path);
        Assert.Equal(FakeDisks.RootNames, pane.Rows.Select(row => row.Name));

        // No parent row: This PC is the top, and offering ".." there would go nowhere.
        Assert.DoesNotContain(pane.Rows, row => row.IsParentNavigation);
        Assert.False(pane.UpCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task EnteringAFolderListsItAndOffersTheWayBack()
    {
        await using var pane = await OpenedAsync();

        pane.Selected = pane.Rows[0];
        await pane.OpenSelectedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["..", "work", "notes.txt"], pane.Rows.Select(row => row.Name));
        Assert.True(pane.UpCommand.CanExecute(null));
        Assert.True(pane.BackCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task GoingUpFromADriveArrivesBackAtThisPc()
    {
        await using var pane = await OpenedAsync();
        pane.Selected = pane.Rows[0];
        await pane.OpenSelectedAsync(TestContext.Current.CancellationToken);

        await pane.UpAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Ui.Pane.ThisPc, pane.Path);
        Assert.Equal(FakeDisks.RootNames, pane.Rows.Select(row => row.Name));
    }

    /// <summary>
    /// The rows are the same five columns as a connection's.
    /// </summary>
    /// <remarks>
    /// Two panes exist to be compared, and one showing "1.2 MB" beside another showing
    /// "1,258,291 bytes" is the kind of difference that only shows up when somebody is doing the
    /// thing the app is for. BrowserRowFactory is the one projection for both.
    /// </remarks>
    [AvaloniaFact]
    public async Task ALocalRowLooksLikeARemoteOne()
    {
        await using var pane = await OpenedAsync();
        pane.Selected = pane.Rows[0];
        await pane.OpenSelectedAsync(TestContext.Current.CancellationToken);

        var file = pane.Rows.Single(row => row.Name == "notes.txt");

        Assert.False(string.IsNullOrWhiteSpace(file.Size));
        Assert.False(file.IsContainer);
        Assert.Equal(StorageItemKind.File, file.Kind);
        Assert.Equal(FakeDisks.NotesPath, file.Location);
    }

    /// <summary>A local pane is a transfer endpoint like any other.</summary>
    /// <remarks>
    /// This is what makes local-to-remote work without anything above the pane knowing it is that:
    /// the source says ThisPc, the destination says SavedConnection, and ManualTransferController
    /// has understood both since long before this port.
    /// </remarks>
    [AvaloniaFact]
    public async Task ALocalPaneDescribesItselfAsThisPc()
    {
        await using var pane = await OpenedAsync();
        pane.Selected = pane.Rows[0];
        await pane.OpenSelectedAsync(TestContext.Current.CancellationToken);

        var context = PaneTransferSnapshots.ContextFor(pane.Source);

        Assert.True(context.IsSuccess);
        Assert.Equal(PaneTransferContextKind.ThisPc, context.Value.Kind);
        Assert.Equal(FakeDisks.FirstDrive.DirectoryPath, context.Value.RelativePath);
    }

    [AvaloniaFact]
    public async Task AFolderThatCannotBeReadSaysSoAndKeepsTheListing()
    {
        await using var pane = await OpenedAsync();
        var before = pane.Rows.Select(row => row.Name).ToArray();

        await pane.NavigateAsync(FakeDisks.LockedPath, TestContext.Current.CancellationToken);

        Assert.Equal(before, pane.Rows.Select(row => row.Name));
        Assert.True(pane.HasStatus);
    }

    /// <summary>
    /// Choosing This PC and then a connection, and back, leaves a working pane each time.
    /// </summary>
    /// <remarks>
    /// The source is swapped rather than the pane rebuilt, so this is where a stale history or a
    /// disposed client would show up.
    /// </remarks>
    [AvaloniaFact]
    public async Task SwitchingBetweenThisPcAndAConnectionWorksBothWays()
    {
        var connection = Summary("Studio Assets");
        var agent = new FakeAgent([connection]);
        await using var pane = new BrowserPaneModel(agent, () => new FakeDisks());
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);

        await pane.OpenAsync(
            pane.Connections.Single(c => c.Id is null), TestContext.Current.CancellationToken);
        Assert.Equal(Ui.Pane.ThisPc, pane.Path);

        await pane.OpenAsync(
            pane.Connections.Single(c => c.Id == connection.ConnectionId),
            TestContext.Current.CancellationToken);
        Assert.Equal("/", pane.Path);
        Assert.Equal(["bucket-file.bin"], pane.Rows.Select(row => row.Name));

        await pane.OpenAsync(
            pane.Connections.Single(c => c.Id is null), TestContext.Current.CancellationToken);
        Assert.Equal(Ui.Pane.ThisPc, pane.Path);
        Assert.Equal(FakeDisks.RootNames, pane.Rows.Select(row => row.Name));
    }

    private static async Task<BrowserPaneModel> OpenedAsync()
    {
        var pane = new BrowserPaneModel(new FakeAgent([]), () => new FakeDisks());
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenAsync(
            pane.Connections.Single(c => c.Id is null), TestContext.Current.CancellationToken);
        return pane;
    }

    private static ConnectionSummary Summary(string name) => new(
        Guid.NewGuid(),
        name,
        StorageConnectionProvider.S3,
        FolderPath: null,
        Tags: [],
        IsFavorite: false,
        IsEnabled: true,
        IconKey: null,
        AccentColor: null,
        Version: 1);

    /// <summary>
    /// Two drives and a couple of folders, with no filesystem underneath.
    /// </summary>
    /// <remarks>
    /// The paths are built through LocalBrowserLocation rather than written out, because what a
    /// path normalises to is the platform's answer -- a drive root on Windows is an ordinary
    /// relative filename on Linux. Asking the type the browser itself uses is what lets this suite
    /// run on both, and is the same lesson the desktop suites learned the first time they ran on
    /// Ubuntu.
    /// </remarks>
    private sealed class FakeDisks : ILocalFileBrowserDataSource
    {
        /// <summary>
        /// What This PC lists: two drives on Windows, one root on Linux.
        /// </summary>
        /// <remarks>
        /// Not a cosmetic difference. Going up from a drive root arrives at This PC because a root
        /// has no parent, and "/mnt/one" has one -- so a fixture that pretended Linux had drives
        /// would have this suite asserting the wrong thing about the one navigation that differs
        /// between the platforms.
        /// </remarks>
        internal static IReadOnlyList<LocalBrowserLocation> Roots { get; } = OperatingSystem.IsWindows()
            ? [LocalBrowserLocation.FromDirectory(@"C:\"), LocalBrowserLocation.FromDirectory(@"D:\")]
            : [LocalBrowserLocation.FromDirectory("/")];

        internal static LocalBrowserLocation FirstDrive => Roots[0];

        internal static IReadOnlyList<string> RootNames { get; } = [.. Roots.Select(NameOf)];

        internal static string WorkPath { get; } = Path.Combine(FirstDrive.DirectoryPath!, "work");

        internal static string NotesPath { get; } = Path.Combine(FirstDrive.DirectoryPath!, "notes.txt");

        internal static string LockedPath { get; } = Path.Combine(FirstDrive.DirectoryPath!, "locked");

        public Task<LocalBrowserSnapshot> BrowseAsync(
            LocalBrowserLocation location,
            CancellationToken cancellationToken)
        {
            if (location.IsThisPc)
            {
                return Task.FromResult(new LocalBrowserSnapshot(location,
                [.. Roots.Select(root => new LocalBrowserEntry(
                    NameOf(root),
                    root.DirectoryPath!,
                    true,
                    null,
                    null,
                    "Local disk drive",
                    "460 GB free"))]));
            }

            if (string.Equals(location.DirectoryPath, LockedPath, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("Access to that folder is denied.");
            }

            if (location.IsSameLocation(FirstDrive))
            {
                return Task.FromResult(new LocalBrowserSnapshot(location,
                [
                    new("work", WorkPath, true, null, DateTimeOffset.UtcNow, "Folder", string.Empty),
                    new("notes.txt", NotesPath, false, 2048, DateTimeOffset.UtcNow, "Text", string.Empty)
                ]));
            }

            return Task.FromResult(new LocalBrowserSnapshot(location, []));
        }

        /// <summary>A drive root has no file name, so it is shown by its path.</summary>
        private static string NameOf(LocalBrowserLocation location) =>
            Path.GetFileName(location.DirectoryPath!) is { Length: > 0 } name
                ? name
                : location.DirectoryPath!;
    }

    private sealed class FakeAgent(ConnectionSummary[] connections) : IRemoteStorageAgentClient
    {
        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionListResponse(StorageIpcContract.CurrentVersion, connections));

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResponse(
                StorageIpcContract.CurrentVersion, request.ConnectionId, Succeeded: true, 1));

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StorageListPageResponse(
                StorageIpcContract.CurrentVersion,
                request.ConnectionId,
                request.RelativePath,
                [new StorageListItem(
                    "bucket-file.bin",
                    "bucket-file.bin",
                    StorageItemKind.File,
                    512,
                    DateTimeOffset.UtcNow,
                    ContentType: null,
                    IsContainer: false)],
                ContinuationToken: null,
                RootIdentity: "root"));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
