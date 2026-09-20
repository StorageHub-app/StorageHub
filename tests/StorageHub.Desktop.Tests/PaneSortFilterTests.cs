using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;
using static StorageHub.Desktop.Tests.WorkspaceFakes;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Sorting, filtering and selecting a listing.
/// </summary>
/// <remarks>
/// <para>
/// All of it runs through <see cref="PagedListingIndex"/>, which is in Desktop.Core, has its own
/// suite and is what 1.x sorted through. That is the point: "folders first, then by name,
/// case-insensitively" is a rule a pane could easily re-derive slightly differently, and a listing
/// that comes out in a different order on one platform than the other is exactly the kind of
/// difference somebody notices only while comparing two panes.
/// </para>
/// <para>
/// What is tested here is the pane's half: which column is sorted by, what a heading says about
/// it, what a filter leaves visible, and what select-all means when a filter is on.
/// </para>
/// </remarks>
public class PaneSortFilterTests
{
    /// <summary>Whatever the provider's order, the pane shows folders first and then by name.</summary>
    [AvaloniaFact]
    public async Task AListingArrivesSortedRegardlessOfWhatTheProviderSaid()
    {
        await using var pane = await OpenedAsync();

        Assert.Equal(
            ["assets", "reports", "alpha.txt", "beta.bin", "gamma.log"],
            pane.Rows.Select(static row => row.Name));
        Assert.Equal(BrowserSortColumn.Name, pane.SortColumn);
        Assert.True(pane.SortAscending);
    }

    /// <summary>Sorting by the column already sorted by turns it round.</summary>
    [AvaloniaFact]
    public async Task SortingByTheSameColumnTwiceReversesIt()
    {
        await using var pane = await OpenedAsync();

        pane.SortBy(BrowserSortColumn.Name);

        Assert.False(pane.SortAscending);
        Assert.Equal(
            ["reports", "assets", "gamma.log", "beta.bin", "alpha.txt"],
            pane.Rows.Select(static row => row.Name));
    }

    /// <summary>
    /// A new column starts ascending rather than inheriting the last direction.
    /// </summary>
    /// <remarks>
    /// "Sort by size" means smallest first until somebody says otherwise. Carrying a descending
    /// name sort into it would answer a question nobody asked, and would leave the arrow pointing
    /// at an order nobody chose.
    /// </remarks>
    [AvaloniaFact]
    public async Task ANewColumnStartsAscending()
    {
        await using var pane = await OpenedAsync();
        pane.SortBy(BrowserSortColumn.Name);
        Assert.False(pane.SortAscending);

        pane.SortBy(BrowserSortColumn.Size);

        Assert.Equal(BrowserSortColumn.Size, pane.SortColumn);
        Assert.True(pane.SortAscending);

        // Folders still lead, whichever column is sorted by: they have no size to compare.
        Assert.Equal(["assets", "reports"], pane.Rows.Take(2).Select(static row => row.Name));
        Assert.Equal(
            ["alpha.txt", "gamma.log", "beta.bin"],
            pane.Rows.Skip(2).Select(static row => row.Name));
    }

    /// <summary>The sorted column is the one wearing the arrow, and only that one.</summary>
    [AvaloniaFact]
    public async Task OnlyTheSortedColumnCarriesAnArrow()
    {
        await using var pane = await OpenedAsync();

        Assert.StartsWith(Ui.Pane.ColumnName, pane.NameHeader, StringComparison.Ordinal);
        Assert.NotEqual(Ui.Pane.ColumnName, pane.NameHeader);
        Assert.Equal(Ui.Pane.ColumnSize, pane.SizeHeader);

        pane.SortBy(BrowserSortColumn.Size);

        Assert.Equal(Ui.Pane.ColumnName, pane.NameHeader);
        Assert.NotEqual(Ui.Pane.ColumnSize, pane.SizeHeader);
    }

    [AvaloniaFact]
    public async Task AFilterNarrowsTheListingWithoutAskingTheAgentAgain()
    {
        await using var pane = await OpenedAsync();

        pane.Filter = "a";

        // A bare term matches anywhere in the name; only an explicit wildcard anchors. Still
        // sorted, because the filter narrows the same view rather than making a new one.
        Assert.Equal(
            ["assets", "alpha.txt", "beta.bin", "gamma.log"],
            pane.Rows.Select(static row => row.Name));
        Assert.True(pane.HasFilter);

        pane.Filter = string.Empty;

        Assert.Equal(5, pane.Rows.Count);
    }

    /// <summary>Wildcards, the same ones 1.x accepted.</summary>
    [AvaloniaTheory]
    [InlineData("*.txt", new[] { "alpha.txt" })]
    [InlineData("?eta*", new[] { "beta.bin" })]
    [InlineData("re*", new[] { "reports" })]
    public async Task AFilterUnderstandsWildcards(string filter, string[] expected)
    {
        await using var pane = await OpenedAsync();

        pane.Filter = filter;

        Assert.Equal(expected, pane.Rows.Select(static row => row.Name));
    }

