# PokeTokenBar — C# port (usage core + VS Code sidecar)

A cross-platform (Windows / macOS / Linux) reimplementation of the usage-reading core, hosted by a
deliberately thin VS Code extension. The Swift app in `../Sources/` remains the reference
implementation to port against.

## Layout

| Path | Role |
|---|---|
| `src/PokeTokenBar.Core` | Parsing + aggregation. No UI, no host, **no third-party packages**. |
| `src/PokeTokenBar.Sidecar` | Console host. stdio only — never opens a socket or port. |
| `tests/PokeTokenBar.Core.Tests` | xunit. Test-only packages are fine here; `Core` stays dependency-free. |
| `../extension` | VS Code extension (TypeScript). Renders; holds no credentials. |

## Security invariants

These are load-bearing, not style preferences. They exist because this process reads OAuth tokens and
session cookies belonging to other tools, and because a VS Code host is reachable by any repository the
user opens.

1. **stdio, never a port.** The host spawns the sidecar and speaks newline-delimited JSON over
   stdin/stdout. No listener means no localhost CSRF, no DNS rebinding, no other-process access, and no
   auth token to manage. If a socket ever becomes unavoidable: named pipe (Windows) or Unix domain
   socket at `0600` — never TCP.
2. **Credentials live only in the sidecar.** It reads the OS credential store, calls provider APIs, and
   returns aggregates. The extension host never holds a token, so a webview XSS has none to steal. Do
   not invert this by storing tokens in VS Code `SecretStorage` and passing them down.
3. **The sidecar distrusts the host.** Workspace config (`.vscode/settings.json` in any repo the user
   opens) is attacker-controlled input. The protocol is therefore a closed command enum carrying **no
   paths, no URLs, no binary names** — the host can never say what to execute or which endpoint to
   call. The sidecar resolves its own tool paths and endpoints. Any exec-path setting that must exist
   is declared `"scope": "machine"` (`machine-overridable` is *not* sufficient).
4. **Untrusted input is sanitized in the sidecar.** It parses JSONL written by other tools, containing
   arbitrary model output. It emits escaped, length-capped display strings so the host cannot be
   tricked into rendering raw content. The webview still needs strict CSP + per-load nonce regardless.
5. **Reads are streamed and capped.** Never load a whole log file into memory. The Swift original does
   this correctly for Codex only (`../Sources/PokeTokenBar/Core/LocalUsageReader.swift:673`) and
   unboundedly elsewhere; that gap is a persistent-DoS bug we are not porting.

## Dependency policy

**Don't hand-write what a maintained library already does.** Buffer management, cryptography,
database access, and protocol framing are all places where bespoke code is a liability, not a
virtue. Two constraints shape *which* library, both consequences of what this process is:

- It holds live bearer tokens, so a dependency's blast radius is credential-shaped. Prefer
  first-party (Microsoft) or otherwise widely-audited packages over convenience wrappers.
- It publishes NativeAOT, so reflection-heavy libraries either fail at publish time or bloat the
  binary. `IsAotCompatible` is set on `src/` to surface this at build time rather than later.

In use:

| Need | Choice |
|---|---|
| Streaming line-delimited reads | `System.IO.Pipelines` — buffer management, chunk-boundary handling, pooling |
| JSON | `System.Text.Json`; `JsonDocument.Parse` takes a `ReadOnlySequence<byte>`, so parsing off a pipe is zero-copy |
| HTTP | `HttpClient` |

Planned, when the work that needs them starts:

| Package | Why |
|---|---|
| `StreamJsonRpc` + `vscode-jsonrpc` (npm) | JSON-RPC 2.0 over stdio, Content-Length framed. Both Microsoft, and the pairing VS Code language servers already use. |
| `Microsoft.Data.Sqlite` | Cursor's `state.vscdb`. Hand-written sqlite P/Invoke on three platforms is strictly worse. |
| `System.Security.Cryptography.ProtectedData` | Windows DPAPI — but only if we ever persist a secret of *our own*. Reading other tools' credentials needs no store (see below). |

