namespace PokeTokenBar.Core.Companions;

/// <summary>
/// What things cost. Tokens are earned passively and spent deliberately.
/// </summary>
/// <remarks>
/// The phase thresholds in <see cref="PokemonBalance"/> are unchanged, so the elapsed-time
/// calibration is untouched: a common line still costs 750M tokens, roughly three days at a
/// measured ~253M a day. <see cref="ClickCost"/> therefore only decides how many presses that
/// takes, not how long it takes — it is an ergonomics knob, not an economic one.
/// </remarks>
public static class CompanionEconomy
{
    /// <summary>Cost of taking one of the offered eggs, which hatches it immediately.</summary>
    public const long HatchPrice = 5_000_000;

    /// <summary>
    /// Tokens added to a companion's progress per press. At 25M a common line is 30 presses
    /// spread over its three days, an uncommon one 75.
    /// </summary>
    public const long ClickCost = 25_000_000;

    /// <summary>How many eggs are offered at once.</summary>
    public const int OfferSize = 3;

    /// <summary>
    /// Seeds for a fresh offer, derived from <paramref name="baseSeed"/> so the same save always
    /// presents the same three eggs — a restart must not reshuffle a choice not yet made.
    /// </summary>
    public static int[] NewOffer(int baseSeed)
    {
        var seeds = new int[OfferSize];
        for (var i = 0; i < OfferSize; i++)
        {
            // Cheap mixing: distinct, deterministic, and spread enough that three eggs from one
            // base do not collapse onto the same species.
            unchecked
            {
                var mixed = baseSeed * 2654435761 + ((i + 1) * 40503);
                seeds[i] = (int)(mixed ^ (mixed >> 13));
            }
        }

        return seeds;
    }
}
