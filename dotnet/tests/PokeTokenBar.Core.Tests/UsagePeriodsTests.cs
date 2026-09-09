using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Core.Tests;

public sealed class UsagePeriodsTests
{
    [Theory]
    [InlineData("2026-09-09", "2026-09-07")] // Wednesday -> Monday
    [InlineData("2026-09-07", "2026-09-07")] // Monday is its own start
    [InlineData("2026-09-13", "2026-09-07")] // Sunday belongs to the week that began Monday
    public void WeekStartsOnMonday(string today, string expectedStart)
    {
        var now = Local(today, 15);

        Assert.Equal(expectedStart, LocalDay.For(UsagePeriods.StartOfWeek(now)));
    }

    [Fact]
    public void MonthStartsOnTheFirst()
    {
        Assert.Equal("2026-09-01", LocalDay.For(UsagePeriods.StartOfMonth(Local("2026-09-09", 15))));
        Assert.Equal("2026-09-01", LocalDay.For(UsagePeriods.StartOfMonth(Local("2026-09-01", 0))));
    }

    [Fact]
    public void ScanStartReachesBackIntoThePreviousMonthWhenTheWeekStraddlesIt()
    {
        // The trap this exists for: on 2026-09-02 (a Wednesday) the current week began Monday
        // 2026-08-31. Bounding the scan by the month alone would skip sessions last written in
        // August, and the weekly total would under-report for the first days of the month.
        var now = Local("2026-09-02", 10);

        var scanStart = UsagePeriods.ScanStart(now);

        Assert.Equal("2026-08-31", LocalDay.For(scanStart));
        Assert.True(scanStart < UsagePeriods.StartOfMonth(now));
    }

    [Fact]
    public void ScanStartUsesTheMonthWhenItIsEarlier()
    {
        // Late in a month the month start is the earlier bound.
        var now = Local("2026-09-25", 10);

        Assert.Equal("2026-09-01", LocalDay.For(UsagePeriods.ScanStart(now)));
    }

    [Fact]
    public void ScanStartIsNeverAfterAnyWindowItFeeds()
    {
        // The append-only assumption only holds if the bound precedes every displayed window.
        for (var day = 1; day <= 30; day++)
        {
            var now = Local($"2026-09-{day:00}", 12);
            var scanStart = UsagePeriods.ScanStart(now);

            Assert.True(scanStart <= UsagePeriods.StartOfWeek(now), $"week bound violated on day {day}");
            Assert.True(scanStart <= UsagePeriods.StartOfMonth(now), $"month bound violated on day {day}");
        }
    }

    private static DateTimeOffset Local(string day, int hour)
    {
        var date = DateTime.Parse(day, null);
        return new DateTimeOffset(
            date.Year,
            date.Month,
            date.Day,
            hour,
            0,
            0,
            TimeZoneInfo.Local.GetUtcOffset(date));
    }
}
