using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// The connection picker's grouping, order and keyboard rules.
/// </summary>
/// <remarks>
/// What a query matches is ConnectionPickerFilterTests' job. These are the rules that were event
/// handlers on a ToolStripDropDown in 1.x and so could not be tested at all: where the highlight
/// starts, that it never rests on a heading, and what typing does to it.
/// </remarks>
public sealed class ConnectionPickerSessionTests
{
    private static readonly ConnectionCardModel Computer = ConnectionPickerFilter.ThisComputer();

    [Fact]
    public void ThisComputerIsOnThisDevice()
    {
        Assert.Equal(Ui.Pane.OnThisDevice, ConnectionPickerFilter.GroupLabel(Computer));
        Assert.Null(Computer.ConnectionId);
    }

    [Fact]
    public void AFavouriteIsGroupedAsOneWhateverItsFolder()
    {
        Assert.Equal(Ui.Pane.Favorites, ConnectionPickerFilter.GroupLabel(Card("Photos", favorite: true, folder: "Team")));
    }

    [Fact]
    public void AFolderIsItsOwnGroup()
    {
        Assert.Equal("Team", ConnectionPickerFilter.GroupLabel(Card("Photos", folder: " Team ")));
    }

    /// <summary>Without a folder, storage and shells are grouped by provider, apart.</summary>
    [Fact]
    public void WithoutAFolderTheProviderGroups()
    {
        var storage = Card("Archive", StorageProviderKind.S3);
        var shell = Card("Build box", StorageProviderKind.Ssh);

        Assert.Equal(Ui.Format(Ui.Pane.StorageGroupFormat, storage.Descriptor.DisplayName), ConnectionPickerFilter.GroupLabel(storage));
        Assert.Equal(Ui.Format(Ui.Pane.ClientGroupFormat, shell.Descriptor.DisplayName), ConnectionPickerFilter.GroupLabel(shell));
    }

    /// <summary>
    /// Every group is one run, so its heading appears once however the cards arrived.
    /// </summary>
    /// <remarks>
    /// Headings are written whenever the group changes, so an interleaved input would split a
    /// group in two and repeat its heading. 1.x left the order to its caller.
    /// </remarks>
    [Fact]
    public void EachHeadingAppearsOnce()
    {
        var session = new ConnectionPickerSession(
            [Card("Zeta", folder: "Team"), Card("Archive", StorageProviderKind.S3), Card("Alpha", folder: "Team"),
             Computer, Card("Beta", StorageProviderKind.S3)],
            active: null);

        var headings = session.Rows.Where(row => row.IsHeader).Select(row => row.GroupLabel).ToArray();

        Assert.Equal(headings.Distinct().Count(), headings.Length);
    }

    [Fact]
    public void ThisComputerComesFirstAndFavouritesNext()
    {
        var ordered = ConnectionPickerFilter.Order(
            [Card("Zeta"), Card("Starred", favorite: true), Computer, Card("Off", enabled: false)]);

        Assert.Equal(["This PC", "Starred", "Zeta", "Off"], ordered.Select(card => card.ConnectionId is null ? "This PC" : card.Name));
    }

    [Fact]
    public void NamesAreInOrderWithinAGroup()
    {
        var ordered = ConnectionPickerFilter.Order([Card("beta", folder: "Team"), Card("Alpha", folder: "Team")]);

        Assert.Equal(["Alpha", "beta"], ordered.Select(card => card.Name));
    }

    /// <summary>The highlight starts on what the pane already has open.</summary>
    [Fact]
    public void TheHighlightStartsOnTheActiveConnection()
    {
        var open = Card("Photos");
        var session = new ConnectionPickerSession([Computer, Card("Archive"), open], open);

        Assert.Same(open, session.HighlightedCard);
    }

    /// <summary>This computer has no id, so it is recognised as active by its name.</summary>
    [Fact]
    public void ThisComputerCanBeTheActiveOne()
    {
        var session = new ConnectionPickerSession([Card("Archive"), Computer], Computer);

        Assert.Same(Computer, session.HighlightedCard);
        Assert.True(session.IsActive(Computer));
    }

