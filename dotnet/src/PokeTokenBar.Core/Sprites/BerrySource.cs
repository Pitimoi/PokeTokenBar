namespace PokeTokenBar.Core.Sprites;

/// <summary>Which held berry is wanted, by index into the curated list in <see cref="BerrySource"/>.</summary>
public sealed record BerryRequest
{
    public required int Index { get; init; }
}

/// <summary>
/// Builds berry sprite URLs and cache filenames from a fixed, curated list of the games' held
/// berries. Everything is derived from an integer index into <see cref="Names"/>, so — like
/// <see cref="SpriteSource"/> — no caller-supplied string ever reaches a URL or a path.
/// </summary>
public static class BerrySource
{
    private const string Base = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/items/berries";

    private static readonly string[] Names =
    [
        "aguav", "apicot", "aspear", "babiri", "belue", "bluk", "charti", "cheri", "chesto",
        "chilan", "chople", "coba", "colbur", "cornn", "custap", "durin", "enigma", "figy",
        "ganlon", "grepa", "haban", "hondew", "iapapa", "jaboca", "kasib", "kebia", "kelpsy",
        "lansat", "leppa", "liechi", "lum", "mago", "magost", "micle", "nanab", "nomel",
        "occa", "oran", "pamtre", "passho", "payapa", "pecha", "persim", "petaya", "pinap",
        "pomeg", "qualot", "rabuta", "rawst", "razz", "rindo", "rowap", "salac", "shuca",
        "sitrus", "spelon", "starf", "tamato", "tanga", "wacan", "watmel", "wepear", "wiki",
        "yache",
    ];

    /// <summary>How many berries <see cref="BerryRequest.Index"/> can select.</summary>
    public static int Count => Names.Length;

    public static bool IsValid(BerryRequest request) =>
        request is not null && request.Index >= 0 && request.Index < Names.Length;

    /// <summary>
    /// Absolute URL for the request, or <c>null</c> when the index is out of range. Always HTTPS
    /// on the pinned host — no part of it comes from a caller.
    /// </summary>
    public static Uri? UrlFor(BerryRequest request) =>
        IsValid(request) ? new Uri($"{Base}/{Names[request.Index]}-berry.png", UriKind.Absolute) : null;

    /// <summary>Cache filename. Contains only digits and a fixed prefix/suffix, so it cannot escape its directory.</summary>
    public static string? FileNameFor(BerryRequest request) =>
        IsValid(request) ? FormattableString.Invariant($"berry-{request.Index}.png") : null;

    /// <summary>
    /// Whether a filename is one this module could have produced. The host validates what the
    /// sidecar hands it before joining it to a directory, so a bug on either side cannot turn
    /// into a path escape.
    /// </summary>
    public static bool IsCacheFileName(string? fileName)
    {
        const string prefix = "berry-";
        const string suffix = ".png";

        if (string.IsNullOrEmpty(fileName) || fileName.Length > 16
            || fileName.Length < prefix.Length + suffix.Length + 1)
        {
            return false;
        }

        if (!fileName.StartsWith(prefix, StringComparison.Ordinal)
            || !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = fileName.AsSpan(prefix.Length, fileName.Length - prefix.Length - suffix.Length);
        foreach (var c in digits)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
