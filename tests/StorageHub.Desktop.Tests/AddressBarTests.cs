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
        await pane.OpenAsync(pane.Connections.Single(c => c.Id is null), TestContext.Current.CancellationToken);
        Assert.NotEmpty(pane.Rows);

        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);

        Assert.Empty(pane.Rows);
        Assert.True(pane.ConnectionStateIsFailed);
    }

    private static async Task<BrowserPaneModel> ThisPcAsync()
    {
        var pane = new BrowserPaneModel(new Refusing(WorkspaceFakes.Summary("unused")));
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenAsync(pane.Connections.Single(c => c.Id is null), TestContext.Current.CancellationToken);
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
