using System.Globalization;
using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// A folder bigger than one page: read a page at a time, shown whole, and pasted into.
/// </summary>
/// <remarks>
/// A remote page is 40 entries, and 2.0 never asked for a second one. Any bigger folder showed its
/// first 40 and counted 40, and a paste into it was refused for not being fully read.
/// </remarks>
public class PanePagingTests
{
    [AvaloniaFact]
    public async Task AFolderBiggerThanAPageSaysSoAndReadsTheRestWhenAsked()
    {
        var (pane, _) = await OpenAsync(entries: 100);
        await using var _pane = pane;

        Assert.Equal(40, pane.Rows.Count);
        Assert.True(pane.HasMorePages);
        Assert.EndsWith(Localization.Ui.Pane.MoreAvailableSuffix, pane.ItemCount, StringComparison.Ordinal);
        Assert.True(pane.LoadMoreCommand.CanExecute(null));

        Assert.True(await pane.LoadMoreAsync(TestContext.Current.CancellationToken));
        Assert.Equal(80, pane.Rows.Count);

        Assert.True(await pane.LoadAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal(100, pane.Rows.Count);
        Assert.False(pane.HasMorePages);
        Assert.Equal(Localization.Ui.Format(Localization.Ui.Pane.ItemCountFormat, 100), pane.ItemCount);
    }

    /// <summary>The rows already shown stay where they are; a new page is merged in, not a rebuild.</summary>
    [AvaloniaFact]
    public async Task ANewPageKeepsTheRowsAlreadyShownAndWhatIsSelected()
    {
        var (pane, _) = await OpenAsync(entries: 60);
        await using var _pane = pane;
        var first = pane.Rows[0];
        pane.SelectedRows.Add(first);

        await pane.LoadMoreAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, pane.Rows[0]);
        Assert.Contains(first, pane.SelectedRows);
        Assert.Equal(pane.Rows.OrderBy(static row => row.Name, StringComparer.OrdinalIgnoreCase), pane.Rows);
    }

    /// <summary>What a paste checks for clashes is the whole folder, not what a filter is showing.</summary>
    [AvaloniaFact]
    public async Task AllRowsIgnoresTheFilter()
    {
        var (pane, _) = await OpenAsync(entries: 10);
        await using var _pane = pane;

        pane.Filter = "file-001*";

        Assert.Single(pane.Rows);
        Assert.Equal(10, pane.AllRows.Count);
    }

    private static async Task<(BrowserPaneModel Pane, PagedAgent Agent)> OpenAsync(int entries)
    {
        var connection = WorkspaceFakes.Summary("Big bucket");
        var agent = new PagedAgent(connection, entries);
        var pane = new BrowserPaneModel(agent);
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);
        return (pane, agent);
    }

    /// <summary>A bucket of files named file-000 upwards, served 40 at a time as the agent does.</summary>
    private sealed class PagedAgent(ConnectionSummary connection, int entries) : IRemoteStorageAgentClient
    {
        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionListResponse(StorageIpcContract.CurrentVersion, [connection]));

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionTestResponse(
                StorageIpcContract.CurrentVersion, request.ConnectionId, Succeeded: true, 1));

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default)
        {
            var start = request.ContinuationToken is { } token ? int.Parse(token, CultureInfo.InvariantCulture) : 0;
            var page = Enumerable.Range(start, Math.Min(40, Math.Max(0, entries - start)))
                .Select(static index => WorkspaceFakes.Entry($"file-{index:000}.bin", 10))
                .ToArray();
            var next = start + page.Length < entries
                ? (start + page.Length).ToString(CultureInfo.InvariantCulture)
                : null;
            return Task.FromResult(new StorageListPageResponse(
                StorageIpcContract.CurrentVersion,
                request.ConnectionId,
                request.RelativePath,
                page,
                next,
                RootIdentity: "root"));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
