using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Core.Companions;

/// <summary>Why a spend was refused, or <see cref="None"/> when it was not.</summary>
public enum SpendRefusal
{
    None,
    NotEnoughBudget,
    NoSuchEgg,
    AlreadyHasCompanion,
    NoCompanion,
}

/// <summary>Outcome of a deliberate spend.</summary>
public sealed record SpendResult
{
    public required CompanionState State { get; init; }

    public required SpendRefusal Refusal { get; init; }

    public bool Accepted => Refusal == SpendRefusal.None;

    /// <summary>Seed of the egg taken, so the caller can resolve its line.</summary>
    public int? ChosenSeed { get; init; }

    /// <summary>Species evolved into during this spend, in order.</summary>
    public IReadOnlyList<int> Evolutions { get; init; } = [];

    /// <summary>Set when the line reached its final form and was retired.</summary>
    public int? GraduatedSpeciesId { get; init; }

    public static SpendResult Refuse(CompanionState state, SpendRefusal refusal) =>
        new() { State = state, Refusal = refusal };
}

/// <summary>
/// Earning and spending. Tokens accrue into a budget on their own; nothing else happens
/// without an explicit spend.
/// </summary>
public static class CompanionKeeper
{
    /// <summary>
    /// Credits the growth of today's total to the budget. Progress is never applied here.
    /// </summary>
    /// <remarks>
    /// A watermark of the last total seen for a given day turns a series of absolute readings
    /// into a monotonic stream of deltas, so neither a refresh that sees no new usage, nor a
    /// day rollover, nor a lower reading after a deleted transcript can credit twice or credit
    /// a negative amount.
    /// </remarks>
    public static CompanionState CreditBudget(CompanionState state, string today, long todayTokens)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(today);

        var current = state.Sanitized();
        var observed = Math.Max(0, todayTokens);

        var delta = string.Equals(current.WatermarkDay, today, StringComparison.Ordinal)
            ? Math.Max(0, observed - current.WatermarkTokens)
            : observed;

        return current with
        {
            Earned = current.Earned + delta,
            WatermarkDay = today,
            WatermarkTokens = observed,
        };
    }

    /// <summary>
    /// Takes the egg at <paramref name="offerIndex"/>, which hatches it immediately.
    /// </summary>
    /// <remarks>
    /// The species behind the seed is not resolved here, because that needs the network. The
    /// caller applies <see cref="CompanionState.WithLine"/> once it has drawn the line, which
    /// is also what enters the species in the Pokédex.
    /// </remarks>
    public static SpendResult ChooseEgg(CompanionState state, int offerIndex)
    {
        ArgumentNullException.ThrowIfNull(state);

        var current = state.Sanitized();
        var offer = current.OfferSeeds ?? [];

        if (current.HasCompanion)
        {
            return SpendResult.Refuse(current, SpendRefusal.AlreadyHasCompanion);
        }

        // Range-checked rather than trusted: the index arrives from the host, and the host
        // takes direction from an editor that any opened repository can influence.
        if (offerIndex < 0 || offerIndex >= offer.Count)
        {
            return SpendResult.Refuse(current, SpendRefusal.NoSuchEgg);
        }

        if (current.Available < CompanionEconomy.HatchPrice)
        {
            return SpendResult.Refuse(current, SpendRefusal.NotEnoughBudget);
        }

        var seed = offer[offerIndex];

        return new SpendResult
        {
            // The other two eggs go with the offer, so the choice has weight.
            State = current with
            {
                Spent = current.Spent + CompanionEconomy.HatchPrice,
                OfferSeeds = [],
                Seed = seed,
            },
            Refusal = SpendRefusal.None,
            ChosenSeed = seed,
        };
    }

    /// <summary>
    /// Spends one press worth of budget on the active companion, evolving or retiring it if
    /// that crosses a threshold.
    /// </summary>
    public static SpendResult Advance(CompanionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var current = state.Sanitized();

        if (!current.HasCompanion)
        {
            return SpendResult.Refuse(current, SpendRefusal.NoCompanion);
        }

        if (current.Available < CompanionEconomy.ClickCost)
        {
            return SpendResult.Refuse(current, SpendRefusal.NotEnoughBudget);
        }

        var advance = CompanionProgression.Apply(current.ToCompanion(), CompanionEconomy.ClickCost);

        var next = current with
        {
            Spent = current.Spent + CompanionEconomy.ClickCost,
            SpeciesPath = advance.Companion.SpeciesPath,
            StageIndex = advance.Companion.StageIndex,
            TokensAtStage = advance.Companion.TokensAtStage,
        };

        // Each form reached earns its own Pokédex entry: the list records what has been raised,
        // not only what was finished.
        foreach (var species in advance.Evolutions)
        {
            next = next.WithPokedexEntry(species);
        }

        // Updated on every step forward, not only a finished line.
        if (advance.Evolutions.Count > 0 || advance.Graduated)
        {
            next = next with { LastReachedSpeciesId = advance.Companion.CurrentSpeciesId };
        }

        if (!advance.Graduated)
        {
            return new SpendResult
            {
                State = next,
                Refusal = SpendRefusal.None,
                Evolutions = advance.Evolutions,
            };
        }

        var graduatedId = advance.Companion.CurrentSpeciesId;

        return new SpendResult
        {
            // The line retires and a fresh trio appears. The seed advances rather than being
            // redrawn, so the next offer stays deterministic from the persisted value.
            State = next with
            {
                SpeciesPath = [],
                StageIndex = 0,
                TokensAtStage = 0,
                OfferSeeds = CompanionEconomy.NewOffer(unchecked(next.Seed * 31 + graduatedId)),
                Graduated = [.. next.Graduated, graduatedId],
            },
            Refusal = SpendRefusal.None,
            Evolutions = advance.Evolutions,
            GraduatedSpeciesId = graduatedId,
        };
    }

    /// <summary>Creates a game for a machine that has never had one.</summary>
    public static CompanionState New() => CompanionState.New(Random.Shared.Next());

    /// <summary>Convenience for crediting from a usage snapshot.</summary>
    public static CompanionState CreditBudget(CompanionState state, UsageTotals today) =>
        CreditBudget(state, today.ToDay, today.Total);
}
