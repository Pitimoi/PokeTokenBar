using System.Text.Json.Serialization;
using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Sidecar.Protocol;

/// <summary>
/// Source-generated serialisation for every type crossing the protocol boundary.
/// </summary>
/// <remarks>
/// Required by NativeAOT: <c>SystemTextJsonFormatter</c> is only AOT-safe when given a
/// generated resolver. StreamJsonRpc's default <c>JsonMessageFormatter</c> is not AOT-ready at
/// all, so the formatter choice in <c>Program</c> is deliberate rather than incidental.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TodayUsageResponse))]
[JsonSerializable(typeof(SidecarInfoResponse))]
[JsonSerializable(typeof(ScanReport))]
[JsonSerializable(typeof(DailyUsage))]
[JsonSerializable(typeof(ModelUsage))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
