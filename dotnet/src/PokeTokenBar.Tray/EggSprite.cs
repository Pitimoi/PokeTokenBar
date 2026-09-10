using System.Net;
using PokeTokenBar.Core.Io;
using PokeTokenBar.Core.Sprites;

namespace PokeTokenBar.Tray;

/// <summary>
/// The games' egg sprite, from the same pinned host and repository as the species sprites,
/// cached beside them. Missing artwork is not an error: the offer still shows plain eggs.
/// </summary>
internal static class EggSprite
{
    private static readonly Uri Url = new("https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/egg.png");
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static string FilePath => Path.Combine(AppPaths.SpriteCache, "egg.png");

    /// <summary>The cached file, downloading it first if absent; null when it cannot be had.</summary>
    public static async ValueTask<string?> EnsureAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(FilePath))
        {
            return FilePath;
        }

        try
        {
            using var response = await Client.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK
                || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true
                || response.Content.Headers.ContentLength is > SpriteCache.MaxSpriteBytes)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > SpriteCache.MaxSpriteBytes)
            {
                return null;
            }

            AppPaths.EnsureDirectory(AppPaths.SpriteCache);
            var temporary = FilePath + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, FilePath, overwrite: true);
            return FilePath;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }
}
