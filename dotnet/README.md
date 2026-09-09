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
| `Microsoft.Data.Sqlite` | Cursor's `state.vscdb`. Hand-written sqlite P/Invoke on three platforms is strictly worse. |
| `System.Security.Cryptography.ProtectedData` | Windows DPAPI; not in the cross-platform base BCL. |
| `vscode-jsonrpc` (npm) | Host-side protocol framing. The LSP team's implementation, not a bespoke one. |

The one place bespoke wins is the **protocol surface**: invariant 3 requires a closed command set,
and a JSON-RPC framework that dispatches by reflection over a target object widens exactly what
that invariant narrows. So the sidecar's command dispatch is explicit even though its framing is
not. That is a deliberate exception, argued on surface area — not a general preference for
hand-rolling.

Test-only packages are unconstrained; they never ship in the sidecar.

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
