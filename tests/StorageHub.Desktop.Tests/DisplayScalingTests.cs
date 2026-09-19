namespace StorageHub.Desktop.Tests;

/// <summary>
/// Guards the defect this project keeps producing: a metric written as a literal pixel.
///
/// The fonts scale with the display and literal pixels do not, so the two drift apart by exactly
/// the scaling factor -- which is why a shell authored at 125% came apart at 100% and back again.
/// These run at whatever scaling the machine is set to, so on a 100% agent most of them are
/// checking an identity; the ones that compare a conversion against the display's own factor hold
/// either way.
/// </summary>
public sealed class DisplayScalingTests
{
    private static float Scale(Control control) => control.DeviceDpi / 96F;

    /// <summary>
    /// The one assertion here that does not depend on the machine's own scaling: the factor is
    /// given rather than read, so this fails on a 100% agent as readily as on a 125% one.
    /// </summary>
    [Theory]
    [InlineData(16, 1F, 16)]
    [InlineData(16, 1.25F, 20)]
    [InlineData(18, 1.25F, 23)]
    [InlineData(20, 1.5F, 30)]
    [InlineData(20, 2F, 40)]
    public void AGlyphIsRasterisedForTheScalingItWillBeDrawnAt(int logicalSize, float scale, int expected)
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var glyph = UiIconFactory.Create(UiGlyph.Save, Color.White, logicalSize, scale);

            Assert.Equal(expected, glyph.Width);
            Assert.Equal(expected, glyph.Height);
        });
    }

    [Fact]
    public void APaddingWrittenInLogicalUnitsIsConvertedOnEveryEdge()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var control = new Control();
            var scaled = control.LogicalToDeviceUnits(new Padding(8, 3, 16, 24));

            Assert.Equal(control.LogicalToDeviceUnits(8), scaled.Left);
            Assert.Equal(control.LogicalToDeviceUnits(3), scaled.Top);
            Assert.Equal(control.LogicalToDeviceUnits(16), scaled.Right);
            Assert.Equal(control.LogicalToDeviceUnits(24), scaled.Bottom);

            // A zero edge stays zero rather than picking up a rounding artefact.
            Assert.Equal(Padding.Empty, control.LogicalToDeviceUnits(Padding.Empty));
        });
    }

    [Fact]
    public void APointWrittenInLogicalUnitsIsConvertedOnBothAxes()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var control = new Control();
            var scaled = control.LogicalToDeviceUnits(new Point(39, 5));

            Assert.Equal(control.LogicalToDeviceUnits(39), scaled.X);
            Assert.Equal(control.LogicalToDeviceUnits(5), scaled.Y);
        });
    }

    /// <summary>
    /// A toolbar resamples every image to <see cref="ToolStrip.ImageScalingSize"/>, and the
    /// framework never scales that property for the display. Left at its logical value it undid
    /// the icon factory's work: a glyph rasterised at 25 pixels for 125% was resampled back to 20.
    /// </summary>
    [Fact]
    public void AToolbarShowsItsGlyphsAtTheSizeTheyWereRasterisedFor()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var strip = new ToolStrip
            {
                ImageScalingSize = new Size(20, 20)
            };
            strip.ImageScalingSize = strip.LogicalToDeviceUnits(new Size(20, 20));

            using var glyph = UiIconFactory.Create(
                UiGlyph.Refresh, StorageHubTheme.Text, 20, Scale(strip));

            Assert.Equal(glyph.Width, strip.ImageScalingSize.Width);
            Assert.Equal(glyph.Height, strip.ImageScalingSize.Height);
        });
    }

    /// <summary>
    /// The scale argument used to default to 1, so a caller that omitted it got a glyph drawn for
    /// 96 DPI and stretched. It is derived from the owner now, and this is what says so.
    /// </summary>
    [Fact]
    public void ATrackedIconWithNoScaleGivenTakesItFromTheControlItBelongsTo()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var button = new StorageHubButton();
            var tracked = StorageHubTheme.TrackIcon(button, UiGlyph.Save, 16);

            using var expected = UiIconFactory.Create(
                UiGlyph.Save, StorageHubTheme.Text, 16, Scale(button));

            Assert.Equal(expected.Width, tracked.Width);
            Assert.Equal(expected.Height, tracked.Height);
        });
    }

    /// <summary>
    /// The settings column is the metric the 125%/100% damage was most visible in: a fixed 720
    /// device pixels holding rows whose padding and font both grew with the display.
    /// </summary>
    [Fact]
    public void TheSettingsContentColumnGrowsWithTheDisplay()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var settings = new SettingsForm();
            var property = typeof(SettingsForm).GetProperty(
                "ScaledContentWidth",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(property);

            var width = Assert.IsType<int>(property.GetValue(settings));
            Assert.Equal(settings.LogicalToDeviceUnits(720), width);
        });
    }

    /// <summary>
    /// A row's height comes from its font plus scaled padding, so a field and the button beside it
    /// stay on one baseline however the display is scaled.
    /// </summary>
    [Fact]
    public void AFieldIsAsTallAsItsFontPlusScaledPadding()
    {
        SyncRunReviewControlTests.RunOnSta(() =>
        {
            using var field = new StorageHubTextField();
            var expected = field.Font.Height + (field.LogicalToDeviceUnits(8) * 2);

            Assert.Equal(expected, field.Height);
        });
    }
}
