using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Core.Companions;

/// <summary>Outcome of feeding a day's usage to the companion.</summary>
public sealed record CompanionUpdate
{
    public required CompanionState State { get; init; }

    /// <summary>Species evolved into during this update, in order.</summary>
    public required IReadOnlyList<int> Evolutions { get; init; }

    /// <summary>Set when a line completed and a fresh companion replaced it.</summary>
    public int? GraduatedSpeciesId { get; init; }
}

/// <summary>
/// Advances a companion from observed usage.
/// </summary>
/// <remarks>
/// Progress comes from the growth of *today's* total rather than an all-time figure, which the
/// reader does not produce. A watermark of the last total seen for a given day turns a series
/// of absolute readings into a monotonic stream of deltas, and a day change resets it — so
/// neither a refresh that sees no new usage nor a day rollover can move progress backwards.
/// </remarks>
public static class CompanionKeeper
{
    public static CompanionUpdate Apply(CompanionState state, string today, long todayTokens)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(today);

        var current = state.Sanitized();
        var observed = Math.Max(0, todayTokens);

        var delta = string.Equals(current.WatermarkDay, today, StringComparison.Ordinal)
            ? Math.Max(0, observed - current.WatermarkTokens)
            : observed;

        var advance = CompanionProgression.Apply(current.ToCompanion(), delta);

        var next = current with
        {
            SpeciesPath = advance.Companion.SpeciesPath,
            StageIndex = advance.Companion.StageIndex,
            TokensAtStage = advance.Companion.TokensAtStage,
            WatermarkDay = today,
            WatermarkTokens = observed,
        };

        if (!advance.Graduated)
        {
            return new CompanionUpdate { State = next, Evolutions = advance.Evolutions };
        }

        // A completed line is recorded and replaced, so there is always a companion to show.
        // The seed advances rather than being redrawn from scratch, keeping the next draw
        // deterministic from the same persisted value.
        var graduatedId = advance.Companion.CurrentSpeciesId;
        var replacement = CompanionState.Hatch(unchecked(current.Seed * 31 + graduatedId)) with
        {
            WatermarkDay = today,
            WatermarkTokens = observed,
            Graduated = [.. next.Graduated, graduatedId],
        };

        return new CompanionUpdate
        {
            State = replacement,
            Evolutions = advance.Evolutions,
            GraduatedSpeciesId = graduatedId,
        };
    }

    /// <summary>Creates a companion for a machine that has never had one.</summary>
    public static CompanionState Hatch() => CompanionState.Hatch(Random.Shared.Next());

    /// <summary>Convenience for driving from a usage snapshot.</summary>
    public static CompanionUpdate Apply(CompanionState state, UsageTotals today) =>
        Apply(state, today.ToDay, today.Total);
}
