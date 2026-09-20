using StorageHub.Contracts.Ipc;
using StorageHub.Desktop;
using StorageHub.Desktop.Localization;
using Xunit;

namespace StorageHub.Desktop.Tests;

/// <summary>
/// Turning a recurrence into cron and back.
/// </summary>
/// <remarks>
/// The agent stores five-field cron and nothing else, and nobody should have to write one to say
/// "every weekday at seven". This is the piece of the schedule manager most able to be quietly
/// wrong: an off-by-one in the day-of-week field is a backup that runs on Sunday instead of
/// Saturday, week after week, with nothing on any screen to say so.
/// </remarks>
public class ScheduleRecurrenceTests
{
    // The frequency travels as its underlying number: it is an internal type, and a public test
    // method cannot take one as a parameter.
    [Theory]
    [InlineData((int)ScheduleFrequency.Daily, "0 2 * * *")]
    [InlineData((int)ScheduleFrequency.Weekdays, "0 2 * * 1-5")]
    public void APresetBecomesTheCronItMeans(int frequencyValue, string expected)
    {
        var recurrence = new ScheduleRecurrence((ScheduleFrequency)frequencyValue, Hour: 2, Minute: 0);

        Assert.Equal(expected, recurrence.ToCron());
    }

    /// <summary>
    /// Sunday is 0 and Saturday is 6, as cron and .NET both have it.
    /// </summary>
    /// <remarks>
    /// The two agreeing is luck rather than design, and it is exactly the sort of thing that gets
    /// "fixed" by somebody who knows that ISO weeks start on Monday. Each day is pinned.
    /// </remarks>
    [Theory]
    [InlineData(DayOfWeek.Sunday, "30 7 * * 0")]
    [InlineData(DayOfWeek.Monday, "30 7 * * 1")]
    [InlineData(DayOfWeek.Tuesday, "30 7 * * 2")]
    [InlineData(DayOfWeek.Wednesday, "30 7 * * 3")]
    [InlineData(DayOfWeek.Thursday, "30 7 * * 4")]
    [InlineData(DayOfWeek.Friday, "30 7 * * 5")]
    [InlineData(DayOfWeek.Saturday, "30 7 * * 6")]
    public void AWeeklyScheduleNamesTheRightDay(DayOfWeek day, string expected)
    {
        var recurrence = new ScheduleRecurrence(
            ScheduleFrequency.Weekly, Hour: 7, Minute: 30, DayOfWeek: day);

        Assert.Equal(expected, recurrence.ToCron());
        Assert.Equal(day, ScheduleRecurrence.Parse(expected).DayOfWeek);
    }

    [Fact]
    public void AMonthlyScheduleNamesTheDayOfTheMonth()
    {
        var recurrence = new ScheduleRecurrence(
            ScheduleFrequency.Monthly, Hour: 23, Minute: 5, DayOfMonth: 28);

        Assert.Equal("5 23 28 * *", recurrence.ToCron());
    }

    /// <summary>Every preset survives a trip to cron and back unchanged.</summary>
    [Theory]
    [InlineData((int)ScheduleFrequency.Daily)]
    [InlineData((int)ScheduleFrequency.Weekdays)]
    [InlineData((int)ScheduleFrequency.Weekly)]
    [InlineData((int)ScheduleFrequency.Monthly)]
    public void APresetRoundTrips(int frequencyValue)
    {
        var frequency = (ScheduleFrequency)frequencyValue;
        var recurrence = new ScheduleRecurrence(
            frequency, Hour: 13, Minute: 45, DayOfWeek: DayOfWeek.Thursday, DayOfMonth: 17);

        var parsed = ScheduleRecurrence.Parse(recurrence.ToCron());

        Assert.Equal(frequency, parsed.Frequency);
        Assert.Equal(13, parsed.Hour);
        Assert.Equal(45, parsed.Minute);
        Assert.Equal(recurrence.ToCron(), parsed.ToCron());
    }

    /// <summary>
    /// Anything no preset describes stays exactly as it was written.
    /// </summary>
    /// <remarks>
    /// This is the promise that makes the builder safe to put in front of an existing schedule:
    /// opening one written by hand and saving it without touching the recurrence must not rewrite
    /// it into the nearest preset.
    /// </remarks>
    [Theory]
    [InlineData("*/15 * * * *")]
    [InlineData("0 3 1,15 * *")]
    [InlineData("0 0 * 1 *")]
    [InlineData("0 9-17 * * 1-5")]
    [InlineData("@daily")]
    [InlineData("not a cron expression at all")]
    public void AnExpressionNoPresetDescribesIsLeftAlone(string expression)
    {
        var parsed = ScheduleRecurrence.Parse(expression);

        Assert.Equal(ScheduleFrequency.Custom, parsed.Frequency);
        Assert.Equal(expression, parsed.ToCron());
    }

