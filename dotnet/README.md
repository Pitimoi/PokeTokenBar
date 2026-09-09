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

### StreamJsonRpc under NativeAOT

Formatter choice is the whole game, and the default is the wrong one:

| Formatter | AOT |
|---|---|
| `NerdbankMessagePackFormatter` | Fully supported, officially recommended |
| `SystemTextJsonFormatter` | Semi-safe — needs `JsonSerializerContext`, `[JsonSerializable]`, `RegisterGenericType<T>()` |
| `JsonMessageFormatter` (**default**) | **Not AOT-ready** |

Server side must use the `AddLocalRpcTarget(RpcTargetMetadata, …)` overload and apply
`[JsonRpcContract]` + `[GenerateShape]` to the contract interface. That attribute is also what
satisfies invariant 3: the callable surface is declared once and enforced by the compiler, which is
a stronger closed-set guarantee than a hand-written dispatch switch.

Test-only packages are unconstrained; they never ship in the sidecar.

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
