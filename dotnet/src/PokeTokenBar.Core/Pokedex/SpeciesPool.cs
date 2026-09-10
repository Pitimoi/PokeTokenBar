using PokeTokenBar.Core.Companions;

namespace PokeTokenBar.Core.Pokedex;

/// <summary>A species with no pre-evolution, and the fields rarity is derived from.</summary>
public sealed record BaseSpecies
{
    public required int Id { get; init; }

    public required int CaptureRate { get; init; }

    public required bool IsLegendary { get; init; }

    public required bool IsMythical { get; init; }

    public Rarity Rarity => RarityRules.From(CaptureRate, IsLegendary, IsMythical);
}

/// <summary>Cached base-species index.</summary>
public sealed record SpeciesIndexSnapshot
{
    public required DateTimeOffset FetchedAt { get; init; }

    public required IReadOnlyList<BaseSpecies> Entries { get; init; }
}

/// <summary>Cached evolution paths for one chain.</summary>
public sealed record EvolutionPathsSnapshot
{
    public required int BaseSpeciesId { get; init; }

    /// <summary>Root-to-leaf paths as species ids. More than one when the chain branches.</summary>
    public required IReadOnlyList<int[]> Paths { get; init; }
}
