using System.Globalization;
using System.Text.Json;
using PokeTokenBar.Core.Companions;
using PokeTokenBar.Core.Io;
using PokeTokenBar.Core.Usage;
using SysIO = System.IO;

namespace PokeTokenBar.Core.Pokedex;

/// <summary>
/// The pool companions are drawn from, backed by PokéAPI and cached on disk.
/// </summary>
/// <remarks>
/// Every path degrades rather than failing: a companion must exist even with no network, no
/// cache and a hostile response, so an unavailable index falls back to the built-in lines. The
/// cache makes the second run and every run after it offline-capable.
/// </remarks>
public sealed class SpeciesLibrary
{
    private readonly PokeApiClient _api;
    private readonly string _directory;
    private Dictionary<string, string>? _names;

    public SpeciesLibrary(PokeApiClient? api = null, string? directory = null)
    {
        _api = api ?? new PokeApiClient();
        _directory = directory ?? SysIO.Path.Combine(AppPaths.DataRoot, "pokedex");
    }

    /// <summary>Draws a line for <paramref name="seed"/>, persisted so a restart cannot redraw.</summary>
    public async ValueTask<EvolutionLine> DrawAsync(int seed, CancellationToken cancellationToken = default)
    {
        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        if (index.Count == 0)
        {
            return EvolutionLines.FromSeed(seed);
        }

        var species = Pick(index, seed);
        var paths = await LoadPathsAsync(species.Id, cancellationToken).ConfigureAwait(false);

        if (paths.Count == 0)
        {
            // The species is real but its chain did not resolve. Keep it, and mark the path
            // unresolved so a later refresh can extend it rather than leaving a three-form
            // species permanently stuck as single-form.
            return new EvolutionLine
            {
                SpeciesPath = [species.Id],
                Rarity = species.Rarity,
                Resolved = false,
            };
        }

        // A branching chain (Eevee) picks a branch from the same seed, so the choice is stable
        // across restarts for the same companion.
        var path = paths[(int)((uint)(seed >> 8) % (uint)paths.Count)];
        return new EvolutionLine { SpeciesPath = path, Rarity = species.Rarity };
    }

    /// <summary>
    /// Re-resolves a line whose chain was previously unavailable, returning null when it still
    /// cannot be resolved or when the species genuinely has no evolutions.
    /// </summary>
    public async ValueTask<EvolutionLine?> ResolveAsync(
        int baseSpeciesId,
        Rarity rarity,
        int seed,
        CancellationToken cancellationToken = default)
    {
        var paths = await LoadPathsAsync(baseSpeciesId, cancellationToken).ConfigureAwait(false);
        if (paths.Count == 0)
        {
            return null;
        }

        var path = paths[(int)((uint)(seed >> 8) % (uint)paths.Count)];
        return new EvolutionLine { SpeciesPath = path, Rarity = rarity, Resolved = true };
    }

    /// <summary>
    /// Weighted so rarer species stay rare. A uniform draw over the index made legendaries
    /// roughly one draw in seven, because generations I to V hold dozens of them.
    /// </summary>
    private static BaseSpecies Pick(IReadOnlyList<BaseSpecies> index, int seed)
    {
        var weights = new int[index.Count];
        var total = 0L;
        for (var i = 0; i < index.Count; i++)
        {
            weights[i] = Weight(index[i].Rarity);
            total += weights[i];
        }

        var point = (long)((ulong)(uint)seed % (ulong)total);
        for (var i = 0; i < index.Count; i++)
        {
            point -= weights[i];
            if (point < 0)
            {
                return index[i];
            }
        }

        return index[0];
    }

    private static int Weight(Rarity rarity) => rarity switch
    {
        Rarity.Common => 40,
        Rarity.Uncommon => 12,
        Rarity.Rare => 4,
        Rarity.Legendary => 1,
        _ => 40,
    };

    /// <summary>True when a usable index is already on disk, so a draw needs no network.</summary>
    public bool HasCachedIndex => SysIO.File.Exists(IndexPath);

    /// <summary>
    /// Display name for a species, or null when it has never been seen. Names accumulate as
    /// species are encountered, so a mid-chain form or a graduated one can be named without a
    /// lookup of its own.
    /// </summary>
    public string? NameFor(int speciesId)
    {
        var names = LoadNames();
        return names.TryGetValue(speciesId.ToString(CultureInfo.InvariantCulture), out var name)
            ? DisplayText.SanitizeIdentifier(name, 24)
            : null;
    }

