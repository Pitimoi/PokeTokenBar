using System.Text.Json.Serialization;

namespace PokeTokenBar.Core.Companions;

/// <summary>
/// Species rarity, derived from PokéAPI's <c>capture_rate</c> and legendary flags.
/// </summary>
/// <remarks>
/// Persisted as a string, so inserting a tier or reordering the enum cannot silently reinterpret
/// every saved companion's rarity — and rarity decides its graduation cost.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<Rarity>))]
public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Legendary,
}

public static class RarityRules
{
    /// <summary>
    /// Ascending by value, used to decide whether a drawn species meets a guarantee. Reversing
    /// this order would silently let a guarantee pass something below what it promised, so the
    /// ordering is pinned by a test rather than left to enum declaration order.
    /// </summary>
    public static int SortRank(this Rarity rarity) => rarity switch
    {
        Rarity.Common => 0,
        Rarity.Uncommon => 1,
        Rarity.Rare => 2,
        Rarity.Legendary => 3,
        _ => 0,
    };

    /// <summary>
    /// Highest <c>capture_rate</c> still counting as this rarity or better; <c>null</c> when the
    /// rarity cannot be expressed as a capture rate at all.
    /// </summary>
    /// <remarks>
    /// Legendary is decided by <c>is_legendary</c>/<c>is_mythical</c> and has no ceiling. That
    /// is not a gap: every legendary has a capture rate of 45 or below, so it falls inside the
    /// rare and uncommon filters anyway and "rare or better" still holds. This is the single
    /// source for the thresholds — duplicating them elsewhere is how a guarantee breaks when
    /// only one copy is updated.
    /// </remarks>
    public static int? CaptureRateCeiling(this Rarity rarity) => rarity switch
    {
        Rarity.Rare => 45,
        Rarity.Uncommon => 120,
        Rarity.Common => 255,
        _ => null,
    };

    /// <summary>Whether a capture rate means this rarity or better.</summary>
    public static bool Includes(this Rarity rarity, int captureRate) =>
        rarity.CaptureRateCeiling() is { } ceiling && captureRate <= ceiling;

    public static Rarity From(int captureRate, bool isLegendary, bool isMythical)
    {
        if (isLegendary || isMythical)
        {
            return Rarity.Legendary;
        }

        if (Rarity.Rare.Includes(captureRate))
        {
            return Rarity.Rare;
        }

        return Rarity.Uncommon.Includes(captureRate) ? Rarity.Uncommon : Rarity.Common;
    }
}
