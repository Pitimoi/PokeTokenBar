using System.Net;
using System.Text;
using System.Text.Json;

namespace PokeTokenBar.Core.Pokedex;

/// <summary>
/// Reads species data from PokéAPI.
/// </summary>
/// <remarks>
/// Both hosts are pinned and every response is size-capped and content-type checked. Species
/// identifiers are parsed out of the URLs the API returns rather than those URLs being
/// followed — a server-supplied URL is exactly the input an SSRF guard exists for, and an
/// integer cannot redirect anything.
/// </remarks>
public sealed class PokeApiClient(HttpClient? client = null)
{
    public const string GraphQlHost = "graphql.pokeapi.co";
    public const string RestHost = "pokeapi.co";

    /// <summary>The base index is a few hundred small records; this is generous for it.</summary>
    public const int MaxResponseBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Animated sprites exist only through generation V, and a companion with no artwork is a
    /// worse outcome than a smaller pool.
    /// </summary>
    public const int MaxSpeciesId = 649;

    private static readonly Uri GraphQlEndpoint = new("https://graphql.pokeapi.co/v1beta2");

    private readonly HttpClient _client = client ?? CreateClient();

    /// <summary>
    /// Every species with no pre-evolution, with the fields rarity is derived from. One request
    /// rather than several hundred.
    /// </summary>
    public async ValueTask<IReadOnlyList<BaseSpecies>> GetBaseSpeciesAsync(
        CancellationToken cancellationToken = default)
    {
        const string query =
            "{ pokemonspecies(where: {evolves_from_species_id: {_is_null: true}, id: {_lte: "
            + "649}}, order_by: {id: asc}) { id capture_rate is_legendary is_mythical } }";

        var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["query"] = query },
            PokedexJsonContext.Default.DictionaryStringString);

        using var request = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

        var json = await ReadJsonAsync(request, GraphQlHost, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return [];
        }

        using var document = json;
        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("pokemonspecies", out var species)
            || species.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<BaseSpecies>(species.GetArrayLength());
        foreach (var entry in species.EnumerateArray())
        {
            if (!entry.TryGetProperty("id", out var id) || !id.TryGetInt32(out var speciesId))
            {
                continue;
            }

            if (speciesId is < 1 or > MaxSpeciesId)
            {
                continue;
            }

            results.Add(new BaseSpecies
            {
                Id = speciesId,
                CaptureRate = ReadInt(entry, "capture_rate", 255),
                IsLegendary = ReadBool(entry, "is_legendary"),
                IsMythical = ReadBool(entry, "is_mythical"),
            });
        }

        return results;
    }

    /// <summary>
    /// Every root-to-leaf path of the chain a species belongs to, as species ids. Branching
    /// chains yield more than one path.
    /// </summary>
    public async ValueTask<IReadOnlyList<int[]>> GetEvolutionPathsAsync(
        int chainId,
        CancellationToken cancellationToken = default)
    {
        if (chainId < 1)
        {
            return [];
        }

        var url = new Uri($"https://{RestHost}/api/v2/evolution-chain/{chainId}/");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var json = await ReadJsonAsync(request, RestHost, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return [];
        }

        using var document = json;
        if (!document.RootElement.TryGetProperty("chain", out var chain))
        {
            return [];
        }

        var paths = new List<int[]>();
        Walk(chain, [], paths);
        return paths;
    }

    /// <summary>Chain id for a species, needed because a species does not name its own chain.</summary>
    public async ValueTask<int?> GetChainIdAsync(int speciesId, CancellationToken cancellationToken = default)
    {
        if (speciesId is < 1 or > MaxSpeciesId)
        {
            return null;
        }

        var url = new Uri($"https://{RestHost}/api/v2/pokemon-species/{speciesId}/");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var json = await ReadJsonAsync(request, RestHost, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        using var document = json;
        if (!document.RootElement.TryGetProperty("evolution_chain", out var chain)
            || !chain.TryGetProperty("url", out var chainUrl)
            || chainUrl.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return TrailingId(chainUrl.GetString());
    }

    /// <summary>
    /// Last path segment of a PokéAPI URL as an integer. Parsing the id rather than following
    /// the URL is what keeps a server-supplied string from choosing what gets fetched.
    /// </summary>
    internal static int? TrailingId(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var segments = url.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            if (int.TryParse(segments[i], out var value) && value > 0)
            {
                return value;
            }
        }

        return null;
    }

    private static void Walk(JsonElement node, List<int> prefix, List<int[]> paths)
    {
        var id = node.TryGetProperty("species", out var species)
                 && species.TryGetProperty("url", out var url)
                 && url.ValueKind == JsonValueKind.String
            ? TrailingId(url.GetString())
            : null;

        // A species outside the sprite range ends the usable path rather than discarding it.
        // Teddiursa -> Ursaring -> Ursaluna is chain 110, and Ursaluna is 901: dropping the
        // whole branch would leave Teddiursa looking like a single-form species. Every
        // generation I-V line whose evolution was added later has this shape.
        if (id is null or < 1 || id > MaxSpeciesId)
        {
            if (prefix.Count > 0)
            {
                paths.Add([.. prefix]);
            }

            return;
        }

        var path = new List<int>(prefix) { id.Value };

        if (!node.TryGetProperty("evolves_to", out var next)
            || next.ValueKind != JsonValueKind.Array
            || next.GetArrayLength() == 0)
        {
            paths.Add([.. path]);
            return;
        }

        // Guard against a cyclic or absurdly deep chain from malformed data.
        if (path.Count >= 8)
        {
            paths.Add([.. path]);
            return;
        }

        foreach (var child in next.EnumerateArray())
        {
            Walk(child, path, paths);
        }
    }

    private async ValueTask<JsonDocument?> ReadJsonAsync(
        HttpRequestMessage request,
        string expectedHost,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is null
            || !request.RequestUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !request.RequestUri.Host.Equals(expectedHost, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            {
                return null;
            }

            var bytes = await ReadCappedAsync(response, cancellationToken).ConfigureAwait(false);
            return bytes is null ? null : JsonDocument.Parse(bytes);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async ValueTask<byte[]?> ReadCappedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
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

            // Enforced while reading as well as against the declared length, because
            // Content-Length is a claim rather than a guarantee.
            if (buffer.Length + read > MaxResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.Length == 0 ? null : buffer.ToArray();
    }

    private static int ReadInt(JsonElement parent, string name, int fallback) =>
        parent.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.Number
        && field.TryGetInt32(out var value)
            ? value
            : fallback;

    private static bool ReadBool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.True;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PokeTokenBar/1.0");
        return client;
    }
}
