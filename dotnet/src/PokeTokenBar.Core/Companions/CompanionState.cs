namespace PokeTokenBar.Core.Companions;

/// <summary>
/// Everything persisted about the game between runs.
/// </summary>
/// <remarks>
/// Fields added after the first release are deliberately not <c>required</c>, and are treated as
/// possibly null in <see cref="Sanitized"/>. A property initialiser does not survive
/// source-generated deserialisation, so a save written before a field existed yields null rather
/// than the initialiser's value — and marking one <c>required</c> would make every older save
/// fail to parse and be quarantined instead of migrated.
/// </remarks>
public sealed record CompanionState
{
    /// <summary>Tokens earned and not yet spent. Nothing progresses without spending this.</summary>
    public long Budget { get; init; }

    /// <summary>
    /// Seeds for the eggs currently offered, one per egg. Empty while a companion is active.
    /// The species behind each is derived from its seed only when chosen, so nothing is
    /// revealed early and a restart cannot redraw a choice not yet made.
    /// </summary>
    public IReadOnlyList<int>? OfferSeeds { get; init; }

    /// <summary>The active companion's line. Empty means no companion, only an offer.</summary>
    public required IReadOnlyList<int> SpeciesPath { get; init; }

    public required int StageIndex { get; init; }

    /// <summary>Tokens spent on the current form.</summary>
    public required long TokensAtStage { get; init; }

    public required Rarity Rarity { get; init; }

    /// <summary>Seed the active line was drawn from.</summary>
    public required int Seed { get; init; }

    /// <summary>
    /// False when the evolution path could not be fetched and may be truncated, so a later
    /// refresh should re-attempt it.
    /// </summary>
    public bool PathResolved { get; init; }

    /// <summary>
    /// Local day the watermark belongs to. Budget is credited from the growth of today's total,
    /// so the watermark has to be discarded when the day rolls over.
    /// </summary>
    public required string WatermarkDay { get; init; }

    /// <summary>Today's token total the last time budget was credited.</summary>
    public required long WatermarkTokens { get; init; }

    /// <summary>
    /// Every species ever owned, in the order first seen — entered when an egg hatches and
    /// again on each evolution, so it records what has been raised rather than only what was
    /// finished.
    /// </summary>
    public IReadOnlyList<int>? Pokedex { get; init; }

    /// <summary>Lines carried all the way to their final form.</summary>
    public required IReadOnlyList<int> Graduated { get; init; }

    /// <summary>True when there is a companion to spend on, rather than an offer to choose from.</summary>
    public bool HasCompanion => SpeciesPath.Count > 0;

    public Companion ToCompanion() => new()
    {
        SpeciesPath = SpeciesPath,
        StageIndex = StageIndex,
        TokensAtStage = TokensAtStage,
        Rarity = Rarity,
    };

    /// <summary>A brand new game: no companion, three eggs on offer, nothing banked.</summary>
    public static CompanionState New(int seed) => new()
    {
        Budget = 0,
        OfferSeeds = CompanionEconomy.NewOffer(seed),
        SpeciesPath = [],
        StageIndex = 0,
        TokensAtStage = 0,
        Rarity = Rarity.Common,
        Seed = seed,
        PathResolved = false,
        WatermarkDay = string.Empty,
        WatermarkTokens = 0,
        Pokedex = [],
        Graduated = [],
    };

    /// <summary>Adopts a drawn line as the active companion and records it in the Pokédex.</summary>
    public CompanionState WithLine(EvolutionLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.SpeciesPath.Count == 0)
        {
            return this;
        }

        return (this with
        {
            SpeciesPath = line.SpeciesPath,
            Rarity = line.Rarity,
            StageIndex = 0,
            TokensAtStage = 0,
            PathResolved = line.Resolved,
        }).WithPokedexEntry(line.SpeciesPath[0]);
    }

    /// <summary>Extends a previously truncated path, keeping the stage already reached.</summary>
    public CompanionState WithResolvedPath(EvolutionLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.SpeciesPath.Count == 0
            || SpeciesPath.Count == 0
            || line.SpeciesPath[0] != SpeciesPath[0])
        {
            return this;
        }

        return this with
        {
            SpeciesPath = line.SpeciesPath,
            StageIndex = Math.Clamp(StageIndex, 0, line.SpeciesPath.Count - 1),
            PathResolved = true,
        };
    }

    /// <summary>Records a species as owned. Order is first-seen, and entries are never repeated.</summary>
    public CompanionState WithPokedexEntry(int speciesId)
    {
        var seen = Pokedex ?? [];
        if (speciesId < 1 || seen.Contains(speciesId))
        {
            return this;
        }

        return this with { Pokedex = [.. seen, speciesId] };
    }

    /// <summary>
    /// Repairs values that cannot be right. Applied on every load, not only on import: clamping
    /// solely at the boundary leaves an already-bad file to reload badly forever.
    /// </summary>
    public CompanionState Sanitized()
    {
        var path = (SpeciesPath ?? []).Where(static id => id is > 0 and <= 1400).ToArray();
        var offer = (OfferSeeds ?? []).Take(CompanionEconomy.OfferSize).ToArray();

        // Neither a companion nor an offer is a dead end rather than a valid state: there would
        // be nothing to spend on and nothing to choose.
        if (path.Length == 0 && offer.Length == 0)
        {
            return New(Seed) with
            {
                Budget = Math.Max(0, Budget),
                Pokedex = Keep(Pokedex),
                Graduated = Keep(Graduated),
                WatermarkDay = WatermarkDay.Length <= 10 ? WatermarkDay : string.Empty,
                WatermarkTokens = Math.Max(0, WatermarkTokens),
            };
        }

        return this with
        {
            Budget = Math.Max(0, Budget),
            OfferSeeds = offer,
            SpeciesPath = path,
            StageIndex = path.Length == 0 ? 0 : Math.Clamp(StageIndex, 0, path.Length - 1),
            TokensAtStage = Math.Clamp(TokensAtStage, 0, PokemonBalance.GraduationTotal(Rarity)),
            WatermarkTokens = Math.Max(0, WatermarkTokens),
            WatermarkDay = WatermarkDay.Length <= 10 ? WatermarkDay : string.Empty,
            Pokedex = Keep(Pokedex),
            Graduated = Keep(Graduated),
        };
    }

    private static int[] Keep(IReadOnlyList<int>? ids) =>
        (ids ?? []).Where(static id => id is > 0 and <= 1400).Take(2000).ToArray();
}
