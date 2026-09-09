namespace PokeTokenBar.Core.Usage;

/// <summary>Token usage and cost for one model within a window.</summary>
public sealed record ModelUsage
{
    /// <summary>Sanitised model identifier; safe to render.</summary>
    public required string Model { get; init; }

    public required long Input { get; init; }

    public required long Output { get; init; }

    public required long CacheWrite { get; init; }

    public required long CacheRead { get; init; }

    /// <summary>Estimated USD. Zero for models with no published rate, never a guess.</summary>
    public required double Cost { get; init; }

    public long Total => Input + Output + CacheWrite + CacheRead;
}

/// <summary>
/// Token usage over an inclusive range of local days, broken down by model. A single day is the
/// degenerate case where both bounds are equal.
/// </summary>
public sealed record UsageTotals
{
    public required string FromDay { get; init; }

    public required string ToDay { get; init; }

    public required long Input { get; init; }

    public required long Output { get; init; }

    public required long CacheWrite { get; init; }

    public required long CacheRead { get; init; }

    public required double Cost { get; init; }

    /// <summary>Descending by total, so a caller can render the top contributors directly.</summary>
    public required IReadOnlyList<ModelUsage> Models { get; init; }

    public long Total => Input + Output + CacheWrite + CacheRead;

    public static UsageTotals Empty(string fromDay, string toDay) => new()
    {
        FromDay = fromDay,
        ToDay = toDay,
        Input = 0,
        Output = 0,
        CacheWrite = 0,
        CacheRead = 0,
        Cost = 0,
        Models = [],
    };
}
