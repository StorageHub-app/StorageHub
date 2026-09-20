using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Go ▸ Favorites lists connections the active pane can actually open. The selection is a pure
/// static so that rule is testable without a window or a pipe.
/// </summary>
public sealed class FavoriteConnectionMenuTests
{
    [Fact]
    public void OnlyFavoritesAreOffered()
    {
        var selected = FavoriteConnectionMenu.Select(
            [Connection("Kept"), Connection("Skipped", isFavorite: false)]);

        Assert.Equal("Kept", Assert.Single(selected).DisplayName);
    }

    [Fact]
    public void ADisabledFavoriteIsNotOffered()
    {
        // A disabled profile cannot be opened, so listing it would be a dead menu entry.
        Assert.Empty(FavoriteConnectionMenu.Select(
            [Connection("Retired", isEnabled: false)]));
    }

    [Fact]
    public void ClientConnectionsAreOfferedOnlyWhenAPaneCanOpenThem()
    {
        var selected = FavoriteConnectionMenu.Select(
            [
                Connection("Shell", type: ConnectionProfileType.Client, provider: StorageConnectionProvider.Ssh),
                Connection("Bucket client", type: ConnectionProfileType.Client, provider: StorageConnectionProvider.S3)
            ]);

        Assert.Equal("Shell", Assert.Single(selected).DisplayName);
    }

    [Fact]
    public void StorageConnectionsAreOfferedWhateverTheirProvider()
    {
        var selected = FavoriteConnectionMenu.Select(
            [
                Connection("Bucket", provider: StorageConnectionProvider.S3),
                Connection("Drop", provider: StorageConnectionProvider.Ftp)
            ]);

        Assert.Equal(2, selected.Count);
    }

    [Fact]
    public void FavoritesAreListedByNameAndCapped()
    {
        var selected = FavoriteConnectionMenu.Select(
            [Connection("zulu"), Connection("Alpha"), Connection("mike")]);

        Assert.Equal(["Alpha", "mike", "zulu"], selected.Select(connection => connection.DisplayName));
        Assert.Equal(
            2,
            FavoriteConnectionMenu.Select(
                [Connection("a"), Connection("b"), Connection("c")], maximum: 2).Count);
    }

    [Fact]
    public void AnEmptyOrMissingCacheYieldsNothingRatherThanThrowing()
    {
        // The menu can open before the overview's first refresh has filled its cache.
        Assert.Empty(FavoriteConnectionMenu.Select(null));
        Assert.Empty(FavoriteConnectionMenu.Select([]));
        Assert.Empty(FavoriteConnectionMenu.Select([Connection("a")], maximum: 0));
    }

    private static ConnectionSummary Connection(
        string name,
        bool isFavorite = true,
        bool isEnabled = true,
        ConnectionProfileType type = ConnectionProfileType.Storage,
        StorageConnectionProvider provider = StorageConnectionProvider.Local) =>
        new(
            Guid.NewGuid(),
            name,
            provider,
            FolderPath: null,
            Tags: [],
            isFavorite,
            isEnabled,
            IconKey: null,
            AccentColor: null,
            Version: 1,
            type);
}
