namespace PokeTokenBar.Core.Companions;

/// <summary>Applies spent tokens to a companion, evolving it as thresholds are met.</summary>
public static class CompanionProgression
{
    /// <summary>
    /// Adds <paramref name="tokensSpent"/> and advances as far as it reaches.
    /// </summary>
    /// <remarks>
    /// A single call can cross more than one form: usage arrives in a batch after a refresh
    /// interval, and a large batch legitimately spans several thresholds. Surplus carries into
    /// the next form rather than being discarded, so no tokens are lost at a boundary.
    /// </remarks>
    public static CompanionAdvance Apply(Companion companion, long tokensSpent)
    {
        ArgumentNullException.ThrowIfNull(companion);

        var current = companion with
        {
            StageIndex = companion.SafeStageIndex,
            TokensAtStage = companion.TokensAtStage + Math.Max(0, tokensSpent),
        };

        var evolutions = new List<int>();

        while (!current.IsFinalStage && current.TokensAtStage >= current.StageThreshold)
        {
            var carried = current.TokensAtStage - current.StageThreshold;
            current = current with
            {
                StageIndex = current.StageIndex + 1,
                TokensAtStage = carried,
            };
            evolutions.Add(current.CurrentSpeciesId);
        }

        return new CompanionAdvance
        {
            Companion = current,
            Evolutions = evolutions,
            Graduated = current.HasGraduated,
        };
    }
}
