namespace PokeTokenBar.Core.Companions;

/// <summary>
/// The active companion: where it is along its evolution line and how much of the current
/// form's growth is done.
/// </summary>
public sealed record Companion
{
    /// <summary>
    /// Species ids along the evolution line, first form first. Only forms up to
    /// <see cref="StageIndex"/> have actually been reached.
    /// </summary>
    public required IReadOnlyList<int> SpeciesPath { get; init; }

    public required int StageIndex { get; init; }

    /// <summary>Tokens spent since reaching the current form.</summary>
    public required long TokensAtStage { get; init; }

    public required Rarity Rarity { get; init; }

    public int TotalForms => Math.Max(1, SpeciesPath.Count);

    /// <summary>
    /// Stage clamped into the path. On-disk state can be corrupt or from an older layout, and
    /// a companion with no displayable form is worse than one showing its first.
    /// </summary>
    public int SafeStageIndex => Math.Clamp(StageIndex, 0, TotalForms - 1);

    public int CurrentSpeciesId => SpeciesPath.Count == 0 ? 0 : SpeciesPath[SafeStageIndex];

    /// <summary>Forms actually reached, for rendering the line so far.</summary>
    public IReadOnlyList<int> ReachedForms => SpeciesPath.Take(SafeStageIndex + 1).ToArray();

    /// <summary>
    /// The one species still "in progress": the current stage of a line that has not yet
    /// graduated. Every other reached form — earlier stages of this same line included — is
    /// locked in as owned, so only this one is worth marking as not finished yet.
    /// </summary>
    public int? PendingSpeciesId => SpeciesPath.Count > 0 && !HasGraduated ? CurrentSpeciesId : null;

    public bool IsFinalStage => SafeStageIndex >= TotalForms - 1;

    /// <summary>Tokens needed at the current form to evolve, or to graduate if it is the last.</summary>
    public long StageThreshold => PokemonBalance.PhaseThreshold(Rarity, TotalForms, SafeStageIndex);

    /// <summary>Progress through the current form, clamped to 0..1.</summary>
    public double StageProgress => StageThreshold <= 0
        ? 1
        : Math.Clamp(TokensAtStage / (double)StageThreshold, 0, 1);

    /// <summary>Whether the final form has met its threshold and the line is complete.</summary>
    public bool HasGraduated => IsFinalStage && TokensAtStage >= StageThreshold;

    public static Companion Hatch(IReadOnlyList<int> speciesPath, Rarity rarity, long carriedTokens = 0) =>
        new()
        {
            SpeciesPath = speciesPath.Count == 0 ? [0] : speciesPath,
            StageIndex = 0,
            TokensAtStage = Math.Max(0, carriedTokens),
            Rarity = rarity,
        };
}

/// <summary>What changed when tokens were applied, so a host can announce it.</summary>
public sealed record CompanionAdvance
{
    public required Companion Companion { get; init; }

    /// <summary>Species ids evolved into, in order. Empty when nothing changed.</summary>
    public required IReadOnlyList<int> Evolutions { get; init; }

    public required bool Graduated { get; init; }

    /// <summary>Set to the revealed species when an egg hatched on this update.</summary>
    public int? Hatched { get; init; }
}