### Why not a binary protocol

FlatBuffers, Cap'n Proto and friends exist to give zero-copy access to large payloads. This
protocol carries a handful of aggregates at minute cadence, so that buys nothing measurable, and
the costs are real: `flatc` codegen on three platforms plus CI, generated code in two languages,
accessor-based TypeScript ergonomics, and a wire format nobody can eyeball — which directly
undercuts auditing the closed command surface that invariant 3 depends on. gRPC is excluded on
transport grounds: HTTP/2 means a listening port, which invariant 1 rules out.

If payloads later grow to full history series, `NerdbankMessagePackFormatter` is an upgrade path
inside StreamJsonRpc rather than a protocol rewrite.

### Formatter choice

Measured against the AOT analyzers rather than taken from the docs, because the docs were
misleading in both directions:

| Formatter | AOT | Wire |
|---|---|---|
| `PolyTypeJsonFormatter` | Clean — **in use** | UTF-8 JSON |
| `NerdbankMessagePackFormatter` | Clean | MessagePack |
| `SystemTextJsonFormatter` | **Fails** — ctor is `RequiresDynamicCode`/`RequiresUnreferencedCode` | UTF-8 JSON |
| `JsonMessageFormatter` (**default**) | Fails | UTF-8 JSON |

To be clear about where the AOT problem is and is not: **System.Text.Json's source-generated
path is AOT-safe, and it is what serialises every payload here** — that is exactly what
`ProtocolJsonContext` is. The blocker is StreamJsonRpc's `SystemTextJsonFormatter` *wrapper*,
whose constructor carries both annotations, so it cannot be constructed AOT-clean however good a
resolver it is handed. The annotation is on the formatter class, not on STJ.

That annotation may well be conservative, and the formatter might behave correctly at runtime
given a source-generated resolver. Verifying that needs a real AOT publish, which the primary
dev machine cannot currently do (see Prerequisites), so suppressing `IL3050` would mean shipping
an unverified assumption into the only configuration we distribute. `PolyTypeJsonFormatter` is
analyzer-clean today and emits the same JSON, which is what keeps `vscode-jsonrpc` interoperable
on the host side; MessagePack would force a codec onto the TypeScript end.

The catch, and the reason this is written down: `PolyTypeJsonFormatter` is marked
evaluation-only, so the `PolyTypeJson` diagnostic is suppressed at the single use site in
`Program.cs`. Should it be withdrawn, the fallback is `NerdbankMessagePackFormatter` plus a
MessagePack codec on the host. The wire format is JSON either way today, so a swap would not
touch the contract or the host's request shapes.

It serialises in two halves, which is worth knowing before changing either:

- **PolyType shapes** drive the RPC contract plumbing — hence `[GenerateShape(IncludeMethods =
  MethodShapeFlags.PublicInstance)]` on the interface, a `[GenerateShapeFor<T>]` witness, and
  `TypeShapeProvider`.
- **System.Text.Json** serialises the payload types through `JsonSerializerOptions.TypeInfoResolver`.
  Omitting the resolver fails at runtime, not at build time: *"JsonTypeInfo metadata … was not
  provided by TypeInfoResolver of type '<null>'"*.

Assign `ProtocolJsonContext.Default.Options` rather than a fresh `JsonSerializerOptions` that
merely borrows the resolver — the camelCase policy lives in `JsonSourceGenerationOptions` and is
baked into the generated metadata, so a fresh options object silently emits PascalCase.

Server side must use the `AddLocalRpcTarget(RpcTargetMetadata, …)` overload with
`RpcTargetMetadata.FromShape<T>()`; the reflection-based overloads are not AOT-safe.

`IsAotCompatible` plus `TreatWarningsAsErrors` on `src/` is what makes all of this checkable on a
machine that cannot run `dotnet publish` — AOT-hostile calls fail the ordinary build.

Test-only packages are unconstrained; they never ship in the sidecar.

### Protocol robustness, measured

