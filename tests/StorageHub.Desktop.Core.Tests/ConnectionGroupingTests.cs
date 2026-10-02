using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The arrangement the desktop kept before groups were the agent's, read so it can be brought
/// across, and the badge on a row.
/// </summary>
/// <remarks>
/// <para>
/// That split was the provider's classification showing through as an organising principle, and it
/// is not one: four buckets and two shells for the same project belong together, and no amount of
/// sorting inside two fixed lists gives that. What is left of the classification is a badge on the
/// row, which is where it is actually useful -- an SSH client and an SFTP connection to the same
/// host look identical without it.
/// </para>
/// <para>
/// The rules worth holding: a connection is in exactly one group, a connection that no longer
/// exists stops appearing, a new one lands where somebody would look for it, and no operation
/// loses track of a connection.
/// </para>
/// </remarks>
public class ConnectionGroupingTests
{
    [Fact]
    public void ConnectionsWithNoSavedArrangementAreGroupedByTheirFolder()
    {
        var team = Card("Studio Assets", folder: "Team");
        var loose = Card("Scratch");

        var groups = ConnectionGrouping.Arrange(saved: null, [team, loose]);

        Assert.Equal(["Team", ConnectionGrouping.DefaultGroupName], groups.Select(g => g.Name));
        Assert.Equal([team.ConnectionId!.Value], groups[0].Members);
        Assert.Equal([loose.ConnectionId!.Value], groups[1].Members);
    }

    /// <summary>Only the first segment: the panel is a flat set of groups, not a tree.</summary>
    [Fact]
    public void ANestedFolderPathBecomesItsFirstSegment()
    {
        var card = Card("Studio Assets", folder: "Team/Renders/2026");

        var groups = ConnectionGrouping.Arrange(saved: null, [card]);

        Assert.Equal("Team", Assert.Single(groups).Name);
    }

    /// <summary>The saved order is the order, and new connections go where their folder says.</summary>
    [Fact]
    public void ASavedArrangementKeepsItsOrderAndAbsorbsNewConnections()
    {
        var first = Card("One");
        var second = Card("Two");
        var arrived = Card("Three", folder: "Team");
        var saved = new ConnectionGroupEntry[]
        {
            new("Archive", [second.ConnectionId!.Value]),
            new("Live", [first.ConnectionId!.Value])
        };

        var groups = ConnectionGrouping.Arrange(saved, [first, second, arrived]);

        Assert.Equal(["Archive", "Live", "Team"], groups.Select(g => g.Name));
        Assert.Equal([arrived.ConnectionId!.Value], groups[2].Members);
    }

    /// <summary>A connection that was deleted stops appearing rather than leaving a gap.</summary>
    [Fact]
    public void AMemberThatNoLongerExistsIsDropped()
    {
        var kept = Card("One");
        var saved = new ConnectionGroupEntry[]
        {
            new("Live", [Guid.NewGuid(), kept.ConnectionId!.Value, Guid.NewGuid()])
        };

        var groups = ConnectionGrouping.Arrange(saved, [kept]);

        Assert.Equal([kept.ConnectionId!.Value], Assert.Single(groups).Members);
    }

    /// <summary>
    /// A connection listed in two groups ends up in the first.
    /// </summary>
    /// <remarks>
    /// A saved file can say anything, including something this cannot represent. Taking the first
    /// is arbitrary but total: the alternative is a panel that shows one connection twice and a
    /// count that does not match what is there.
    /// </remarks>
    [Fact]
    public void AConnectionInTwoSavedGroupsAppearsOnlyInTheFirst()
    {
        var card = Card("One");
        var id = card.ConnectionId!.Value;
        var saved = new ConnectionGroupEntry[] { new("Live", [id]), new("Archive", [id]) };

        var groups = ConnectionGrouping.Arrange(saved, [card]);

        Assert.Equal([id], groups[0].Members);
        Assert.Empty(groups[1].Members);
    }

    /// <summary>
    /// What is brought across to the agent once: the saved groups in their order, empty ones and
    /// icons included, then the folder groups nobody had filed elsewhere. The default group is left
    /// out, because Ungrouped is what it was.
    /// </summary>
    [Fact]
    public void TheOldArrangementIsBroughtAcrossWithoutItsDefaultGroup()
    {
        var live = Card("One");
        var loose = Card("Two");
        var arrived = Card("Three", folder: "Team");
        var saved = new ConnectionGroupEntry[]
        {
            new("Archive", []),
            new("Live", [live.ConnectionId!.Value]),
            new(ConnectionGrouping.DefaultGroupName, [loose.ConnectionId!.Value])
        };

        var entries = ConnectionGrouping.LegacyImport(
            saved,
            new Dictionary<string, string> { ["Live"] = "layers" },
            [live, loose, arrived]);

        Assert.Equal(["Archive", "Live", "Team"], entries.Select(static entry => entry.Name));
        Assert.Empty(entries[0].Members);
        Assert.Equal("layers", entries[1].IconKey);
        Assert.Equal([arrived.ConnectionId!.Value], entries[2].Members);
        Assert.DoesNotContain(entries, entry => entry.Members.Contains(loose.ConnectionId!.Value));
        Assert.Empty(ConnectionGrouping.LegacyImport(saved: null, icons: null, []));
    }

    /// <summary>The badge is what is left of the Storage and Clients split, on the row.</summary>
    [Theory]
    [InlineData(StorageProviderKind.S3, false)]
    [InlineData(StorageProviderKind.Sftp, false)]
    [InlineData(StorageProviderKind.Ssh, true)]
    public void TheBadgeSaysWhetherAConnectionIsAClient(StorageProviderKind provider, bool client)
    {
        var card = new ConnectionCardModel("A host", provider, "host", "Ready", ConnectionId: Guid.NewGuid());

        var badge = ConnectionGrouping.BadgeFor(card);

        Assert.Equal(client ? Ui.Connections.BadgeClient : Ui.Connections.BadgeStorage, badge);
    }

    private static ConnectionCardModel Card(string name, string? folder = null) => new(
        name,
        StorageProviderKind.S3,
        "endpoint",
        "Ready",
        ConnectionId: Guid.NewGuid(),
        FolderPath: folder);
}
