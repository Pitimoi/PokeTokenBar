namespace PokeTokenBar.Core.Companions;

/// <summary>
/// Everything persisted about a companion between runs.
/// </summary>
public sealed record CompanionState
{
    public required IReadOnlyList<int> SpeciesPath { get; init; }

    public required int StageIndex { get; init; }

    public required long TokensAtStage { get; init; }

    public required Rarity Rarity { get; init; }

    /// <summary>Seed the line was drawn from, so a restart cannot redraw a different species.</summary>
    public required int Seed { get; init; }

    /// <summary>
    /// Local day the watermark below belongs to. Progress is driven by the growth of today's
    /// total, so the watermark has to be discarded when the day rolls over.
    /// </summary>
    public required string WatermarkDay { get; init; }

    /// <summary>Today's token total the last time progress was applied.</summary>
    public required long WatermarkTokens { get; init; }

    /// <summary>Lines completed so far, oldest first — the beginnings of a collection.</summary>
    public required IReadOnlyList<int> Graduated { get; init; }

    /// <summary>
    /// False when the evolution path could not be fetched and may be truncated. Not required,
    /// so a save written before this existed reads as unresolved and gets one re-attempt rather
    /// than staying wrong forever.
    /// </summary>
    public bool PathResolved { get; init; }

    public Companion ToCompanion() => new()
    {
        SpeciesPath = SpeciesPath,
        StageIndex = StageIndex,
        TokensAtStage = TokensAtStage,
        Rarity = Rarity,
    };

    public static CompanionState Hatch(int seed)
    {
        var line = EvolutionLines.FromSeed(seed);
        return new CompanionState
        {
            SpeciesPath = line.SpeciesPath,
            StageIndex = 0,
            TokensAtStage = 0,
            Rarity = line.Rarity,
            Seed = seed,
            WatermarkDay = string.Empty,
            WatermarkTokens = 0,
            Graduated = [],
            PathResolved = false,
        };
    }

    /// <summary>
    /// Replaces the line while keeping identity and history: the seed, the day watermark and
    /// the collection all survive, because only the species being raised is changing.
    /// </summary>
    public CompanionState WithLine(EvolutionLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return line.SpeciesPath.Count == 0
            ? this
            : this with
            {
                SpeciesPath = line.SpeciesPath,
                Rarity = line.Rarity,
                StageIndex = 0,
                TokensAtStage = 0,
                PathResolved = line.Resolved,
            };
    }

    /// <summary>
    /// Extends a previously truncated path in place, keeping the companion's progress. Only the
    /// tail grows, so the stage it has already reached stays valid.
    /// </summary>
    public CompanionState WithResolvedPath(EvolutionLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        // The replacement must start with the same species, or it is a different companion.
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

    /// <summary>
    /// Repairs values that cannot be right.
    /// </summary>
    /// <remarks>
    /// Applied on every load, not only when importing. The original learned this the hard way:
    /// clamping solely at the import boundary leaves already-persisted extreme values to crash
    /// on each subsequent launch.
    /// </remarks>
    public CompanionState Sanitized()
    {
        var path = SpeciesPath is { Count: > 0 }
            ? SpeciesPath.Where(static id => id is > 0 and <= 1400).ToArray()
            : [];

        if (path.Length == 0)
        {
            return Hatch(Seed);
        }

        return this with
        {
            SpeciesPath = path,
            StageIndex = Math.Clamp(StageIndex, 0, path.Length - 1),
            TokensAtStage = Math.Clamp(TokensAtStage, 0, PokemonBalance.GraduationTotal(Rarity)),
            WatermarkTokens = Math.Max(0, WatermarkTokens),
            WatermarkDay = WatermarkDay.Length <= 10 ? WatermarkDay : string.Empty,
            Graduated = Graduated?.Where(static id => id is > 0 and <= 1400).Take(500).ToArray() ?? [],
        };
    }
}
