namespace StorageHub.Desktop.Tests;

/// <summary>
/// What a rounded control fills the space outside its corners with.
/// </summary>
/// <remarks>
/// A rounded corner is a lie told with two colours: the control paints the shape, and it paints
/// the leftover corner in whatever is behind it so the shape looks cut out rather than drawn on a
/// square. Get the second colour wrong and every corner grows a small block of it -- which is what
/// happened on the settings pages, where a card paints itself SurfaceMuted but reports
/// Transparent, so fields on it filled their corners with the page's darker Surface.
/// </remarks>
public sealed class FieldBackdropTests
{
    [Fact]
    public void AFieldOnACardFillsItsCornersWithTheCardsColour()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var page = new Panel { BackColor = StorageHubTheme.Surface };
            using var card = new SettingsCard();
            using var field = new StorageHubTextField();
            page.Controls.Add(card);
            card.Controls.Add(field);

            Assert.Equal(StorageHubFieldChrome.CardFill, StorageHubFieldChrome.Backdrop(field));
            Assert.NotEqual(page.BackColor, StorageHubFieldChrome.Backdrop(field));
        });
    }

    [Fact]
    public void AFieldOnAPlainSurfaceStillFollowsItsBackColor()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var host = new Panel { BackColor = StorageHubTheme.Canvas };
            using var field = new StorageHubTextField();
            host.Controls.Add(field);

            Assert.Equal(StorageHubTheme.Canvas, StorageHubFieldChrome.Backdrop(field));
        });
    }

    [Fact]
    public void ATransparentContainerIsLookedThrough()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            // A row paints nothing of its own, so the answer has to come from further up.
            using var page = new Panel { BackColor = StorageHubTheme.Surface };
            using var card = new SettingsCard();
            using var row = new Panel { BackColor = Color.Transparent };
            using var field = new StorageHubNumberField();
            page.Controls.Add(card);
            card.Controls.Add(row);
            row.Controls.Add(field);

            Assert.Equal(StorageHubFieldChrome.CardFill, StorageHubFieldChrome.Backdrop(field));
        });
    }
}
