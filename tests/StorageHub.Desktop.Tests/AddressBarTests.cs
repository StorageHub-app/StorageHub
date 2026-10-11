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

            // From Connections Home, which has no folders, something that is not a folder here is
            // told to choose a connection, and a folder here is opened on This PC.
            pane.Connection = pane.Connections.Single(static c => c.Kind == PaneContentKind.ConnectionsHome);
            pane.Address = "photos";
            await pane.GoToAddressAsync(TestContext.Current.CancellationToken);
            Assert.Equal(Ui.Pane.ChooseConnection, pane.Status);
            Assert.Equal(PaneContentKind.ConnectionsHome, pane.Connection?.Kind);

            pane.Address = folder.FullName;
            await pane.GoToAddressAsync(TestContext.Current.CancellationToken);
            Assert.Equal(PaneContentKind.ThisPc, pane.Connection?.Kind);
            Assert.Contains(pane.Rows, static row => row.Name == "a.txt");
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

    /// <summary>
    /// A connection refused on its server's identity offers Review trust where it happened, which
    /// opens that connection's Trust tab; any other failure does not.
    /// </summary>
    [AvaloniaFact]
    public async Task AFailureOnTheServersIdentityOffersReviewTrust()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var connection = WorkspaceFakes.Summary("Files");
        await using var offline = new BrowserPaneModel(new Refusing(connection)) { ReviewTrust = static _ => { } };
        await offline.LoadConnectionsAsync(cancellation);
        await offline.OpenConnectionAsync(connection.ConnectionId, cancellation);
        Assert.False(offline.ShowsReviewTrust);

        Guid? reviewed = null;
        await using var pane = new BrowserPaneModel(new Refusing(
            connection,
            new StorageIpcFailure("storage.trust.host_key_rejected", StorageIpcFailureCategory.Security, "Rejected.", false)))
        {
            ReviewTrust = id => reviewed = id
        };
        await pane.LoadConnectionsAsync(cancellation);
        await pane.OpenConnectionAsync(connection.ConnectionId, cancellation);

        Assert.True(pane.StatusIsWarning);
        Assert.True(pane.ShowsReviewTrust);
        pane.ReviewTrustCommand.Execute(null);
        Assert.Equal(connection.ConnectionId, reviewed);
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

            foreach (var (pane, name) in new[] { (empty, "pane-empty"), (failed, "pane-failed"), (empty, "pane-drop-target") })
            {
                var view = new BrowserPaneView { DataContext = pane };
                var window = new Avalonia.Controls.Window { Content = view, Width = 700, Height = 360 };
                if (name == "pane-drop-target")
                {
                    // Lit as a drag over it would light it, list and tree both, to see the colour.
                    Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.Control>(view, "PART_Rows")!.Classes.Add(PaneDragHandler.DropTargetClass);
                    Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.Control>(view, "PART_Tree")!.Classes.Add(PaneDragHandler.DropTargetClass);
                }

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

    /// <summary>
    /// With "Refresh a folder when it changes" on, a folder on this computer is watched, and a
    /// change shows once the folder has settled -- keeping the filter and selection -- but not
    /// while the pane is busy, when it is still owed and shown after.
    /// </summary>
    [AvaloniaFact]
    public async Task AWatchedFolderShowsWhatChangedOnceItSettlesAndWaitsWhileThePaneIsBusy()
    {
        var folder = Directory.CreateTempSubdirectory("storagehub-watched-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "a.txt"), "x", TestContext.Current.CancellationToken);
            var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
            var watched = new List<string>();
            Action? changed = null;
            var settings = FolderAutoRefreshSettings.From(true, 30);

            await using var pane = await ThisPcAsync();
            pane.Clock = () => now;
            pane.AutoRefreshSettings = () => settings;
            pane.FolderWatch = (path, report) =>
            {
                watched.Add(path);
                changed = report;
                return null;
            };
            pane.Address = folder.FullName;
            await pane.GoToAddressAsync(TestContext.Current.CancellationToken);
            Assert.Equal([folder.FullName], watched);
            pane.Filter = "*.txt";
            var a = pane.Rows.Single(static row => row.Name == "a.txt");
            pane.SelectedRows.Add(a);

            // Nothing changed: nothing is read, however long it waits.
            now = now.AddSeconds(5);
            Assert.False(await pane.AutoRefreshAsync());

            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "b.txt"), "x", TestContext.Current.CancellationToken);
            changed!();
            Assert.False(await pane.AutoRefreshAsync());
            now += FolderAutoRefresh.Settle;
            Assert.True(await pane.AutoRefreshAsync());

            Assert.Equal("*.txt", pane.Filter);
            Assert.Equal(["a.txt", "b.txt"], pane.Rows.Where(static row => !row.IsParentNavigation).Select(static row => row.Name));
            Assert.Same(a, pane.Rows.Single(static row => row.Name == "a.txt"));
            Assert.Contains(a, pane.SelectedRows);

            // Busy reading the folder for another reason: the change waits, and is shown after.
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "c.txt"), "x", TestContext.Current.CancellationToken);
            changed();
            now += FolderAutoRefresh.Settle;
            var going = pane.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.True(pane.IsBusy);
            Assert.False(await pane.AutoRefreshAsync());
            await going;
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "d.txt"), "x", TestContext.Current.CancellationToken);
            Assert.True(await pane.AutoRefreshAsync());
            Assert.Contains(pane.Rows, static row => row.Name == "d.txt");

            // Turned off in Settings: within a few seconds the folder is no longer followed.
            settings = FolderAutoRefreshSettings.Off;
            now += BrowserPaneModel.AutoRefreshSettingsReadEvery;
            changed();
            now += FolderAutoRefresh.Settle;
            Assert.False(await pane.AutoRefreshAsync());
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
    private sealed class Refusing(ConnectionSummary connection, StorageIpcFailure? failure = null) : IRemoteStorageAgentClient
    {
        private readonly StorageIpcFailure _failure = failure ??
            new StorageIpcFailure("storage.unreachable", StorageIpcFailureCategory.Unavailable, "The server did not answer.", true);

        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionListResponse(StorageIpcContract.CurrentVersion, [connection]));

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResponse(
                StorageIpcContract.CurrentVersion, request.ConnectionId, Succeeded: false, 0, _failure));

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StorageListPageResponse(
                StorageIpcContract.CurrentVersion, request.ConnectionId, request.RelativePath, [], null,
                Failure: _failure));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
