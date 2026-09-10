using System.Text.Json.Serialization;

namespace PokeTokenBar.Core.Pokedex;

/// <summary>
/// Source-generated serialisation for the cached Pokédex data. Required rather than
/// convenient: the generic overloads that take <c>JsonSerializerOptions</c> are annotated
/// <c>RequiresDynamicCode</c>, so only the <c>JsonTypeInfo</c> ones survive an AOT publish.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SpeciesIndexSnapshot))]
[JsonSerializable(typeof(EvolutionPathsSnapshot))]
[JsonSerializable(typeof(SpeciesNames))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class PokedexJsonContext : JsonSerializerContext;
