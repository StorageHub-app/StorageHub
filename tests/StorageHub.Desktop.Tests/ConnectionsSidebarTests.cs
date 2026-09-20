using System.Windows.Input;
using Avalonia.Headless.XUnit;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using StorageHub.Desktop.Views;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The connections sidebar, which is the first part of the shell holding real data.
/// </summary>
public class ConnectionsSidebarTests
{
    private sealed class NoCommand : ICommand
    {
        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }

    private static ConnectionsSidebar Sidebar(Func<IRemoteStorageAgentClient>? client) =>
        new(new NoCommand(), client);

    private static ConnectionSummary Summary(string name, bool enabled = true) => new(
        Guid.NewGuid(),
        name,
        StorageConnectionProvider.S3,
        "bucket/prefix",
        [],
        IsFavorite: false,
        IsEnabled: enabled,
        IconKey: null,
        AccentColor: null,
        Version: 1);

    [AvaloniaFact]
    public async Task ConnectionsFromTheAgentBecomeCards()
    {
        var sidebar = Sidebar(() => new FakeStorageClient(
            new ConnectionListResponse(StorageIpcContract.CurrentVersion,
                [Summary("Studio Assets"), Summary("Site Backups")])));

        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.False(sidebar.IsEmpty);

        // In a group now rather than in a flat list. With nothing arranged there is one group, so
        // this also says that an unarranged panel is one group and not one per connection.
        var group = Assert.Single(sidebar.Groups);
        Assert.Equal(["Studio Assets", "Site Backups"], group.Connections.Select(row => row.Name));

        // The projection is ConnectionCardFactory's, so the second line reads the same here as it
        // does everywhere else the same connections are listed.
        Assert.All(group.Connections, row => Assert.Contains("bucket/prefix", row.Endpoint, StringComparison.Ordinal));
    }

    /// <summary>
    /// Nothing saved and nothing answering are different, and must read differently.
    /// </summary>
    /// <remarks>
    /// Reporting "No saved connections yet" when the agent is down is how somebody spends an
    /// afternoon wondering where their connections went.
    /// </remarks>
    [AvaloniaFact]
    public async Task AnUnreachableAgentDoesNotReadAsAnEmptyAccount()
    {
        var sidebar = Sidebar(() => new FakeStorageClient(new InvalidOperationException("no agent")));

        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(sidebar.IsEmpty);
        Assert.Equal(Ui.Shell.AgentNotConnected, sidebar.Status);
        Assert.NotEqual(Ui.Connections.SidebarEmpty, sidebar.Status);
    }

    [AvaloniaFact]
    public async Task AnAccountWithNoConnectionsSaysSo()
    {
        var sidebar = Sidebar(() => new FakeStorageClient(
            new ConnectionListResponse(StorageIpcContract.CurrentVersion, [])));

        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(sidebar.IsEmpty);
        Assert.Equal(Ui.Connections.SidebarEmpty, sidebar.Status);
    }

    /// <summary>A refusal from the agent is shown, not swallowed.</summary>
    [AvaloniaFact]
    public async Task AFailureFromTheAgentIsReported()
    {
        var sidebar = Sidebar(() => new FakeStorageClient(
            new ConnectionListResponse(
                StorageIpcContract.CurrentVersion,
                [],
                new StorageIpcFailure("unauthorized", StorageIpcFailureCategory.Unauthorized, "refused", IsTransient: false))));

        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal("refused", sidebar.Status);
    }

    /// <summary>Disabled connections are listed, because hiding them hides why they stopped working.</summary>
    [AvaloniaFact]
    public async Task DisabledConnectionsAreStillListed()
    {
        var client = new FakeStorageClient(
            new ConnectionListResponse(StorageIpcContract.CurrentVersion, [Summary("Old NAS", enabled: false)]));
        var sidebar = Sidebar(() => client);

        await sidebar.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(client.LastRequest!.IncludeDisabled);
        var row = Assert.Single(Assert.Single(sidebar.Groups).Connections);
        Assert.False(row.IsEnabled);
    }
}
