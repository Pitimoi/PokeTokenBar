using System.Globalization;

namespace PokeTokenBar.Core.Sprites;

/// <summary>Which artwork is wanted for a species.</summary>
public sealed record SpriteRequest
{
    public required int SpeciesId { get; init; }

    public bool Animated { get; init; }

    public bool Shiny { get; init; }
}

/// <summary>
/// Builds sprite URLs and cache filenames. Everything is derived from an integer species id, so
/// no caller-supplied string ever reaches a URL or a path.
/// </summary>
public static class SpriteSource
{
    public const string Host = "raw.githubusercontent.com";

    /// <summary>Highest species id accepted. Bounds the URL space and rejects nonsense early.</summary>
    public const int MaxSpeciesId = 1400;

    /// <summary>
    /// Animated sprites exist only for generations I to V in the source repository, so asking
    /// beyond this yields a 404 rather than a picture.
    /// </summary>
    public const int MaxAnimatedSpeciesId = 649;

    private const string Base = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon";

    public static bool IsValid(SpriteRequest request) =>
        request is not null
        && request.SpeciesId >= 1
        && request.SpeciesId <= MaxSpeciesId
        && (!request.Animated || request.SpeciesId <= MaxAnimatedSpeciesId);

    /// <summary>
    /// Absolute URL for the request, or <c>null</c> when the request is out of range. Always
    /// HTTPS on the pinned host — no part of it comes from a caller.
    /// </summary>
    public static Uri? UrlFor(SpriteRequest request)
    {
        if (!IsValid(request))
        {
            return null;
        }

        var id = request.SpeciesId.ToString(CultureInfo.InvariantCulture);
        var path = (request.Animated, request.Shiny) switch
        {
            (true, false) => $"{Base}/versions/generation-v/black-white/animated/{id}.gif",
            (true, true) => $"{Base}/versions/generation-v/black-white/animated/shiny/{id}.gif",
            (false, false) => $"{Base}/{id}.png",
            (false, true) => $"{Base}/shiny/{id}.png",
        };

        return new Uri(path, UriKind.Absolute);
    }

    /// <summary>
    /// Cache filename, matching the original's scheme so an existing cache stays valid. Contains
    /// only digits and a fixed suffix, so it cannot escape its directory.
    /// </summary>
    public static string? FileNameFor(SpriteRequest request)
    {
        if (!IsValid(request))
        {
            return null;
        }

        var suffix = request.Shiny ? "sh" : string.Empty;
        var kind = request.Animated ? "a" : "s";
        var extension = request.Animated ? "gif" : "png";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{request.SpeciesId}-{suffix}{kind}.{extension}");
    }

    /// <summary>
    /// Whether a filename is one this module could have produced. The host validates what the
    /// sidecar hands it before joining it to a directory, so a bug on either side cannot turn
    /// into a path escape.
    /// </summary>
    public static bool IsCacheFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.Length > 32)
        {
            return false;
        }

        if (fileName.Contains('/', StringComparison.Ordinal)
            || fileName.Contains('\\', StringComparison.Ordinal)
            || fileName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var dash = fileName.IndexOf('-', StringComparison.Ordinal);
        var dot = fileName.LastIndexOf('.');
        if (dash <= 0 || dot <= dash)
        {
            return false;
        }

        var digits = fileName.AsSpan(0, dash);
        foreach (var c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        var middle = fileName.AsSpan(dash + 1, dot - dash - 1);
        var extension = fileName.AsSpan(dot + 1);

        var middleOk = middle is "a" or "s" or "sha" or "shs";
        var extensionOk = extension is "png" or "gif";
        return middleOk && extensionOk;
    }
}
