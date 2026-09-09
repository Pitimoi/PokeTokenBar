namespace PokeTokenBar.Core.Usage;

/// <summary>Token usage for one model within a day.</summary>
public sealed record ModelUsage
{
    /// <summary>Sanitised model identifier; safe to render.</summary>
    public required string Model { get; init; }

    public required long Input { get; init; }

    public required long Output { get; init; }

    public required long CacheWrite { get; init; }

    public required long CacheRead { get; init; }

    public long Total => Input + Output + CacheWrite + CacheRead;
}

/// <summary>Token usage for one local calendar day, broken down by model.</summary>
public sealed record DailyUsage
{
    public required string LocalDay { get; init; }

    public required long Input { get; init; }

    public required long Output { get; init; }

    public required long CacheWrite { get; init; }

    public required long CacheRead { get; init; }

    /// <summary>Descending by total, so a caller can render the top contributors directly.</summary>
    public required IReadOnlyList<ModelUsage> Models { get; init; }

    public long Total => Input + Output + CacheWrite + CacheRead;

    public static DailyUsage Empty(string localDay) => new()
    {
        LocalDay = localDay,
        Input = 0,
        Output = 0,
        CacheWrite = 0,
        CacheRead = 0,
        Models = [],
    };
}
