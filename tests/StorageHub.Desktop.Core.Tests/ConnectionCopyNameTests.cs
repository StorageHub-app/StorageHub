using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

public sealed class ConnectionCopyNameTests
{
    [Fact]
    public void ACopyIsNamedForTheOriginal() =>
        Assert.Equal("Backups (copy)", ConnectionCopyNames.Next("Backups", ["Backups"]));

    [Fact]
    public void ATakenNameMovesOnToTheNextNumber() =>
        Assert.Equal(
            "Backups (copy 3)",
            ConnectionCopyNames.Next("Backups", ["Backups", "Backups (copy)", "BACKUPS (COPY 2)"]));

    [Fact]
    public void ACopyOfACopyIsNamedForTheCopy() =>
        Assert.Equal("Backups (copy) (copy)", ConnectionCopyNames.Next("Backups (copy)", ["Backups (copy)"]));

    [Fact]
    public void ALongNameLosesItsOwnEndNotTheSuffix()
    {
        var name = new string('n', ConnectionProfileIpcLimits.MaximumDisplayNameLength);

        var copy = ConnectionCopyNames.Next(name, [name]);

        Assert.True(copy.Length <= ConnectionProfileIpcLimits.MaximumDisplayNameLength);
        Assert.EndsWith(" (copy)", copy, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ShippedTranslationProvider.Cultures), MemberType = typeof(ShippedTranslationProvider))]
    public void EveryLanguageNamesACopyAndANumberedCopyDifferently(string culture) =>
        ShippedTranslationProvider.InCulture(culture, () =>
        {
            var first = ConnectionCopyNames.Next("Backups", ["Backups"]);
            var second = ConnectionCopyNames.Next("Backups", ["Backups", first]);
            Assert.Contains("Backups", first, StringComparison.Ordinal);
            Assert.NotEqual(first, second);
            Assert.Contains("2", second, StringComparison.Ordinal);
        });
}
