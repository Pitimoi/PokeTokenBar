using System.Text.Json.Serialization;

namespace PokeTokenBar.Core.Companions;

/// <summary>
/// Source-generated serialisation for the persisted companion. Required rather than convenient:
/// reflection-based serialisation is unavailable once the sidecar is published with NativeAOT.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(CompanionState))]
internal sealed partial class CompanionJsonContext : JsonSerializerContext;
