using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Core.Tests;

public sealed class UsageAggregatorTests
{
    [Fact]
    public void SumsPerModelWithinADay()
    {
        var entries = new[]
        {
            Entry("a", "2026-09-09", "opus", input: 10, output: 1),
            Entry("b", "2026-09-09", "opus", input: 20, output: 2),
            Entry("c", "2026-09-09", "haiku", input: 5, output: 5),
        };

        var day = UsageAggregator.ForDay(entries, "2026-09-09");

        Assert.Equal(43, day.Total);
        Assert.Equal(35, day.Input);
        Assert.Equal(2, day.Models.Count);
        Assert.Equal("opus", day.Models[0].Model);
        Assert.Equal(33, day.Models[0].Total);
        Assert.Equal(10, day.Models[1].Total);
    }

    [Fact]
    public void OrdersModelsByTotalDescending()
    {
        var entries = new[]
        {
            Entry("a", "2026-09-09", "small", input: 1),
            Entry("b", "2026-09-09", "big", input: 100),
            Entry("c", "2026-09-09", "medium", input: 50),
        };

        var day = UsageAggregator.ForDay(entries, "2026-09-09");

        Assert.Equal(["big", "medium", "small"], day.Models.Select(m => m.Model));
    }

    [Fact]
    public void BreaksTiesByModelNameSoOutputIsStable()
    {
        // A status bar that reorders equal-valued rows on every refresh looks broken.
        var entries = new[]
        {
            Entry("a", "2026-09-09", "zeta", input: 10),
            Entry("b", "2026-09-09", "alpha", input: 10),
        };

        var day = UsageAggregator.ForDay(entries, "2026-09-09");

        Assert.Equal(["alpha", "zeta"], day.Models.Select(m => m.Model));
    }

    [Fact]
    public void IgnoresEntriesFromOtherDays()
    {
        var entries = new[]
        {
            Entry("a", "2026-09-08", "opus", input: 999),
            Entry("b", "2026-09-09", "opus", input: 7),
            Entry("c", "2026-09-10", "opus", input: 999),
        };

        var day = UsageAggregator.ForDay(entries, "2026-09-09");

        Assert.Equal(7, day.Total);
    }

    [Fact]
    public void ReturnsEmptyRatherThanNullForADayWithNoUsage()
    {
        var day = UsageAggregator.ForDay([], "2026-09-09");

        Assert.Equal("2026-09-09", day.FromDay);
        Assert.Equal(0, day.Total);
        Assert.Empty(day.Models);
    }

    [Fact]
    public void GroupsDaysNewestFirst()
    {
        var entries = new[]
        {
            Entry("a", "2026-09-08", "opus", input: 1),
            Entry("b", "2026-09-10", "opus", input: 2),
            Entry("c", "2026-09-09", "opus", input: 3),
        };

        var days = UsageAggregator.ByDay(entries);

        Assert.Equal(["2026-09-10", "2026-09-09", "2026-09-08"], days.Select(d => d.FromDay));
    }

    [Fact]
    public void TotalsExceedingInt32SurviveAggregation()
    {
        var entries = Enumerable.Range(0, 8)
            .Select(i => Entry($"e{i}", "2026-09-09", "opus", cacheRead: TokenCount.Implausible))
            .ToArray();

        var day = UsageAggregator.ForDay(entries, "2026-09-09");

        Assert.Equal(8L * TokenCount.Implausible, day.Total);
        Assert.True(day.Total > int.MaxValue);
    }

    [Fact]
    public async Task AgreesWithTheReaderOnTheDayKey()
    {
        // Guards drift between the two: if the reader and LocalDay ever formatted
        // differently, aggregation would silently return an empty day.
        var instant = DateTimeOffset.Parse("2026-09-09T14:23:51.933Z", null);
        using var transcript = new TempTranscript(
            TempTranscript.UsageLine(input: 42, timestamp: "2026-09-09T14:23:51.933Z"));

        var scan = await ClaudeTranscriptReader.ReadFileAsync(transcript.Path);
        var day = UsageAggregator.ForDay(scan.Entries, LocalDay.For(instant));

        Assert.Equal(42, day.Total);
    }

    private static UsageEntry Entry(
        string id,
        string localDay,
        string model,
        long input = 0,
        long output = 0,
        long cacheWrite = 0,
        long cacheRead = 0) => new()
        {
            Id = id,
            Timestamp = DateTimeOffset.Parse(localDay + "T12:00:00Z", null),
            LocalDay = localDay,
            Model = model,
            Input = input,
            Output = output,
            CacheWrite = cacheWrite,
            CacheRead = cacheRead,
        };

    [Fact]
    public void SumsAcrossAnInclusiveDayRange()
    {
        var entries = new[]
        {
            Entry("a", "2026-09-06", "opus", input: 1),
            Entry("b", "2026-09-07", "opus", input: 10),
            Entry("c", "2026-09-09", "opus", input: 100),
            Entry("d", "2026-09-10", "opus", input: 1000),
        };

        var week = UsageAggregator.ForRange(entries, "2026-09-07", "2026-09-09");

        Assert.Equal(110, week.Total);
        Assert.Equal("2026-09-07", week.FromDay);
        Assert.Equal("2026-09-09", week.ToDay);
    }

    [Fact]
    public void RangeBoundsAreInclusiveAtBothEnds()
    {
        var entries = new[]
        {
            Entry("a", "2026-09-07", "opus", input: 1),
            Entry("b", "2026-09-09", "opus", input: 2),
        };

        Assert.Equal(3, UsageAggregator.ForRange(entries, "2026-09-07", "2026-09-09").Total);
    }

    [Fact]
    public void RangeSpansAMonthBoundary()
    {
        // yyyy-MM-dd sorts chronologically, which is what makes string comparison safe here.
        var entries = new[]
        {
            Entry("a", "2026-08-31", "opus", input: 5),
            Entry("b", "2026-09-01", "opus", input: 7),
            Entry("c", "2026-08-30", "opus", input: 999),
        };

        Assert.Equal(12, UsageAggregator.ForRange(entries, "2026-08-31", "2026-09-01").Total);
    }

    [Fact]
    public void AttributesCostPerModel()
    {
        var entries = new[]
        {
            Entry("a", "2026-09-09", "claude-opus-4-8", input: 1_000_000),
            Entry("b", "2026-09-09", "claude-haiku-4-5-20251001", input: 1_000_000),
        };

        var day = UsageAggregator.ForDay(entries, "2026-09-09");

        Assert.Equal(6d, day.Cost, precision: 6);
        Assert.Equal(5d, day.Models.Single(m => m.Model == "claude-opus-4-8").Cost, precision: 6);
        Assert.Equal(1d, day.Models.Single(m => m.Model.Contains("haiku")).Cost, precision: 6);
    }

    [Fact]
    public void CostsUnpricedModelsAtZeroWithoutAffectingOthers()
    {
        var entries = new[]
        {
            Entry("a", "2026-09-09", "claude-opus-4-8", input: 1_000_000),
            Entry("b", "2026-09-09", "grok-codex-fast", input: 1_000_000),
        };

        var day = UsageAggregator.ForDay(entries, "2026-09-09");

        Assert.Equal(5d, day.Cost, precision: 6);
        Assert.Equal(0d, day.Models.Single(m => m.Model.StartsWith("grok")).Cost);
    }
}