    /// <summary>
    /// A month field that is not "every month" is custom, however ordinary the rest looks.
    /// </summary>
    /// <remarks>
    /// "0 2 * 1 *" is every day in January. Reading it as Daily and then saving would turn a yearly
    /// schedule into one that runs every day of the year.
    /// </remarks>
    [Fact]
    public void ARestrictedMonthIsNotDaily()
    {
        Assert.Equal(ScheduleFrequency.Custom, ScheduleRecurrence.Parse("0 2 * 1 *").Frequency);
    }

    /// <summary>Out-of-range fields are not silently clamped into a preset.</summary>
    [Theory]
    [InlineData("60 2 * * *")]
    [InlineData("0 24 * * *")]
    [InlineData("0 2 * * 7")]
    [InlineData("0 2 0 * *")]
    [InlineData("0 2 32 * *")]
    [InlineData("-1 2 * * *")]
    public void AnOutOfRangeFieldIsCustom(string expression)
    {
        Assert.Equal(ScheduleFrequency.Custom, ScheduleRecurrence.Parse(expression).Frequency);
    }

    /// <summary>Nothing at all is custom and empty rather than a crash.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingIsCustom(string? expression)
    {
        var parsed = ScheduleRecurrence.Parse(expression);

        Assert.Equal(ScheduleFrequency.Custom, parsed.Frequency);
        Assert.Equal(string.Empty, parsed.ToCron());
    }

    /// <summary>Extra spacing is not a different schedule.</summary>
    [Fact]
    public void SpacingDoesNotChangeTheMeaning()
    {
        Assert.Equal(ScheduleFrequency.Daily, ScheduleRecurrence.Parse("  0   2  *  *  * ").Frequency);
    }

    /// <summary>
    /// The description says what it does, and the weekday follows the shell's language.
    /// </summary>
    /// <remarks>
    /// The whole point of the builder is that somebody can read the schedule back. A cron
    /// expression is not that, and a Danish shell showing "Monday" is only half of it.
    /// </remarks>
    [Fact]
    public void TheDescriptionIsInTheShellsLanguage()
    {
        ShippedTranslationProvider.InCulture("da-DK", () =>
        {
            var weekly = new ScheduleRecurrence(
                ScheduleFrequency.Weekly, Hour: 7, Minute: 0, DayOfWeek: DayOfWeek.Monday);

            Assert.Contains("mandag", weekly.Describe(), StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>A custom expression describes itself as advanced rather than pretending.</summary>
    [Fact]
    public void ACustomExpressionSaysItIsAdvanced() =>
        Assert.Equal(
            Ui.Schedules.AdvancedRecurrence, ScheduleRecurrence.Parse("*/5 * * * *").Describe());

    /// <summary>A new schedule starts at two in the morning, every day.</summary>
    [Fact]
    public void TheDefaultIsNightly()
    {
        Assert.Equal("0 2 * * *", ScheduleRecurrence.Default.ToCron());
        Assert.Equal(ScheduleFrequency.Daily, ScheduleRecurrence.Default.Frequency);
    }

    /// <summary>Every frequency has a name in every shipped language.</summary>
    [Theory]
    [MemberData(nameof(ShippedTranslationProvider.Cultures), MemberType = typeof(ShippedTranslationProvider))]
    public void EveryFrequencyHasWords(string culture)
    {
        ShippedTranslationProvider.InCulture(culture, () =>
        {
            foreach (var frequency in Enum.GetValues<ScheduleFrequency>())
            {
                var name = ScheduleRecurrence.Name(frequency);
                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.NotEqual(frequency.ToString(), name);
            }
        });
    }

    /// <summary>
    /// The cron the agent receives is in Western digits whatever the reader's culture.
    /// </summary>
    /// <remarks>
    /// The description is for a person and follows their culture; the expression is a protocol
    /// value the agent parses and must not.
    /// </remarks>
    [Fact]
    public void TheExpressionDoesNotFollowTheReadersCulture()
    {
        ShippedTranslationProvider.InCulture("da-DK", () =>
            Assert.Equal(
                "30 7 * * 3",
                new ScheduleRecurrence(
                    ScheduleFrequency.Weekly, 7, 30, DayOfWeek.Wednesday).ToCron()));
    }
}
