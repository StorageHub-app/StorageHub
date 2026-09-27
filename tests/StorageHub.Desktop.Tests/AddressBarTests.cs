using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Typing where to go, as 1.x's address bar allowed; 2.0's was read-only.
/// </summary>
public class AddressBarTests
{
    [AvaloniaFact]
    public async Task ATypedFolderIsWhereThePaneGoes()
    {
        var folder = Directory.CreateTempSubdirectory("storagehub-address-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "a.txt"), "x", TestContext.Current.CancellationToken);
            await using var pane = await ThisPcAsync();

            pane.Address = folder.FullName;
            await pane.GoToAddressAsync(TestContext.Current.CancellationToken);

            Assert.Contains(pane.Rows, static row => row.Name == "a.txt");
            Assert.Equal(pane.Path, pane.Address);

            // And the name This PC goes back to the drives.
            pane.Address = Ui.Pane.ThisPc;
            await pane.GoToAddressAsync(TestContext.Current.CancellationToken);
            Assert.Equal(Ui.Pane.ThisPc, pane.Path);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A folder that does not exist lands on the nearest one that does, and says so -- the local
    /// browser's own rule, and 1.x's.
    /// </summary>
    [AvaloniaFact]
    public async Task AMissingFolderLandsOnItsNearestParentAndSaysSo()
    {
        await using var pane = await ThisPcAsync();
        var temp = Path.TrimEndingDirectorySeparator(Path.GetTempPath());

        pane.Address = Path.Combine(temp, "no-such-folder-" + Guid.NewGuid().ToString("N"));
        await pane.GoToAddressAsync(TestContext.Current.CancellationToken);

        Assert.Equal(temp, Path.TrimEndingDirectorySeparator(pane.Path), ignoreCase: true);
        Assert.True(pane.HasStatus);
    }

    /// <summary>What is not a path at all says why, and the listing stays: a typo costs nothing.</summary>
    [AvaloniaFact]
    public async Task SomethingThatIsNotAPathSaysWhyAndKeepsTheListing()
    {
        await using var pane = await ThisPcAsync();
        var before = pane.Rows.Select(static row => row.Name).ToArray();

        pane.Address = "not a path";
        await pane.GoToAddressAsync(TestContext.Current.CancellationToken);

        Assert.True(pane.HasStatus);
        Assert.Equal(before, pane.Rows.Select(static row => row.Name));

        pane.ResetAddress();
        Assert.Equal(pane.Path, pane.Address);
    }

    /// <summary>A connection that will not open shows nothing, not the last one's files.</summary>
    [AvaloniaFact]
    public async Task AConnectionThatWillNotOpenLeavesNothingBehind()
    {
        var connection = WorkspaceFakes.Summary("Offline bucket");
        await using var pane = new BrowserPaneModel(new Refusing(connection));
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenAsync(pane.Connections.Single(static c => c.Kind == PaneContentKind.ThisPc), TestContext.Current.CancellationToken);
        Assert.NotEmpty(pane.Rows);

        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);

        Assert.Empty(pane.Rows);
        Assert.True(pane.ConnectionStateIsFailed);
    }

    /// <summary>An empty folder says so in the middle of the list; a full one says nothing.</summary>
    [AvaloniaFact]
    public async Task AnEmptyFolderSaysSoInTheList()
    {
        var folder = Directory.CreateTempSubdirectory("storagehub-empty-");
        try
        {
            await using var pane = await ThisPcAsync();
            Assert.False(pane.HasEmptyNotice);

            pane.Address = folder.FullName;
            await pane.GoToAddressAsync(TestContext.Current.CancellationToken);

            Assert.Equal(Ui.Pane.FolderIsEmpty, pane.EmptyNotice);
            Assert.False(pane.HasStatus);
            Assert.False(pane.ShowsLoading);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>A failure is a warning, and trying again reopens what failed.</summary>
    [AvaloniaFact]
    public async Task AFailureIsAWarningThatCanBeRetried()
    {
        var connection = WorkspaceFakes.Summary("Offline bucket");
        await using var pane = new BrowserPaneModel(new Refusing(connection));
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);

        Assert.True(pane.StatusIsWarning);
        Assert.True(pane.HasStatus);
        Assert.False(pane.HasEmptyNotice);
        Assert.True(pane.RetryCommand.CanExecute(null));
    }

    /// <summary>The empty and the failed pane, for a human to look at. STORAGEHUB_SHOT_DIR keeps them.</summary>
    [AvaloniaFact]
    public async Task TheEmptyAndTheFailedPaneCanBePhotographed()
    {
        var folder = Directory.CreateTempSubdirectory("storagehub-empty-");
        try
        {
            await using var empty = await ThisPcAsync();
            empty.Address = folder.FullName;
            await empty.GoToAddressAsync(TestContext.Current.CancellationToken);

            var connection = WorkspaceFakes.Summary("Offline bucket");
            await using var failed = new BrowserPaneModel(new Refusing(connection));
            await failed.LoadConnectionsAsync(TestContext.Current.CancellationToken);
            await failed.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);

            foreach (var (pane, name) in new[] { (empty, "pane-empty"), (failed, "pane-failed") })
            {
                var window = new Avalonia.Controls.Window
                {
                    Content = new BrowserPaneView { DataContext = pane }, Width = 700, Height = 360
                };
                window.Show();
                window.Measure(new Avalonia.Size(700, 360));
                window.Arrange(new Avalonia.Rect(0, 0, 700, 360));
                window.UpdateLayout();
                var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
                Assert.NotNull(frame);
                var directory = Environment.GetEnvironmentVariable("STORAGEHUB_SHOT_DIR");
                if (string.IsNullOrWhiteSpace(directory)) continue;
                Directory.CreateDirectory(directory);
                using var stream = File.Create(Path.Combine(directory, name + ".png"));
                frame!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The quiet re-read during a transfer: a file that landed appears, and nothing else moves --
    /// not the filter, not the selection, not the rows already shown.
    /// </summary>
    [AvaloniaFact]
    public async Task AQuietRefreshShowsWhatLandedAndKeepsEverythingElse()
    {
        var folder = Directory.CreateTempSubdirectory("storagehub-quiet-");
        try
        {
            foreach (var name in new[] { "a.txt", "b.txt", "notes.md" })
            {
                await File.WriteAllTextAsync(Path.Combine(folder.FullName, name), "x", TestContext.Current.CancellationToken);
            }

            await using var pane = await ThisPcAsync();
            pane.Address = folder.FullName;
            await pane.GoToAddressAsync(TestContext.Current.CancellationToken);
            pane.Filter = "*.txt";
            var a = pane.Rows.Single(static row => row.Name == "a.txt");
            pane.SelectedRows.Add(a);

            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "c.txt"), "x", TestContext.Current.CancellationToken);
            await pane.RefreshQuietlyAsync(TestContext.Current.CancellationToken);

            Assert.Equal("*.txt", pane.Filter);
            Assert.Equal(["a.txt", "b.txt", "c.txt"], pane.Rows.Where(static row => !row.IsParentNavigation).Select(static row => row.Name));
            Assert.Same(a, pane.Rows.Single(static row => row.Name == "a.txt"));
            Assert.Contains(a, pane.SelectedRows);
            Assert.False(pane.ShowsLoading);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static async Task<BrowserPaneModel> ThisPcAsync()
    {
        var pane = new BrowserPaneModel(new Refusing(WorkspaceFakes.Summary("unused")));
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenAsync(pane.Connections.Single(static c => c.Kind == PaneContentKind.ThisPc), TestContext.Current.CancellationToken);
        return pane;
    }

    /// <summary>An agent whose one connection never answers.</summary>
    private sealed class Refusing(ConnectionSummary connection) : IRemoteStorageAgentClient
    {
        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionListResponse(StorageIpcContract.CurrentVersion, [connection]));

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResponse(
                StorageIpcContract.CurrentVersion, request.ConnectionId, Succeeded: false, 0,
                new StorageIpcFailure("storage.unreachable", StorageIpcFailureCategory.Unavailable, "The server did not answer.", true)));

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StorageListPageResponse(
                StorageIpcContract.CurrentVersion, request.ConnectionId, request.RelativePath, [], null,
                Failure: new StorageIpcFailure("storage.unreachable", StorageIpcFailureCategory.Unavailable, "The server did not answer.", true)));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
