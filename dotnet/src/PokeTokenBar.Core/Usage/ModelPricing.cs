namespace PokeTokenBar.Core.Usage;

/// <summary>Per-token USD rates for one model.</summary>
public readonly record struct ModelRate(double Input, double Output, double CacheWrite, double CacheRead)
{
    public static ModelRate Zero => default;

    /// <summary>Rates are published per million tokens; declaring them that way keeps the table readable.</summary>
    public static ModelRate PerMillion(double input, double output, double cacheWrite, double cacheRead) =>
        new(input / 1_000_000d, output / 1_000_000d, cacheWrite / 1_000_000d, cacheRead / 1_000_000d);

    public double Cost(long input, long output, long cacheWrite, long cacheRead) =>
        (input * Input) + (output * Output) + (cacheWrite * CacheWrite) + (cacheRead * CacheRead);
}

/// <summary>
/// Token pricing, matching the rates <c>ccusage</c> uses so local reports agree with it.
/// </summary>
public static class ModelPricing
{
    private static readonly Dictionary<string, ModelRate> Table = new(StringComparer.Ordinal)
    {
        ["claude-opus-4-8"] = ModelRate.PerMillion(5, 25, 6.25, 0.5),
        ["claude-opus-4-7"] = ModelRate.PerMillion(5, 25, 6.25, 0.5),
        ["claude-sonnet-4-6"] = ModelRate.PerMillion(3, 15, 3.75, 0.3),
        ["claude-haiku-4-5-20251001"] = ModelRate.PerMillion(1, 5, 1.25, 0.1),
        ["claude-fable-5"] = ModelRate.PerMillion(10, 50, 12.5, 1.0),
        ["gpt-5.5"] = ModelRate.PerMillion(5, 30, 0, 0.5),
        // Gemini: official API rates, base tier, prompts under 200K. Cache read only —
        // storage-time charges are not derivable from a token count.
        ["gemini-2.5-pro"] = ModelRate.PerMillion(1.25, 10, 0, 0.3125),
        ["gemini-2.5-flash"] = ModelRate.PerMillion(0.30, 2.5, 0, 0.075),
        ["gemini-2.0-flash"] = ModelRate.PerMillion(0.10, 0.4, 0, 0.025),
    };

    /// <summary>
    /// Rate for a model: exact match first, then a family fallback so a version bump does not
    /// silently price at zero. An unrecognised model costs nothing rather than guessing.
    /// </summary>
    public static ModelRate RateFor(string model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (Table.TryGetValue(model, out var exact))
        {
            return exact;
        }

        var name = model.ToLowerInvariant();

        // Both of these must precede the family fallbacks. Grok reports its own charge and has
        // no rate table, and names like `grok-codex-*` would otherwise match the GPT family and
        // display an invented amount. Antigravity is subscription-billed with no per-token
        // charge, and its `antigravity/` prefix is exactly what makes such a name miss the exact
        // table and fall through — this CLI also calls models named `claude-sonnet-4-6`.
        if (name.StartsWith("grok", StringComparison.Ordinal)
            || name.StartsWith("antigravity/", StringComparison.Ordinal))
        {
            return ModelRate.Zero;
        }

        if (name.Contains("fable", StringComparison.Ordinal))
        {
            return ModelRate.PerMillion(10, 50, 12.5, 1.0);
        }

        if (name.Contains("opus", StringComparison.Ordinal))
        {
            return ModelRate.PerMillion(5, 25, 6.25, 0.5);
        }

        if (name.Contains("sonnet", StringComparison.Ordinal))
        {
            return ModelRate.PerMillion(3, 15, 3.75, 0.3);
        }

        if (name.Contains("haiku", StringComparison.Ordinal))
        {
            return ModelRate.PerMillion(1, 5, 1.25, 0.1);
        }

        if (name.Contains("gpt", StringComparison.Ordinal)
            || name.Contains("codex", StringComparison.Ordinal)
            || name.Contains("o4", StringComparison.Ordinal)
            || name.Contains("o3", StringComparison.Ordinal))
        {
            return ModelRate.PerMillion(5, 30, 0, 0.5);
        }

        if (name.StartsWith("gemini", StringComparison.Ordinal))
        {
            if (name.Contains("pro", StringComparison.Ordinal))
            {
                return ModelRate.PerMillion(1.25, 10, 0, 0.3125);
            }

            if (name.Contains("flash", StringComparison.Ordinal))
            {
                return ModelRate.PerMillion(0.30, 2.5, 0, 0.075);
            }
        }

        return ModelRate.Zero;
    }

    public static double Cost(string model, long input, long output, long cacheWrite, long cacheRead) =>
        RateFor(model).Cost(input, output, cacheWrite, cacheRead);
}
