using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop.Tests;

public sealed class ConnectionIconCatalogTests
{
    [Fact]
    public void A_chosen_icon_wins_over_the_provider_default()
    {
        var glyph = ConnectionIconCatalog.ResolveForConnection(
            "shield",
            StorageProviderKind.S3,
            ConnectionProfileType.Storage);

        Assert.Equal(UiGlyph.Shield, glyph);
    }

    [Theory]
    [InlineData(StorageProviderKind.Local, ConnectionProfileType.Storage, UiGlyph.Home)]
    [InlineData(StorageProviderKind.Sftp, ConnectionProfileType.Storage, UiGlyph.Server)]
    [InlineData(StorageProviderKind.S3, ConnectionProfileType.Storage, UiGlyph.Cloud)]
    [InlineData(StorageProviderKind.Ssh, ConnectionProfileType.Client, UiGlyph.Terminal)]
    public void A_connection_without_a_chosen_icon_falls_back_to_its_provider(
        StorageProviderKind provider, ConnectionProfileType type, UiGlyph expected)
    {
        // The fallback is what keeps every existing profile looking right without anyone having to
        // pick an icon for it first.
        Assert.Equal(expected, ConnectionIconCatalog.ResolveForConnection(null, provider, type));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-icon-this-build-has")]
    public void An_unknown_key_degrades_to_the_provider_default(string key)
    {
        // Keys are free text on the wire and in the database, so one written by a newer build must
        // not break the row -- it just loses the choice.
        Assert.Equal(
            UiGlyph.Cloud,
            ConnectionIconCatalog.ResolveForConnection(key, StorageProviderKind.S3, ConnectionProfileType.Storage));
        Assert.Null(ConnectionIconCatalog.Resolve(key));
    }

    [Fact]
    public void Keys_are_matched_without_regard_to_case_or_padding()
    {
        Assert.Equal(UiGlyph.Key, ConnectionIconCatalog.Resolve("  KEY  "));
    }

    [Fact]
    public void Every_offered_icon_round_trips_through_its_key()
    {
        foreach (var (key, glyph) in ConnectionIconCatalog.Choices)
        {
            Assert.Equal(glyph, ConnectionIconCatalog.Resolve(key));
            Assert.Equal(key, ConnectionIconCatalog.KeyFor(glyph));
        }
    }

    [Fact]
    public void The_offered_keys_are_unique_and_fit_the_profile_field()
    {
        var keys = ConnectionIconCatalog.Choices.Select(static choice => choice.Key).ToArray();

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // ConnectionProfileMetadata caps IconKey at 64 characters and rejects control characters.
        Assert.All(keys, key =>
        {
            Assert.InRange(key.Length, 1, 64);
            Assert.DoesNotContain(key, char.IsControl);
        });
    }
}
