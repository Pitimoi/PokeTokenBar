using PolyType;

namespace PokeTokenBar.Sidecar.Protocol;

/// <summary>
/// PolyType witness supplying compile-time shapes for every type crossing the protocol
/// boundary, which is what lets the formatter serialise without reflection under NativeAOT.
/// </summary>
[GenerateShapeFor<TodayUsageResponse>]
[GenerateShapeFor<SidecarInfoResponse>]
internal sealed partial class ProtocolShapes;
