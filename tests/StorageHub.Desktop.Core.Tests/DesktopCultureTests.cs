using System.Globalization;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

public sealed class DesktopCultureTests
{
    [Theory]
    [InlineData("da-DK", "da-DK")]
    [InlineData("de-DE", "de-DE")]
    [InlineData("en-US", "en-US")]
    public void AShippedLanguageIsUsedAsIs(string uiCulture, string expected) =>
        Assert.Equal(expected, DesktopCulture.Resolve(null, new CultureInfo(uiCulture)));

    /// <summary>
    /// Windows offers plenty of regional variants StorageHub does not ship. A Danish speaker on
    /// da-GL should get Danish rather than English.
    /// </summary>
    [Theory]
    [InlineData("da-GL", "da-DK")]
    [InlineData("de-AT", "de-DE")]
    [InlineData("de-CH", "de-DE")]
    [InlineData("en-GB", "en-US")]
    public void ARegionalVariantFallsBackToTheShippedLanguage(string uiCulture, string expected) =>
        Assert.Equal(expected, DesktopCulture.Resolve(null, new CultureInfo(uiCulture)));

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    [InlineData("")]
    public void AnUnshippedLanguageFallsBackToEnglish(string uiCulture) =>
        Assert.Equal(DesktopCulture.DefaultCulture, DesktopCulture.Resolve(null, new CultureInfo(uiCulture)));

    [Fact]
    public void AConfiguredLanguageOverridesTheOperatingSystem() =>
        Assert.Equal("de-DE", DesktopCulture.Resolve("de-DE", new CultureInfo("da-DK")));

    [Theory]
    [InlineData(DesktopCulture.AutomaticLanguage)]
    [InlineData("AUTO")]
    [InlineData(null)]
    [InlineData("")]
    public void AutomaticMeansFollowTheOperatingSystem(string? configured) =>
        Assert.Equal("da-DK", DesktopCulture.Resolve(configured, new CultureInfo("da-DK")));

    /// <summary>
    /// A language that was configured and then dropped from the product must not leave the shell
    /// without words.
    /// </summary>
    [Fact]
    public void AConfiguredLanguageThatIsNotShippedIsIgnored() =>
        Assert.Equal("da-DK", DesktopCulture.Resolve("fr-FR", new CultureInfo("da-DK")));

    [Fact]
    public void EnglishIsTheDefaultAndIsShipped()
    {
        Assert.Equal("en-US", DesktopCulture.DefaultCulture);
        Assert.Contains(DesktopCulture.DefaultCulture, DesktopCulture.SupportedCultures);
    }
}
