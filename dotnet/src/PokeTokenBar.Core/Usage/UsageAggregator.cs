namespace PokeTokenBar.Core.Usage;

/// <summary>Rolls deduplicated entries up into per-window, per-model totals with cost.</summary>
public static class UsageAggregator
{
    /// <summary>Totals for one local day.</summary>
    public static UsageTotals ForDay(IEnumerable<UsageEntry> entries, string day)
    {
        ArgumentNullException.ThrowIfNull(day);
        return ForRange(entries, day, day);
    }

    /// <summary>
    /// Totals over an inclusive range of local days. Bounds are compared as <c>yyyy-MM-dd</c>
    /// strings, which sort chronologically, so no date parsing is needed on the hot path.
    /// </summary>
    public static UsageTotals ForRange(IEnumerable<UsageEntry> entries, string fromDay, string toDay)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(fromDay);
        ArgumentNullException.ThrowIfNull(toDay);

        var models = new Dictionary<string, ModelAccumulator>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (string.CompareOrdinal(entry.LocalDay, fromDay) < 0
                || string.CompareOrdinal(entry.LocalDay, toDay) > 0)
            {
                continue;
            }

            models.TryGetValue(entry.Model, out var accumulated);
            models[entry.Model] = accumulated.Add(entry);
        }

        return models.Count == 0 ? UsageTotals.Empty(fromDay, toDay) : Compose(fromDay, toDay, models);
    }

    /// <summary>Per-day totals, newest day first.</summary>
    public static IReadOnlyList<UsageTotals> ByDay(IEnumerable<UsageEntry> entries)
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

        var result = new List<UsageTotals>(days.Count);
        foreach (var (day, models) in days)
        {
            result.Add(Compose(day, day, models));
        }

        result.Sort(static (left, right) => string.CompareOrdinal(right.FromDay, left.FromDay));
        return result;
    }

    private static UsageTotals Compose(
        string fromDay,
        string toDay,
        Dictionary<string, ModelAccumulator> models)
    {
        var breakdown = new List<ModelUsage>(models.Count);
        long input = 0, output = 0, cacheWrite = 0, cacheRead = 0;
        var cost = 0d;

        foreach (var (model, totals) in models)
        {
            input += totals.Input;
            output += totals.Output;
            cacheWrite += totals.CacheWrite;
            cacheRead += totals.CacheRead;

            // Costing aggregated tokens rather than each entry is equivalent, since rates are
            // linear per token, and avoids a pricing lookup per entry.
            var modelCost = ModelPricing.Cost(
                model,
                totals.Input,
                totals.Output,
                totals.CacheWrite,
                totals.CacheRead);
            cost += modelCost;

            breakdown.Add(new ModelUsage
            {
                Model = model,
                Input = totals.Input,
                Output = totals.Output,
                CacheWrite = totals.CacheWrite,
                CacheRead = totals.CacheRead,
                Cost = modelCost,
            });
        }

        // Model name as the tie-break keeps output stable across runs, which matters for a
        // status bar that would otherwise reorder equal rows on every refresh.
        breakdown.Sort(static (left, right) => right.Total != left.Total
            ? right.Total.CompareTo(left.Total)
            : string.CompareOrdinal(left.Model, right.Model));

        return new UsageTotals
        {
            FromDay = fromDay,
            ToDay = toDay,
            Input = input,
            Output = output,
            CacheWrite = cacheWrite,
            CacheRead = cacheRead,
            Cost = cost,
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
