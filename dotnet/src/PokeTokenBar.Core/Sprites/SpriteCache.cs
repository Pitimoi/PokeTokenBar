using System.Net;
using PokeTokenBar.Core.Io;

namespace PokeTokenBar.Core.Sprites;

public sealed record SpriteResult
{
    /// <summary>Cache-relative filename, or <c>null</c> when no sprite could be produced.</summary>
    public string? FileName { get; init; }

    public bool FromCache { get; init; }

    /// <summary>Why a fetch produced nothing. Null on success.</summary>
    public string? Failure { get; init; }
}

/// <summary>
/// Downloads sprites once and serves them from disk afterwards.
/// </summary>
/// <remarks>
/// All network access lives here rather than in the host: this process already owns credentials
/// and outbound calls, and handing the host a URL to fetch would widen what it can be pointed
/// at. The host only ever receives a filename.
/// </remarks>
public sealed class SpriteCache
{
    /// <summary>
    /// Generous for a sprite — real ones are 0.5–1 KB static and a few tens of KB animated —
    /// while still bounding what a compromised or substituted host could make us write.
    /// </summary>
    public const int MaxSpriteBytes = 2 * 1024 * 1024;

    private readonly HttpClient _client;
    private readonly string _directory;

    public SpriteCache(HttpClient? client = null, string? directory = null)
    {
        _client = client ?? CreateClient();
        _directory = directory ?? AppPaths.SpriteCache;
    }

    public string Directory => _directory;

    /// <summary>
    /// Returns the cached filename, downloading it first if absent. Never throws for an
    /// unreachable network or a missing sprite — a companion without artwork still has a name
    /// and a level, so this degrades rather than failing the whole request.
    /// </summary>
    public async ValueTask<SpriteResult> GetAsync(SpriteRequest request, CancellationToken cancellationToken = default)
    {
        var fileName = SpriteSource.FileNameFor(request);
        if (fileName is null)
        {
            return new SpriteResult { Failure = "species id out of range" };
        }

        var path = Path.Combine(_directory, fileName);
        if (File.Exists(path))
        {
            return new SpriteResult { FileName = fileName, FromCache = true };
        }

        var url = SpriteSource.UrlFor(request);
        if (url is null)
        {
            return new SpriteResult { Failure = "no url for request" };
        }

        try
        {
            var bytes = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                return new SpriteResult { Failure = "rejected response" };
            }

            await WriteAtomicallyAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            return new SpriteResult { FileName = fileName };
        }
        catch (HttpRequestException)
        {
            return new SpriteResult { Failure = "network unavailable" };
        }
        catch (TaskCanceledException)
        {
            return new SpriteResult { Failure = "timed out" };
        }
        catch (IOException)
        {
            return new SpriteResult { Failure = "cache not writable" };
        }
    }

    /// <summary>
    /// Returns the cached filename for a held berry, downloading it first if absent. Same
    /// degrade-rather-than-fail contract as <see cref="GetAsync"/> — a feed prompt without
    /// artwork just falls back to the plain feed emoji.
    /// </summary>
    public async ValueTask<SpriteResult> GetBerryAsync(BerryRequest request, CancellationToken cancellationToken = default)
    {
        var fileName = BerrySource.FileNameFor(request);
        if (fileName is null)
        {
            return new SpriteResult { Failure = "berry index out of range" };
        }

        var path = Path.Combine(_directory, fileName);
        if (File.Exists(path))
        {
            return new SpriteResult { FileName = fileName, FromCache = true };
        }

        var url = BerrySource.UrlFor(request);
        if (url is null)
        {
            return new SpriteResult { Failure = "no url for request" };
        }

        try
        {
            var bytes = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                return new SpriteResult { Failure = "rejected response" };
            }

            await WriteAtomicallyAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            return new SpriteResult { FileName = fileName };
        }
        catch (HttpRequestException)
        {
            return new SpriteResult { Failure = "network unavailable" };
        }
        catch (TaskCanceledException)
        {
            return new SpriteResult { Failure = "timed out" };
        }
        catch (IOException)
        {
            return new SpriteResult { Failure = "cache not writable" };
        }
    }

    private async ValueTask<byte[]?> DownloadAsync(Uri url, CancellationToken cancellationToken)
    {
        // Belt and braces: the URL is built entirely from an integer, but asserting the scheme
        // and host here means a future change to that builder cannot silently start talking to
        // somewhere else.
        if (!url.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !url.Host.Equals(SpriteSource.Host, StringComparison.Ordinal))
        {
            return null;
        }

        using var response = await _client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Reject on the declared length before reading a byte, then enforce the cap again while
        // reading, because Content-Length is a claim rather than a guarantee.
        if (response.Content.Headers.ContentLength is > MaxSpriteBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaxSpriteBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.Length == 0 ? null : buffer.ToArray();
    }

    private async ValueTask WriteAtomicallyAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        AppPaths.EnsureDirectory(_directory);

        // Write beside the target and move into place, so a crash or a concurrent reader never
        // sees a half-written image.
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);

        try
        {
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException)
        {
            File.Delete(temporary);
            throw;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PokeTokenBar/1.0");
        return client;
    }
}
