using System.Globalization;
using StorageHub.Contracts.Ipc;

namespace StorageHub.Desktop;

/// <summary>
/// The time zones a schedule can be on, and which one a new schedule starts on.
/// </summary>
/// <remarks>
/// <para>
/// Only schedules have a zone of their own. Every time StorageHub shows (the queue, run history,
/// file lists) follows the operating system, and nothing here changes that.
/// </para>
/// <para>
/// The ids are the operating system's own: Windows names on Windows, IANA names on Linux. The agent
/// runs on the same machine as the desktop, so a schedule is stored the way the agent there reads
/// it. A name from the other system, which an imported settings file can carry, is turned into
/// this one's by <see cref="Normalize"/>.
/// </para>
/// </remarks>
internal static class ScheduleTimeZones
{
    /// <summary>What the setting holds to follow the operating system's zone.</summary>
    internal const string FollowSystem = "system";

    /// <summary>
    /// Every zone the machine knows, by its region name.
    /// </summary>
    /// <remarks>
    /// The machine's own zone is added if the list leaves it out. On Linux it can: a machine on
    /// Etc/UTC is not in GetSystemTimeZones, and a new schedule then started on whichever zone
    /// sorted first, Africa/Abidjan.
    /// </remarks>
    internal static IReadOnlyList<TimeZoneInfo> All { get; } =
        [.. TimeZoneInfo.GetSystemTimeZones()
            .Append(TimeZoneInfo.Local)
            .DistinctBy(static zone => zone.Id, StringComparer.Ordinal)
            .OrderBy(static zone => zone.Id, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>A zone as the pickers name it: its region, and today's standard offset.</summary>
    internal static string Caption(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return $"{zone.Id} · UTC{Offset(zone)}";
    }

    /// <summary>
    /// The operating system's zone as it is now.
    /// </summary>
    /// <remarks>
    /// .NET reads the zone once and keeps it, so a machine moved to another zone while StorageHub
    /// runs would go on handing out the old one. Following the system means asking it again.
    /// </remarks>
    internal static TimeZoneInfo SystemZone()
    {
        TimeZoneInfo.ClearCachedData();
        return TimeZoneInfo.Local;
    }

    /// <summary>
    /// The id as this machine names the zone, or null when it is not a zone this machine knows.
    /// </summary>
    /// <remarks>
    /// A Windows name on Linux, or an IANA name on Windows, is converted rather than refused: a
    /// settings file exported on the other system should land on the same zone, not on none.
    /// </remarks>
    internal static string? Normalize(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > ScheduleManagementIpcLimits.MaximumTimeZoneIdLength)
        {
            return null;
        }

        var trimmed = id.Trim();
        if (Find(trimmed) is { } known) return known.Id;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(trimmed, out var iana) && Find(iana) is { } fromWindows)
        {
            return fromWindows.Id;
        }

        return TimeZoneInfo.TryConvertIanaIdToWindowsId(trimmed, out var windows) && Find(windows) is { } fromIana
            ? fromIana.Id
            : null;
    }

    /// <summary>
    /// The zone a new schedule starts on: the one chosen in Settings, or the system's as it is now.
    /// A chosen zone this machine does not know falls back to the system's.
    /// </summary>
    internal static TimeZoneInfo ForNewSchedule(string? setting) =>
        Normalize(setting) is { } id ? Find(id)! : SystemZone();

    /// <summary>Whether the setting follows the system rather than naming a zone.</summary>
    internal static bool FollowsSystem(string? setting) => Normalize(setting) is null;

    private static TimeZoneInfo? Find(string id) =>
        All.FirstOrDefault(zone => string.Equals(zone.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// A zone's standard offset, as "+01:00".
    /// </summary>
    /// <remarks>
    /// Written out rather than handed to a format string: TimeSpan's custom formats have no
    /// positive/negative sections (that is a numeric-format feature), and asking for one throws a
    /// FormatException from inside a static initializer, where it surfaces as the entire screen
    /// failing to construct rather than as one wrong label.
    /// </remarks>
    private static string Offset(TimeZoneInfo zone)
    {
        var offset = zone.BaseUtcOffset;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(offset < TimeSpan.Zero ? '-' : '+')}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}");
    }
}
