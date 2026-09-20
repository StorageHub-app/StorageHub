using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The connections panel's search box, which used to be decoration.
/// </summary>
/// <remarks>
/// It had no <c>Text</c> binding at all: it accepted typing and nothing read it. The matching is
/// <see cref="ConnectionPickerFilter"/>, which has been in Desktop.Core with its own tests and was
/// referenced from nowhere -- so what is tested here is the panel's half, not the matching.
/// </remarks>
public class ConnectionSearchTests
{
    [AvaloniaFact]
    public async Task TypingNarrowsThePanel()
    {
        var sidebar = Sidebar(Summary("Studio Assets"), Summary("Site Backups"), Summary("Archive"));
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        sidebar.Search = "site";

        Assert.Equal(["Site Backups"], Rows(sidebar));
        Assert.True(sidebar.HasSearch);
    }

    /// <summary>Every term has to match, so typing more narrows rather than widens.</summary>
    [AvaloniaFact]
    public async Task EveryTermHasToMatch()
    {
        var sidebar = Sidebar(Summary("Studio Assets"), Summary("Studio Renders"));
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        sidebar.Search = "studio";
        Assert.Equal(2, Rows(sidebar).Length);

        sidebar.Search = "studio renders";
        Assert.Equal(["Studio Renders"], Rows(sidebar));
    }

    [AvaloniaFact]
    public async Task ClearingTheSearchBringsEverythingBack()
    {
        var sidebar = Sidebar(Summary("Studio Assets"), Summary("Site Backups"));
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);
        sidebar.Search = "site";

        sidebar.ClearSearchCommand.Execute(null);

        Assert.False(sidebar.HasSearch);
        Assert.Equal(2, Rows(sidebar).Length);
    }

    /// <summary>
    /// A search matching nothing says so, rather than looking like an empty account.
    /// </summary>
    /// <remarks>
    /// An empty panel is the same picture whether nothing is saved or nothing matched, and those
    /// are very different situations to be in.
    /// </remarks>
    [AvaloniaFact]
    public async Task ASearchThatMatchesNothingSaysSo()
    {
        var sidebar = Sidebar(Summary("Studio Assets"));
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        sidebar.Search = "no-such-connection";

        Assert.True(sidebar.IsEmpty);
        Assert.Equal(Ui.Connections.NoMatches, sidebar.Status);
    }

    /// <summary>
    /// And it never overwrites why the agent could not be reached.
    /// </summary>
    /// <remarks>
    /// The regression this caught while being written: rebuilding for a keystroke set the status
    /// unconditionally, so typing into the box replaced "the agent is not connected" with "no
    /// saved connections yet" -- turning a diagnosable failure into a wrong statement of fact.
    /// </remarks>
    [AvaloniaFact]
    public async Task ASearchDoesNotOverwriteAnAgentFailure()
    {
        var sidebar = new ConnectionsSidebar(
            new RelayCommand(static _ => { }),
            static () => throw new IOException("the agent is not listening"));
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);
        var reported = sidebar.Status;

        Assert.Equal(Ui.Shell.AgentNotConnected, reported);

        sidebar.Search = "anything";

        Assert.Equal(reported, sidebar.Status);
    }

    /// <summary>A row opens in the active pane, which is what the shell hands the panel.</summary>
    [AvaloniaFact]
    public async Task ARowOpensTheConnectionItNames()
    {
        var connection = Summary("Studio Assets");
        var sidebar = Sidebar(connection);
        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        var opened = new List<Guid>();
        sidebar.OpenConnection = opened.Add;
        var row = sidebar.Groups.SelectMany(static group => group.Connections).Single();

        sidebar.OpenConnection(row.Id);

        Assert.Equal([connection.ConnectionId], opened);
    }

    private static string[] Rows(ConnectionsSidebar sidebar) =>
        [.. sidebar.Groups.SelectMany(static group => group.Connections).Select(static row => row.Name)];

    private static ConnectionsSidebar Sidebar(params ConnectionSummary[] connections) =>
        new(new RelayCommand(static _ => { }), () => new FixedAgent(connections));

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

    private sealed class FixedAgent(ConnectionSummary[] connections) : IRemoteStorageAgentClient
    {
        public Task<ConnectionListResponse> ListConnectionsAsync(
            ConnectionListRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConnectionListResponse(StorageIpcContract.CurrentVersion, connections));

        public Task<ConnectionTestResponse> TestConnectionAsync(
            ConnectionTestRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<StorageListPageResponse> ListStorageAsync(
            StorageListPageRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
