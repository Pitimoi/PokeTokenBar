namespace PokeTokenBar.Core.Usage;

/// <summary>One assistant turn's token usage, normalised across providers.</summary>
public sealed record UsageEntry
{
    /// <summary>
    /// Deduplication key — <c>message.id | requestId</c>. Streaming re-emits the same turn
    /// repeatedly, so totals are wrong without collapsing on this.
    /// </summary>
    public required string Id { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Calendar day in local time (<c>yyyy-MM-dd</c>), the unit totals bucket by.</summary>
    public required string LocalDay { get; init; }

    /// <summary>Model name, sanitised for display — it originates in an untrusted log.</summary>
    public required string Model { get; init; }

    /// <summary>Counts are 64-bit because a real corpus total exceeds <see cref="int.MaxValue"/>.</summary>
    public required long Input { get; init; }

    public required long Output { get; init; }

    public required long CacheWrite { get; init; }

    public required long CacheRead { get; init; }

    public long Total => Input + Output + CacheWrite + CacheRead;
}
