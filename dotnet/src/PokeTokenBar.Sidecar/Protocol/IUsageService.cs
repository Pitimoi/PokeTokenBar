using PolyType;
using StreamJsonRpc;

namespace PokeTokenBar.Sidecar.Protocol;

/// <summary>
/// The sidecar's entire callable surface.
/// </summary>
/// <remarks>
/// Every method here is reachable by whatever spawned this process, and the host derives its
/// behaviour partly from editor configuration that any opened repository can supply. So no
/// parameter may name a path, a URL, an endpoint, or an executable — the sidecar resolves all
/// of those itself. Adding a parameter that does is the one change that would breach the trust
/// boundary; the contract attribute is what makes this set closed and compiler-enforced.
/// </remarks>
[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface IUsageService
{
    /// <summary>Token usage and cost for today, this week and this month.</summary>
    ValueTask<UsageResponse> GetUsageAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sidecar version and whether any transcript root exists, so the host can distinguish
    /// "no usage yet" from "this tool is not installed" without being told any path.
    /// </summary>
    ValueTask<SidecarInfoResponse> GetInfoAsync(CancellationToken cancellationToken);
}