    /// <summary>A filter that matches nothing says so rather than looking like an empty folder.</summary>
    [AvaloniaFact]
    public async Task AFilterThatMatchesNothingSaysWhyTheListingIsEmpty()
    {
        await using var pane = await OpenedAsync();

        pane.Filter = "no-such-thing";

        Assert.Empty(pane.Rows);
        Assert.Equal(Ui.Pane.NoItemsMatchFilter, pane.Status);
    }

    /// <summary>
    /// Entering a folder clears the filter.
    /// </summary>
    /// <remarks>
    /// Otherwise somebody arrives in a folder that looks empty and is not, with the reason two
    /// controls away from where they are looking.
    /// </remarks>
    [AvaloniaFact]
    public async Task EnteringAFolderClearsTheFilter()
    {
        await using var pane = await OpenedAsync();
        pane.Filter = "a";

        await pane.NavigateAsync("reports", TestContext.Current.CancellationToken);

        Assert.False(pane.HasFilter);
        Assert.Equal(string.Empty, pane.Filter);
    }

    /// <summary>Select all takes the listing, and never the way back out of it.</summary>
    [AvaloniaFact]
    public async Task SelectAllTakesEverythingButTheParentRow()
    {
        await using var pane = await OpenedAsync();
        await pane.NavigateAsync("reports", TestContext.Current.CancellationToken);
        Assert.Contains(pane.Rows, static row => row.IsParentNavigation);

        pane.SelectAll();

        Assert.DoesNotContain(pane.SelectedRows, static row => row.IsParentNavigation);
        Assert.Equal(pane.Rows.Count - 1, pane.SelectedRows.Count);
    }

    /// <summary>
    /// Select all takes what the filter is showing, not what it is hiding.
    /// </summary>
    /// <remarks>
    /// The one way a filter could make a delete worse than no filter: selecting rows nobody can
    /// see. Whatever acts on a selection next has to be able to trust that the selection is what
    /// was on screen.
    /// </remarks>
    [AvaloniaFact]
    public async Task SelectAllRespectsTheFilter()
    {
        await using var pane = await OpenedAsync();
        pane.Filter = "*.txt";

        pane.SelectAll();

        Assert.Equal(["alpha.txt"], pane.SelectedRows.Select(static row => row.Name));
    }

    [AvaloniaFact]
    public async Task InvertingSwapsWhatIsSelectedForWhatIsNot()
    {
        await using var pane = await OpenedAsync();
        pane.SelectedRows.Add(pane.Rows.Single(static row => row.Name == "alpha.txt"));

        pane.InvertSelection();

        Assert.Equal(
            ["assets", "reports", "beta.bin", "gamma.log"],
            pane.SelectedRows.Select(static row => row.Name));
    }

    /// <summary>
    /// Re-sorting keeps what was selected selected.
    /// </summary>
    /// <remarks>
    /// The rows are rebuilt from the index on every sort, and the index hands back decoded copies
    /// rather than the instances that went in. Selection therefore has to be restored by location,
    /// or clicking a column heading would quietly empty it.
    /// </remarks>
    [AvaloniaFact]
    public async Task SortingKeepsTheSelection()
    {
        await using var pane = await OpenedAsync();
        pane.SelectedRows.Add(pane.Rows.Single(static row => row.Name == "beta.bin"));

        pane.SortBy(BrowserSortColumn.Size);

        Assert.Equal(["beta.bin"], pane.SelectedRows.Select(static row => row.Name));
    }

    /// <summary>A terminal pane has no listing, so neither command offers anything.</summary>
    [AvaloniaFact]
    public async Task ATerminalPaneHasNothingToSelect()
    {
        var shell = Summary("build-box", StorageConnectionProvider.Ssh, ConnectionProfileType.Client);
        var agent = new FakeBrowsingAgent([shell]);
        await using var pane = new BrowserPaneModel(agent);
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);

        await pane.OpenConnectionAsync(shell.ConnectionId, TestContext.Current.CancellationToken);

        Assert.False(pane.SelectAllCommand.CanExecute(null));
        Assert.False(pane.InvertSelectionCommand.CanExecute(null));
    }

    /// <summary>Two folders and three files, deliberately not in the order they should appear.</summary>
    private static async Task<BrowserPaneModel> OpenedAsync()
    {
        var connection = Summary("Studio Assets");
        var agent = new FakeBrowsingAgent([connection]);
        agent.Listings[(connection.ConnectionId, "")] = new Page(
        [
            Entry("gamma.log", 300),
            Entry("reports", container: true),
            Entry("alpha.txt", 100),
            Entry("assets", container: true),
            Entry("beta.bin", 900)
        ]);
        agent.Listings[(connection.ConnectionId, "reports")] = new Page(
            [Entry("q1.pdf", 2048, parent: "reports"), Entry("q2.pdf", 4096, parent: "reports")]);

        var pane = new BrowserPaneModel(agent);
        await pane.LoadConnectionsAsync(TestContext.Current.CancellationToken);
        await pane.OpenConnectionAsync(connection.ConnectionId, TestContext.Current.CancellationToken);
        return pane;
    }
}
