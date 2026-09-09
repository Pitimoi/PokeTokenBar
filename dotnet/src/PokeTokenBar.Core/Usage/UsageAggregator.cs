namespace PokeTokenBar.Core.Usage;

/// <summary>Rolls deduplicated entries up into per-day, per-model totals.</summary>
public static class UsageAggregator
{
    /// <summary>
    /// Groups <paramref name="entries"/> by local day, newest day first, each with its model
    /// breakdown ordered by total descending.
    /// </summary>
    public static IReadOnlyList<DailyUsage> ByDay(IEnumerable<UsageEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var days = new Dictionary<string, Dictionary<string, ModelAccumulator>>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (!days.TryGetValue(entry.LocalDay, out var models))
            {
                models = new Dictionary<string, ModelAccumulator>(StringComparer.Ordinal);
                days[entry.LocalDay] = models;
            }

            models.TryGetValue(entry.Model, out var accumulated);
            models[entry.Model] = accumulated.Add(entry);
        }

        var result = new List<DailyUsage>(days.Count);
        foreach (var (localDay, models) in days)
        {
            result.Add(Compose(localDay, models));
        }

        result.Sort(static (left, right) => string.CompareOrdinal(right.LocalDay, left.LocalDay));
        return result;
    }

    /// <summary>
    /// Totals for one day. Returns an empty result rather than null when the day has no
    /// usage, so a caller rendering "today" has nothing to special-case.
    /// </summary>
    public static DailyUsage ForDay(IEnumerable<UsageEntry> entries, string localDay)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(localDay);

        var matching = entries.Where(e => string.Equals(e.LocalDay, localDay, StringComparison.Ordinal));
        var days = ByDay(matching);
        return days.Count == 0 ? DailyUsage.Empty(localDay) : days[0];
    }

    private static DailyUsage Compose(string localDay, Dictionary<string, ModelAccumulator> models)
    {
        var breakdown = new List<ModelUsage>(models.Count);
        long input = 0, output = 0, cacheWrite = 0, cacheRead = 0;

        foreach (var (model, totals) in models)
        {
            input += totals.Input;
            output += totals.Output;
            cacheWrite += totals.CacheWrite;
            cacheRead += totals.CacheRead;

            breakdown.Add(new ModelUsage
            {
                Model = model,
                Input = totals.Input,
                Output = totals.Output,
                CacheWrite = totals.CacheWrite,
                CacheRead = totals.CacheRead,
            });
        }

        // Ordinal model name as the tie-break keeps output stable across runs, which matters
        // for both snapshot tests and a status bar that would otherwise reorder on refresh.
        breakdown.Sort(static (left, right) => right.Total != left.Total
            ? right.Total.CompareTo(left.Total)
            : string.CompareOrdinal(left.Model, right.Model));

        return new DailyUsage
        {
            LocalDay = localDay,
            Input = input,
            Output = output,
            CacheWrite = cacheWrite,
            CacheRead = cacheRead,
            Models = breakdown,
        };
    }

    private readonly record struct ModelAccumulator(long Input, long Output, long CacheWrite, long CacheRead)
    {
        public ModelAccumulator Add(UsageEntry entry) => new(
            Input + entry.Input,
            Output + entry.Output,
            CacheWrite + entry.CacheWrite,
            CacheRead + entry.CacheRead);
    }
}
