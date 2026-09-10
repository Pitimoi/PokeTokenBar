namespace PokeTokenBar.Core.Companions;

/// <summary>
/// The token economy. Calibrated against a measured average of roughly 253M tokens a day, so a
/// common line graduates in about three days of heavy use.
/// </summary>
/// <remarks>
/// Totals are 64-bit deliberately: the legendary graduation total alone exceeds
/// <see cref="int.MaxValue"/>, so the Swift original's <c>Int</c> cannot be ported to
/// <c>int</c> even for the constants.
/// </remarks>
public static class PokemonBalance
{
    /// <summary>
    /// Total tokens to take a line from its first form to graduation. Equal for every line of a
    /// given rarity, independent of how many forms it has.
    /// </summary>
    public static long GraduationTotal(Rarity rarity) => rarity switch
    {
        Rarity.Common => 750_000_000,
        Rarity.Uncommon => 1_875_000_000,
        Rarity.Rare => 3_000_000_000,
        Rarity.Legendary => 6_000_000_000,
        _ => 750_000_000,
    };

    /// <summary>
    /// Tokens needed at <paramref name="stageIndex"/> to reach the next form, or to graduate
    /// from the final one.
    /// </summary>
    /// <remarks>
    /// Growing the i-th form of a k-form line costs <c>T·i / (k(k+1)/2)</c>, so later forms cost
    /// progressively more and the costs sum to exactly <c>T</c> regardless of k. That identity
    /// is what keeps graduation effort equal across lines of the same rarity, and it is pinned
    /// by a test.
    /// </remarks>
    public static long PhaseThreshold(Rarity rarity, int totalForms, int stageIndex)
    {
        var forms = Math.Max(1, totalForms);
        var stage = Math.Max(0, stageIndex) + 1;
        var total = (double)GraduationTotal(rarity);
        var denominator = forms * (forms + 1) / 2.0;

        return (long)Math.Round(total * stage / denominator, MidpointRounding.AwayFromZero);
    }
}
