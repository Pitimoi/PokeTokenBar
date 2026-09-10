using System.Globalization;
using System.Text.Json;
using PokeTokenBar.Core.Io;
using PokeTokenBar.Core.Sprites;

namespace PokeTokenBar.Tray;

/// <summary>
/// English species names, fetched once from PokeAPI's data mirror on the same pinned host the
/// sprites come from, then kept on disk. A missing name is not an error: the companion still
/// has a number.
/// </summary>
internal sealed class SpeciesNames
{
    private const string Base = "https://raw.githubusercontent.com/PokeAPI/api-data/master/data/api/v2/pokemon-species/";

    private readonly HttpClient _client;
    private readonly string _path = Path.Combine(AppPaths.DataRoot, "names.json");
    private Dictionary<string, string>? _cache;

    public SpeciesNames(HttpClient? client = null)
    {
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async ValueTask<string?> GetAsync(int speciesId, CancellationToken cancellationToken)
    {
        if (speciesId is < 1 or > SpriteSource.MaxSpeciesId)
        {
            return null;
        }

        var cache = _cache ??= Load();
        var key = speciesId.ToString(CultureInfo.InvariantCulture);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            using var response = await _client
                .GetAsync(new Uri(Base + key + "/index.json"), cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var name = EnglishName(document.RootElement);
            if (name is null)
            {
                return null;
            }

            cache[key] = name;
            Save(cache);
            return name;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    private static string? EnglishName(JsonElement species)
    {
        if (species.TryGetProperty("names", out var names) && names.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in names.EnumerateArray())
            {
                if (entry.TryGetProperty("language", out var language)
                    && language.TryGetProperty("name", out var code)
                    && code.ValueEquals("en")
                    && entry.TryGetProperty("name", out var name))
                {
                    return name.GetString();
                }
            }
        }

        return species.TryGetProperty("name", out var slug) ? slug.GetString() : null;
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(_path), StatusJsonContext.Default.DictionaryStringString) ?? [];
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
        }

        return [];
    }

    private void Save(Dictionary<string, string> cache)
    {
        AppPaths.EnsureDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(cache, StatusJsonContext.Default.DictionaryStringString));
        File.Move(temporary, _path, overwrite: true);
    }
}
