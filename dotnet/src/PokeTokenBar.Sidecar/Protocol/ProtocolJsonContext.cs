using System.Text.Json.Serialization;
using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Sidecar.Protocol;

/// <summary>
/// Source-generated serialisation for every type crossing the protocol boundary.
/// </summary>
/// <remarks>
/// Load-bearing under NativeAOT: <c>PolyTypeJsonFormatter</c> resolves payload types through
/// <c>JsonSerializerOptions.TypeInfoResolver</c>, and without a generated resolver it fails at
/// runtime rather than at build time. See "Formatter choice" in dotnet/README.md.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UsageResponse))]
[JsonSerializable(typeof(SidecarInfoResponse))]
[JsonSerializable(typeof(ScanReport))]
[JsonSerializable(typeof(CompanionResponse))]
[JsonSerializable(typeof(UsageTotals))]
[JsonSerializable(typeof(ModelUsage))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