    private Dictionary<string, string> LoadNames()
    {
        if (_names is not null)
        {
            return _names;
        }

        var snapshot = ReadCache(NamesPath, PokedexJsonContext.Default.SpeciesNames);
        _names = snapshot?.ById ?? [];
        return _names;
    }

    private void RememberNames(IEnumerable<KeyValuePair<int, string>> learned)
    {
        var names = LoadNames();
        var changed = false;

        foreach (var (id, name) in learned)
        {
            if (id < 1 || name.Length == 0)
            {
                continue;
            }

            var key = id.ToString(CultureInfo.InvariantCulture);
            if (!names.ContainsKey(key))
            {
                names[key] = name;
                changed = true;
            }
        }

        if (changed)
        {
            WriteCache(NamesPath, new SpeciesNames { ById = names }, PokedexJsonContext.Default.SpeciesNames);
        }
    }

    private string NamesPath => SysIO.Path.Combine(_directory, "names.json");

    private string IndexPath => SysIO.Path.Combine(_directory, "base-index.json");

    private string PathsPath(int speciesId) =>
        SysIO.Path.Combine(
            _directory,
            string.Create(CultureInfo.InvariantCulture, $"chain-{speciesId}.json"));

    private async ValueTask<IReadOnlyList<BaseSpecies>> LoadIndexAsync(CancellationToken cancellationToken)
    {
        var cached = ReadCache(IndexPath, PokedexJsonContext.Default.SpeciesIndexSnapshot);
        if (cached is { Entries.Count: > 0 })
        {
            RememberNames(cached.Entries.Select(e => new KeyValuePair<int, string>(e.Id, e.Name)));
            return cached.Entries;
        }

        var fetched = await _api.GetBaseSpeciesAsync(cancellationToken).ConfigureAwait(false);

        // A truncated index would permanently narrow the pool, so it is only worth caching when
        // it is plausibly complete. Generation I to V has a few hundred base species.
        if (fetched.Count >= 100)
        {
            WriteCache(
                IndexPath,
                new SpeciesIndexSnapshot { FetchedAt = DateTimeOffset.UtcNow, Entries = fetched },
                PokedexJsonContext.Default.SpeciesIndexSnapshot);
        }

        RememberNames(fetched.Select(e => new KeyValuePair<int, string>(e.Id, e.Name)));
        return fetched;
    }

    private async ValueTask<IReadOnlyList<int[]>> LoadPathsAsync(int speciesId, CancellationToken cancellationToken)
    {
        var file = PathsPath(speciesId);
        var cached = ReadCache(file, PokedexJsonContext.Default.EvolutionPathsSnapshot);
        if (cached is { Paths.Count: > 0 })
        {
            return cached.Paths;
        }

        var chainId = await _api.GetChainIdAsync(speciesId, cancellationToken).ConfigureAwait(false);
        if (chainId is null)
        {
            return [];
        }

        var chain = await _api.GetEvolutionChainAsync(chainId.Value, cancellationToken).ConfigureAwait(false);
        RememberNames(chain.Names);
        var usable = chain.Paths.Where(p => p.Length > 0 && p[0] == speciesId).ToArray();

        if (usable.Length > 0)
        {
            WriteCache(
                file,
                new EvolutionPathsSnapshot { BaseSpeciesId = speciesId, Paths = usable },
                PokedexJsonContext.Default.EvolutionPathsSnapshot);
        }

        return usable;
    }

    private static T? ReadCache<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            return SysIO.File.Exists(path)
                ? JsonSerializer.Deserialize(SysIO.File.ReadAllText(path), typeInfo)
                : null;
        }
        catch (JsonException)
        {
            // A corrupt cache entry is worth discarding silently; it is re-fetchable.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void WriteCache<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        try
        {
            AppPaths.EnsureDirectory(_directory);
            var temporary = path + ".tmp";
            SysIO.File.WriteAllText(temporary, JsonSerializer.Serialize(value, typeInfo));
            SysIO.File.Move(temporary, path, overwrite: true);
        }
        catch (IOException)
        {
            // Caching is an optimisation; failing to cache must not fail the draw.
        }
    }
}
