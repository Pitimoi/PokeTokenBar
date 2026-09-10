namespace PokeTokenBar.Sidecar.Protocol;

/// <summary>The companion as the host needs to render it.</summary>
public sealed record CompanionResponse
{
    /// <summary>Tokens earned and not yet spent.</summary>
    public required long Budget { get; init; }

    /// <summary>What taking an egg costs.</summary>
    public required long HatchPrice { get; init; }

    /// <summary>What one press on a companion costs.</summary>
    public required long ClickCost { get; init; }

    /// <summary>
    /// How many eggs are on offer. Nothing else about them is sent: the species behind each is
    /// derived from its seed only once chosen, so there is nothing here to spoil the choice.
    /// </summary>
    public required int OfferCount { get; init; }

    /// <summary>False while there is only an offer to choose from.</summary>
    public required bool HasCompanion { get; init; }

    /// <summary>True when the budget covers taking an egg.</summary>
    public required bool CanHatch { get; init; }

    /// <summary>True when there is a companion and the budget covers a press.</summary>
    public required bool CanAdvance { get; init; }

    /// <summary>Why the last spend was refused; empty when it was accepted or none was asked for.</summary>
    public string Refusal { get; init; } = string.Empty;

    public required int SpeciesId { get; init; }

    /// <summary>
    /// Species name, sanitised and length-capped because it comes from an external API. Empty
    /// when unknown, in which case the host falls back to the dex number.
    /// </summary>
    public required string SpeciesName { get; init; }

    public required int StageIndex { get; init; }

    public required int TotalForms { get; init; }

    /// <summary>Progress through the current form, 0 to 1.</summary>
    public required double StageProgress { get; init; }

    public required long TokensAtStage { get; init; }

    public required long StageThreshold { get; init; }

    /// <summary>Rarity name. A string so the host is not coupled to enum ordering.</summary>
    public required string Rarity { get; init; }

    /// <summary>Forms reached so far, for drawing the line.</summary>
    public required IReadOnlyList<int> ReachedForms { get; init; }

    /// <summary>Species evolved into by the spend that produced this response.</summary>
    public required IReadOnlyList<int> JustEvolved { get; init; }

    /// <summary>Set when a line completed and retired.</summary>
    public int? JustGraduated { get; init; }

    /// <summary>Set to the revealed species when an egg was taken and hatched.</summary>
    public int? JustHatched { get; init; }

    /// <summary>Every species ever owned, in the order first seen — the Pokédex.</summary>
    public required IReadOnlyList<int> Pokedex { get; init; }

    /// <summary>Lines carried all the way to their final form.</summary>
    public required IReadOnlyList<int> Graduated { get; init; }

    /// <summary>Names for every species mentioned in this response, keyed by dex id.</summary>
    public required IReadOnlyDictionary<int, string> Names { get; init; }

    /// <summary>
    /// Cache-relative sprite filenames for the Pokédex, keyed by dex id. Filenames only, for
    /// the same reason as <see cref="SpriteFileName"/>: the host validates each before joining
    /// it to <see cref="SpriteDirectory"/>.
    /// </summary>
    public required IReadOnlyDictionary<int, string> CollectionSprites { get; init; }

    /// <summary>
    /// Cache-relative sprite filename, or null when it could not be fetched. Never a URL and
    /// never a path: the host joins it to <see cref="SpriteDirectory"/> after validating it.
    /// </summary>
    public string? SpriteFileName { get; init; }

    /// <summary>
    /// Directory holding cached sprites, so the host can narrow its webview resource roots to
    /// exactly this folder and nothing else.
    /// </summary>
    public required string SpriteDirectory { get; init; }
}