Fuzzed by sending hostile frames to the built binary, one fresh process per case
(`extension/test/protocol.test.mjs` keeps this as a regression suite):

| Input | Result |
|---|---|
| Unknown method | `-32601`, stays alive |
| `System.IO.File.Delete` with a path argument | `-32601` — the closed set holds; no dispatch by type name |
| Wrong parameter shape, extra parameters, 300 KB string argument | `-32602`, stays alive |
| Oversized `Content-Length` | waits for more bytes, stays alive |
| Missing `jsonrpc` member | accepted; StreamJsonRpc is lenient here |
| **Malformed JSON** | **process dies** — unhandled `JsonReaderException` |
| **Nesting past depth 64** | **process dies** — unhandled `JsonReaderException` |

No error response disclosed a filesystem path.

The two crashes are unhandled exceptions from the evaluation-only formatter rather than the
`-32700` parse error the specification calls for. They are **not reachable today**: only the
extension writes frames and `vscode-jsonrpc` constructs them, so no untrusted party can deliver
malformed bytes. Following the defect protocol, an unreachable trigger gets no guard in the
sidecar — but a helper that stays dead leaves a stale number on screen, so the host detects the
exit and restarts with exponential backoff and a hard attempt cap. If a future change forwards
webview or workspace input toward this boundary, the trigger becomes reachable and the crash
must be fixed at the formatter rather than absorbed by the restart.

## Credentials

Verified on a Windows machine with Claude Code installed, and cross-checked against the Swift
original: **there is no per-platform credential store to abstract over for reading.**

- `~/.claude/.credentials.json` holds `claudeAiOauth.{accessToken, refreshToken, expiresAt,
  refreshTokenExpiresAt, scopes, subscriptionType, rateLimitTier}`. Same path on all three
  platforms.
- Windows Credential Manager holds no Claude, Cursor or Gemini entries — the file is the only
  source.
- The Swift original prefers this file over the macOS Keychain, and records a measured 13-second
  block from `SecItemCopyMatching` during a poll (`OAuthLimitsProvider.swift:177`). Keychain is the
  slow fallback, not the primary.
- There is no `libsecret`/`secret-tool` path anywhere in the original: it is macOS-only, so Linux
  was never covered upstream. Nothing extra is needed, since the file path is identical.

So credential *reading* is a JSON file read plus an optional macOS-only Keychain fallback.
Credential *writing* is a separate question that only arises if the session-key feature is ported;
that is the one case needing DPAPI / Keychain / libsecret, and it is deferred.

`subscriptionType` and `rateLimitTier` are present in the file, which may remove the need for an
API call to render a limit tier.

## Prerequisites

- **.NET 10 SDK** (developed against 10.0.302).
- **Node 20+** for the extension.

Day-to-day development needs nothing else:

```bash
dotnet build            # from this directory
dotnet test
dotnet run --project src/PokeTokenBar.Sidecar
```

### NativeAOT publish

`PublishAot` is on for the sidecar, so **`dotnet publish` requires a full C++ toolchain** — it is the
only command that does. Two gotchas, both measured on a Windows 11 dev box:

- `vswhere.exe` must be on `PATH`. It ships at
  `C:\Program Files (x86)\Microsoft Visual Studio\Installer\` but is not added to `PATH` by default,
  and the ILCompiler targets invoke it by bare name. Without it the link step fails with a mangled
  command line rather than a clear error.
- The **Windows SDK** must be installed, not just the MSVC compiler. A partial "Desktop development
  with C++" install links `link.exe` successfully and then fails with
  `LNK1181: cannot open input file 'advapi32.lib'`.

Neither blocks development, because `build`/`run`/`test` never invoke the native toolchain.

**NativeAOT cannot cross-compile.** The macOS and Linux sidecars cannot be produced from Windows, so
per-RID artifacts are built on matching CI runners (`windows-latest`, `macos-latest`, `ubuntu-latest`)
and bundled into platform-specific VSIX builds. Downloading the sidecar on first run is deliberately
rejected: it would reintroduce the unverified-artifact problem the Swift release pipeline has today.
