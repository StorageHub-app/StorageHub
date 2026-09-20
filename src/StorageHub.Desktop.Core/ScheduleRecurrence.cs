using System.Globalization;
using StorageHub.Contracts.Ipc;
using StorageHub.Desktop.Localization;

namespace StorageHub.Desktop;

/// <summary>How often a schedule runs, in the terms somebody would choose it in.</summary>
internal enum ScheduleFrequency
{
    Daily,
    Weekdays,
    Weekly,
    Monthly,

    /// <summary>A cron expression that none of the above describes, kept as typed.</summary>
    Custom
}

/// <summary>
/// A recurrence, and the cron expression it means.
/// </summary>
/// <remarks>
/// <para>
/// The agent stores five-field cron and nothing else; nobody should have to write one to say
/// "every weekday at seven". This is both halves of that translation, and it is the piece of the
/// schedule manager most able to be quietly wrong -- an off-by-one in the day-of-week field is a
/// schedule that runs on the wrong day and gives no sign of it.
/// </para>
/// <para>
/// Anything the builder cannot express stays <see cref="ScheduleFrequency.Custom"/> and keeps the
/// expression exactly as written. A round trip through here never rewrites somebody's cron: an
/// expression that means what no preset means is left alone rather than approximated to the
/// nearest one.
/// </para>
/// </remarks>
internal sealed record ScheduleRecurrence(
    ScheduleFrequency Frequency,
    int Hour = 2,
    int Minute = 0,
    DayOfWeek DayOfWeek = DayOfWeek.Monday,
    int DayOfMonth = 1,
    string CustomExpression = "")
{
    /// <summary>What a new schedule starts on: two in the morning, every day.</summary>
    internal static ScheduleRecurrence Default { get; } = new(ScheduleFrequency.Daily, 2, 0);

    /// <summary>The five-field cron expression this recurrence means.</summary>
    internal string ToCron() => Frequency switch
    {
        ScheduleFrequency.Daily => $"{Field(Minute)} {Field(Hour)} * * *",
        ScheduleFrequency.Weekdays => $"{Field(Minute)} {Field(Hour)} * * 1-5",
        ScheduleFrequency.Weekly => $"{Field(Minute)} {Field(Hour)} * * {Field((int)DayOfWeek)}",
        ScheduleFrequency.Monthly => $"{Field(Minute)} {Field(Hour)} {Field(DayOfMonth)} * *",
        _ => CustomExpression.Trim()
    };

    /// <summary>
    /// Reads a cron expression back into a recurrence.
    /// </summary>
    /// <remarks>
    /// Whatever comes in is kept as <see cref="CustomExpression"/> as well, so an expression this
    /// cannot classify survives a trip through the editor unchanged.
    /// </remarks>
    internal static ScheduleRecurrence Parse(string? expression)
    {
        var text = (expression ?? string.Empty).Trim();
        var custom = new ScheduleRecurrence(ScheduleFrequency.Custom, CustomExpression: text);

        var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length != 5 ||
            !TryField(fields[0], 0, 59, out var minute) ||
            !TryField(fields[1], 0, 23, out var hour))
        {
            return custom;
        }

        var everyMonth = fields[3] == "*";
        if (!everyMonth) return custom;

        if (fields[2] == "*" && fields[4] == "*")
        {
            return custom with { Frequency = ScheduleFrequency.Daily, Hour = hour, Minute = minute };
        }

        if (fields[2] == "*" && fields[4] == "1-5")
        {
            return custom with { Frequency = ScheduleFrequency.Weekdays, Hour = hour, Minute = minute };
        }

        if (fields[2] == "*" && TryField(fields[4], 0, 6, out var weekDay))
        {
            return custom with
            {
                Frequency = ScheduleFrequency.Weekly,
                Hour = hour,
                Minute = minute,
                DayOfWeek = (DayOfWeek)weekDay
            };
        }

        if (fields[4] == "*" && TryField(fields[2], 1, 31, out var monthDay))
        {
            return custom with
            {
                Frequency = ScheduleFrequency.Monthly,
                Hour = hour,
                Minute = minute,
                DayOfMonth = monthDay
            };
        }

        return custom;
    }

    /// <summary>
    /// What this recurrence does, in a sentence.
    /// </summary>
    /// <remarks>
    /// The time is formatted in the reader's culture and the weekday comes from the shell's
    /// language rather than the machine's regional settings, which is the same rule the rest of
    /// the shell's enum wording follows.
    /// </remarks>
    internal string Describe() => Frequency switch
    {
        ScheduleFrequency.Daily => Ui.Format(Ui.Schedules.RunsDailyFormat, Time()),
        ScheduleFrequency.Weekdays => Ui.Format(Ui.Schedules.RunsWeekdaysFormat, Time()),
        ScheduleFrequency.Weekly =>
            Ui.Format(Ui.Schedules.RunsWeeklyFormat, UiEnumNames.Describe(DayOfWeek), Time()),
        ScheduleFrequency.Monthly =>
            Ui.Format(Ui.Schedules.RunsMonthlyFormat, DayOfMonth, Time()),
        _ => Ui.Schedules.AdvancedRecurrence
    };

    /// <summary>The name of this frequency, for the picker.</summary>
    internal static string Name(ScheduleFrequency frequency) => frequency switch
    {
        ScheduleFrequency.Daily => Ui.Schedules.EveryDay,
        ScheduleFrequency.Weekdays => Ui.Schedules.EveryWeekday,
        ScheduleFrequency.Weekly => Ui.Schedules.EveryWeek,
        ScheduleFrequency.Monthly => Ui.Schedules.EveryMonth,
        _ => Ui.Schedules.CustomAdvanced
    };

    private string Time() =>
        DateTime.Today.AddHours(Hour).AddMinutes(Minute).ToString("t", CultureInfo.CurrentCulture);

    /// <summary>
    /// A cron field, always in Western digits.
    /// </summary>
    /// <remarks>
    /// The expression is a protocol value the agent parses, not something to be read, so it must
    /// not follow the reader's culture the way the description above does.
    /// </remarks>
    private static string Field(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool TryField(string text, int minimum, int maximum, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
        value >= minimum && value <= maximum;
}
