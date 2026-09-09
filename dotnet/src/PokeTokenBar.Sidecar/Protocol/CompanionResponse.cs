namespace PokeTokenBar.Sidecar.Protocol;

/// <summary>The companion as the host needs to render it.</summary>
public sealed record CompanionResponse
{
    public required int SpeciesId { get; init; }

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

    /// <summary>Species evolved into during this refresh, so the host can announce it.</summary>
    public required IReadOnlyList<int> JustEvolved { get; init; }

    /// <summary>Set when a line completed on this refresh.</summary>
    public int? JustGraduated { get; init; }

    public required int GraduatedCount { get; init; }

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
