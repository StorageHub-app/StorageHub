using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Every enum value the shell shows has words, in every shipped language.
/// </summary>
/// <remarks>
/// <see cref="UiEnumNames"/> keeps a fallback arm per enum, because these values cross the IPC
/// boundary and a newer agent can send one this build does not know. That fallback is also exactly
/// how an enum member added later would reach the screen as its C# identifier without anything
/// failing — which is what put "Review" in a Danish queue toolbar.
///
/// So the fallback is allowed to exist and is not allowed to be reachable from a declared member:
/// each test below walks <see cref="Enum.GetValues{T}"/> and asserts the description is not the
/// identifier. Adding a member to any of these enums fails here until it has a word.
/// </remarks>
public sealed class UiEnumNameTests
{
    [Theory]
    [MemberData(nameof(ShippedTranslationProvider.Cultures), MemberType = typeof(ShippedTranslationProvider))]
    public void EveryTransferStateHasWords(string culture) =>
        AssertNamed(culture, Enum.GetValues<TransferQueueState>(), UiEnumNames.Name);

    [Theory]
    [MemberData(nameof(ShippedTranslationProvider.Cultures), MemberType = typeof(ShippedTranslationProvider))]
    public void EveryTransferOperationHasWords(string culture) =>
        AssertNamed(culture, Enum.GetValues<TransferQueueOperation>(), UiEnumNames.Name);

    [Theory]
    [MemberData(nameof(ShippedTranslationProvider.Cultures), MemberType = typeof(ShippedTranslationProvider))]
    public void EveryReconciliationActionHasWords(string culture) =>
        AssertNamed(culture, Enum.GetValues<TransferReconciliationAction>(), UiEnumNames.Name);

    [Theory]
    [MemberData(nameof(ShippedTranslationProvider.Cultures), MemberType = typeof(ShippedTranslationProvider))]
    public void EverySyncPhaseHasWords(string culture) =>
        AssertNamed(culture, Enum.GetValues<SyncIpcRunPhase>(), UiEnumNames.Name);

    [Theory]
    [MemberData(nameof(ShippedTranslationProvider.Cultures), MemberType = typeof(ShippedTranslationProvider))]
    public void EveryDispatchStateHasWords(string culture) =>
        AssertNamed(culture, Enum.GetValues<SyncIpcDispatchState>(), UiEnumNames.Name);

    [Theory]
    [MemberData(nameof(ShippedTranslationProvider.Cultures), MemberType = typeof(ShippedTranslationProvider))]
    public void EveryPlanOperationKindHasWords(string culture) =>
        AssertNamed(culture, Enum.GetValues<SyncIpcPlanOperationKind>(), UiEnumNames.Name);

    [Theory]
    [MemberData(nameof(ShippedTranslationProvider.Cultures), MemberType = typeof(ShippedTranslationProvider))]
    public void EveryConflictStateHasWords(string culture) =>
        AssertNamed(culture, Enum.GetValues<SyncIpcConflictState>(), UiEnumNames.Name);

    /// <summary>
    /// Weekdays come from .NET rather than the shipped files, so this checks the wiring instead of
    /// the words: that the day follows the shell's language, not the machine's regional settings.
    /// </summary>
    [Theory]
    [InlineData("da-DK", DayOfWeek.Monday, "mandag")]
    [InlineData("de-DE", DayOfWeek.Monday, "Montag")]
    [InlineData("en-US", DayOfWeek.Monday, "Monday")]
    public void WeekdaysComeFromTheShellsLanguage(string culture, DayOfWeek day, string expected)
    {
        ShippedTranslationProvider.InCulture(culture, () =>
            Assert.Equal(expected, UiEnumNames.Describe(day)));
    }

    /// <summary>
    /// The unknown value a future agent could send still renders, rather than throwing inside a
    /// grid's paint. This is what the fallback arms are for.
    /// </summary>
    [Fact]
    public void AnUnknownValueStillRenders()
    {
        var unknown = (TransferQueueState)9999;
        Assert.False(string.IsNullOrWhiteSpace(UiEnumNames.Describe(unknown)));
    }

    private static void AssertNamed<T>(string culture, T[] values, Func<T, string?> name)
        where T : struct, Enum
    {
        ShippedTranslationProvider.InCulture(culture, () =>
        {
            Assert.NotEmpty(values);
            foreach (var value in values)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(name(value)),
                    $"{culture}: {typeof(T).Name}.{value} has no words, so it would reach the screen " +
                    "as its identifier.");
            }
        });
    }
}