    /// <summary>With nothing open, the first connection -- never a heading.</summary>
    [Fact]
    public void WithNothingOpenTheFirstConnectionIsHighlighted()
    {
        var session = new ConnectionPickerSession([Card("Archive"), Computer], active: null);

        Assert.True(session.Rows[0].IsHeader);
        Assert.Equal(1, session.Highlighted);
        Assert.Same(Computer, session.HighlightedCard);
    }

    /// <summary>The arrow keys step over headings, and stop at the ends rather than wrapping.</summary>
    [Fact]
    public void MovingStepsOverHeadingsAndStopsAtTheEnds()
    {
        var session = new ConnectionPickerSession(
            [Computer, Card("Archive", folder: "Team"), Card("Photos", folder: "Work")],
            active: null);

        session.Move(1);
        Assert.Equal("Archive", session.HighlightedCard!.Name);
        session.Move(1);
        Assert.Equal("Photos", session.HighlightedCard!.Name);
        session.Move(1);
        Assert.Equal("Photos", session.HighlightedCard!.Name);

        session.Move(-1);
        session.Move(-1);
        session.Move(-1);
        Assert.Same(Computer, session.HighlightedCard);
    }

    /// <summary>A pointer over a heading does not take the highlight off a connection.</summary>
    [Fact]
    public void AHeadingCannotBeHighlighted()
    {
        var session = new ConnectionPickerSession([Computer, Card("Archive")], active: null);
        var before = session.Highlighted;

        session.HighlightRow(0);

        Assert.Equal(before, session.Highlighted);
    }

    /// <summary>Typing keeps the highlight where it was, while that connection still matches.</summary>
    [Fact]
    public void TypingKeepsAHighlightThatStillMatches()
    {
        var photos = Card("Photos backup");
        var session = new ConnectionPickerSession([Computer, Card("Archive"), photos], active: null);
        session.HighlightRow(session.Rows.ToList().FindIndex(row => ReferenceEquals(row.Card, photos)));

        session.Query = "backup";

        Assert.Same(photos, session.HighlightedCard);
    }

    /// <summary>When it stops matching, the highlight goes to the first connection that does.</summary>
    [Fact]
    public void TypingMovesAHighlightThatNoLongerMatches()
    {
        var session = new ConnectionPickerSession([Computer, Card("Archive"), Card("Photos")], active: null);

        session.Query = "photo";

        Assert.Equal("Photos", session.HighlightedCard!.Name);
    }

    [Fact]
    public void NothingMatchingIsEmptyWithNothingHighlighted()
    {
        var session = new ConnectionPickerSession([Computer, Card("Archive")], active: null);

        session.Query = "no such thing";

        Assert.True(session.IsEmpty);
        Assert.Equal(-1, session.Highlighted);
        Assert.Null(session.HighlightedCard);
    }

    /// <summary>
    /// A card's state and summary come from the strings.
    /// </summary>
    /// <remarks>
    /// They were English literals after the port, although every one of them had a translated
    /// counterpart in the pane's strings.
    /// </remarks>
    [Fact]
    public void ACardIsDescribedFromTheStrings()
    {
        var connection = new ConnectionSummary(
            Guid.NewGuid(), "Archive", StorageConnectionProvider.S3, FolderPath: null, Tags: [],
            IsFavorite: false, IsEnabled: true, IconKey: null, AccentColor: null, Version: 1);

        var card = ConnectionCardFactory.Create(connection);

        Assert.Equal(Ui.Pane.NotTested, card.State);
        Assert.Equal(
            Ui.Format(Ui.Pane.SavedProfileSummaryFormat, ConnectionProviderCatalog.Get(StorageProviderKind.S3).DisplayName),
            card.Endpoint);
        Assert.Equal(Ui.Pane.Unavailable, ConnectionCardFactory.DescribeHealth(
            new ConnectionHealthSnapshot(ConnectionHealthState.Unavailable, DateTimeOffset.UnixEpoch, 0, "unreachable")));
    }

    private static ConnectionCardModel Card(
        string name,
        StorageProviderKind provider = StorageProviderKind.Sftp,
        bool favorite = false,
        string? folder = null,
        bool enabled = true) =>
        new(name, provider, $"{name.ToLowerInvariant()}.example", "Ready", favorite, Guid.NewGuid(), enabled, FolderPath: folder);
}
