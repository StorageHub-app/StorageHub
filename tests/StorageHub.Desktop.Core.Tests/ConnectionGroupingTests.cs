using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Groups somebody made, instead of the Storage and Clients split.
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

    /// <summary>An empty arrangement still has somewhere for the next connection to land.</summary>
    [Fact]
    public void AnArrangementIsNeverEmpty()
    {
        var groups = ConnectionGrouping.Arrange(saved: null, []);

        Assert.Equal(ConnectionGrouping.DefaultGroupName, Assert.Single(groups).Name);
    }

    [Fact]
    public void MovingAConnectionTakesItOutOfWhereItWas()
    {
        var card = Card("One");
        var id = card.ConnectionId!.Value;
        var groups = ConnectionGrouping.Arrange(null, [card]);
        groups = ConnectionGrouping.Add(groups, "Archive");

        groups = ConnectionGrouping.Move(groups, id, "Archive", 0);

        Assert.Empty(groups[0].Members);
        Assert.Equal([id], groups[1].Members);
    }

    /// <summary>Dropping onto a group that does not exist yet makes it.</summary>
    [Fact]
    public void MovingIntoAnUnknownGroupCreatesIt()
    {
        var card = Card("One");
        var groups = ConnectionGrouping.Arrange(null, [card]);

        groups = ConnectionGrouping.Move(groups, card.ConnectionId!.Value, "Archive", 0);

        Assert.Equal("Archive", groups[^1].Name);
        Assert.Equal([card.ConnectionId!.Value], groups[^1].Members);
    }

    /// <summary>Dropping between two rows puts it between them.</summary>
    [Fact]
    public void MovingWithinAGroupReordersIt()
    {
        var one = Card("One");
        var two = Card("Two");
        var three = Card("Three");
        var groups = ConnectionGrouping.Arrange(null, [one, two, three]);
        var name = groups[0].Name;

        groups = ConnectionGrouping.Move(groups, three.ConnectionId!.Value, name, 0);

        Assert.Equal(
            [three.ConnectionId!.Value, one.ConnectionId!.Value, two.ConnectionId!.Value],
            groups[0].Members);
    }

    /// <summary>An index past the end lands at the end rather than throwing.</summary>
    [Fact]
    public void MovingPastTheEndLandsAtTheEnd()
    {
        var one = Card("One");
        var two = Card("Two");
        var groups = ConnectionGrouping.Arrange(null, [one, two]);
        var name = groups[0].Name;

        groups = ConnectionGrouping.Move(groups, one.ConnectionId!.Value, name, 99);

        Assert.Equal([two.ConnectionId!.Value, one.ConnectionId!.Value], groups[0].Members);
    }

    [Fact]
    public void AddingAGroupThatAlreadyExistsChangesNothing()
    {
        var groups = ConnectionGrouping.Arrange(null, []);
        groups = ConnectionGrouping.Add(groups, "Archive");

        var again = ConnectionGrouping.Add(groups, "archive");

        Assert.Equal(2, again.Count);
    }

    [Fact]
    public void RenamingKeepsThePlaceAndTheContents()
    {
        var card = Card("One");
        var groups = ConnectionGrouping.Arrange(null, [card]);
        groups = ConnectionGrouping.Add(groups, "Archive");

        groups = ConnectionGrouping.Rename(groups, groups[0].Name, "Live");

        Assert.Equal(["Live", "Archive"], groups.Select(g => g.Name));
        Assert.Equal([card.ConnectionId!.Value], groups[0].Members);
    }

    /// <summary>Two groups with one name is a state nothing else here can represent.</summary>
    [Fact]
    public void RenamingOntoAnExistingNameIsRefused()
    {
        var groups = ConnectionGrouping.Arrange(null, []);
        groups = ConnectionGrouping.Add(groups, "Archive");

        var after = ConnectionGrouping.Rename(groups, "Archive", groups[0].Name);

        Assert.Equal(groups.Select(g => g.Name), after.Select(g => g.Name));
    }

    /// <summary>Removing a group tidies the panel and never loses a connection.</summary>
    [Fact]
    public void RemovingAGroupKeepsWhatWasInIt()
    {
        var card = Card("One");
        var groups = ConnectionGrouping.Arrange(null, [card]);
        groups = ConnectionGrouping.Add(groups, "Archive");
        groups = ConnectionGrouping.Move(groups, card.ConnectionId!.Value, "Archive", 0);

        groups = ConnectionGrouping.Remove(groups, "Archive");

        var remaining = Assert.Single(groups);
        Assert.Equal([card.ConnectionId!.Value], remaining.Members);
    }

    /// <summary>The last group stays, because then there would be nowhere for anything to be.</summary>
    [Fact]
    public void TheLastGroupCannotBeRemoved()
    {
        var groups = ConnectionGrouping.Arrange(null, []);

        var after = ConnectionGrouping.Remove(groups, groups[0].Name);

        Assert.Single(after);
    }

    [Fact]
    public void AGroupCanBeMovedUpThePanel()
    {
        var groups = ConnectionGrouping.Arrange(null, []);
        groups = ConnectionGrouping.Add(groups, "Archive");

        groups = ConnectionGrouping.Reorder(groups, "Archive", 0);

        Assert.Equal("Archive", groups[0].Name);
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
