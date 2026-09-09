using System.Text.Json;
using PokeTokenBar.Sidecar;
using PokeTokenBar.Sidecar.Protocol;
using StreamJsonRpc;

// stdio only. Opening a socket — even on loopback — would make a process that reads other
// tools' credentials reachable by any local process and by any web page the user visits.
// Arguments are ignored on purpose: nothing the caller says may select what this process reads.
//
// PolyTypeJsonFormatter, not SystemTextJsonFormatter: the latter's constructor is annotated
// RequiresDynamicCode, so it is unsafe under AOT even when handed a JsonSerializerContext.
// Both emit UTF-8 JSON, which is what keeps vscode-jsonrpc on the host side interoperable.
#pragma warning disable PolyTypeJson // Evaluation-only API; see "Formatter choice" in dotnet/README.md.
var formatter = new PolyTypeJsonFormatter
{
    TypeShapeProvider = ProtocolShapes.GeneratedTypeShapeProvider,
    // The context's own options, not a fresh instance borrowing its resolver: the camelCase
    // policy lives in JsonSourceGenerationOptions and is baked into the generated metadata.
    JsonSerializerOptions = ProtocolJsonContext.Default.Options,
};
#pragma warning restore PolyTypeJson

using var stdin = Console.OpenStandardInput();
using var stdout = Console.OpenStandardOutput();

var handler = new HeaderDelimitedMessageHandler(stdout, stdin, formatter);
using var rpc = new JsonRpc(handler);

rpc.AddLocalRpcTarget(
    RpcTargetMetadata.FromShape<IUsageService>(),
    new UsageService(),
    new JsonRpcTargetOptions { DisposeOnDisconnect = false });

rpc.StartListening();

try
{
    await rpc.Completion;
}
catch (OperationCanceledException)
{
    // The host closed the pipe: a normal shutdown, not a failure.
}
catch (ObjectDisposedException)
{
}

return 0;
