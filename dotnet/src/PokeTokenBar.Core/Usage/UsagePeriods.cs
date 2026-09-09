namespace PokeTokenBar.Core.Usage;

/// <summary>The reporting windows: this day, this week, this month.</summary>
public static class UsagePeriods
{
    /// <summary>
    /// First day of the current week. Monday per ISO-8601, chosen because it is deterministic:
    /// the Swift original takes the first day from the system calendar, which cannot be
    /// reproduced here since the sidecar runs with invariant globalisation and no locale.
    /// </summary>
    public static DateTimeOffset StartOfWeek(DateTimeOffset now)
    {
        var daysSinceMonday = ((int)now.DayOfWeek + 6) % 7;
        return StartOfDay(now).AddDays(-daysSinceMonday);
    }

    public static DateTimeOffset StartOfMonth(DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        return new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, local.Offset);
    }

    /// <summary>
    /// Earliest file modification time a scan must consider to populate every window.
    /// </summary>
    /// <remarks>
    /// This has to be the minimum of all displayed windows, not just the month. Transcripts are
    /// append-only, so "a file modified before the window started holds nothing inside it" only
    /// holds when the scan bound is at or before every window's start. In eleven months of a
    /// typical year the current week begins in the previous month, so bounding by the month
    /// alone drops sessions last touched then, and the weekly total silently under-reports for
    /// the first days of a month.
    /// </remarks>
    public static DateTimeOffset ScanStart(DateTimeOffset now)
    {
        var week = StartOfWeek(now);
        var month = StartOfMonth(now);
        return week < month ? week : month;
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset);
    }
}
