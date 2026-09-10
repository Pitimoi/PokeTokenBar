# PokeTokenBar — C# port (usage core, VS Code sidecar, tray app)

A cross-platform (Windows / macOS / Linux) reimplementation of the usage-reading core, with two
hosts: a deliberately thin VS Code extension, and a standalone tray application (see
[Tray app](#tray-app)). The Swift app it was ported from lives [upstream](https://github.com/chattymin/PokeTokenBar).

## Layout

| Path | Role |
|---|---|
| `src/PokeTokenBar.Core` | Parsing + aggregation. No UI, no host, **no third-party packages**. |
| `src/PokeTokenBar.Sidecar` | Console host. stdio only — never opens a socket or port. |
| `src/PokeTokenBar.Tray` | Desktop host: tray icon + popup (Avalonia). Publishes `status.json` for other tools. |
| `tests/PokeTokenBar.Core.Tests` | xunit. Test-only packages are fine here; `Core` stays dependency-free. |
| `../extension` | VS Code extension (TypeScript). Renders; holds no credentials. |
| `../scripts/claude-statusline` | Claude Code status-line segments reading the tray app's `status.json`. |

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
   this correctly for one provider only and unboundedly elsewhere; that gap is a
   persistent-DoS bug we are not porting.

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

## Tray app

`src/PokeTokenBar.Tray` runs in the background and plays the same game as the VS Code extension
from the system tray:

- Your Claude Code usage is read from its transcripts every minute and credited to a **budget**.
- With no companion, three **eggs** are on offer. Pick one (it costs a fixed number of tokens) and
  it hatches on the spot into a species drawn from the whole Pokédex.
- With a companion, **feed** it: each press spends tokens on its growth; reaching a threshold
  evolves it, and the final form completes the line, after which three new eggs appear.
- The tray icon is the companion's sprite (a pokéball while eggs are on offer). Clicking it opens
  a small popup: the eggs to pick from, or the sprite at 3× with species number, rarity, stage,
  progress and the feed button; then the budget ledger and your Pokédex (every species you have
  owned, as a scrollable grid of sprites). It hides when it loses focus; **Refresh** and **Quit**
  are in the popup, **Open** and **Quit** in the tray menu. Token usage itself is published in
  `status.json` for the status line rather than shown here.
- The save is shared with the sidecar, so both hosts show the same companion and budget.
- It also publishes machine-readable status for other tools — a Claude Code status line and
  spinner verbs are provided (see [Claude Code integration](#claude-code-integration)).

### Requirements

- **.NET 10 SDK** to build. No other packages beyond Avalonia are pulled in.
- **A system tray.** Windows and macOS have one. On Linux the desktop must offer a
  StatusNotifierItem host: KDE Plasma does natively; GNOME needs the *AppIndicator and
  KStatusNotifierItem Support* extension (Ubuntu ships it enabled).
- **Network access** to `pokeapi.co` / `graphql.pokeapi.co` (species, names, evolution chains)
  and `raw.githubusercontent.com` (sprites). Everything is cached after the first download;
  offline, hatching falls back to a built-in set of classic lines and the companion still shows
  its number and progress.

### Build and run

```bash
dotnet run --project src/PokeTokenBar.Tray          # from this directory
```

To install it, publish a Release build to a stable location and run that instead of the source
tree, so rebuilding does not disturb the running app:

```bash
dotnet publish src/PokeTokenBar.Tray -c Release -o <install-dir>
dotnet <install-dir>/PokeTokenBar.Tray.dll
```

There is no single-instance guard: launching it twice gives two icons.

### Start at login (Linux)

A systemd user service, tied to the graphical session so it stops and starts with it:

```ini
# ~/.config/systemd/user/poketokenbar.service
[Unit]
Description=PokeTokenBar tray companion
PartOf=graphical-session.target
After=graphical-session.target

[Service]
ExecStart=/usr/bin/dotnet <install-dir>/PokeTokenBar.Tray.dll
Restart=on-failure
RestartSec=5

[Install]
WantedBy=graphical-session.target
```

```bash
systemctl --user daemon-reload && systemctl --user enable --now poketokenbar
systemctl --user restart poketokenbar        # after publishing a new build
journalctl --user -u poketokenbar -f         # logs
```

On Windows and macOS use the usual login-item mechanisms (Startup folder / Login Items) to run
the published `PokeTokenBar.Tray` executable.

### Data directory

Everything the app writes lives in one folder — `~/.local/share/PokeTokenBar` on Linux (or
`$XDG_DATA_HOME/PokeTokenBar`), `%LOCALAPPDATA%\PokeTokenBar` on Windows,
`~/Library/Application Support/PokeTokenBar` on macOS:

| File | Purpose |
|---|---|
| `companion.json` | The save: budget ledger, eggs on offer or active companion, Pokédex, completed lines (shared with the sidecar). |
| `pokedex/` | Cached species index, evolution chains and names (maintained by `Core`). |
| `sprites/`, `icons/` | Cached sprites, and 64×64 crops of them for tools that render images. |
| `status.json` | For other tools: current companion (id, name, stage, progress, dominant colour, sprite and icon paths) or `null` while eggs are on offer, the budget (available, earned, spent, prices, eggs on offer), last completed line, the Pokédex (ids, names, sprite paths), today's usage. Rewritten on every refresh, atomically. |
| `claude-settings.json` | A Claude Code settings fragment carrying `spinnerVerbs` about the companion. |

Everything but `companion.json` is a cache: deleting the folder loses the save, nothing else.

### Claude Code integration

Both integrations read files from the data directory, so the tray app must be running (or have
run at least once) for them to show anything.

#### Status line

`../scripts/claude-statusline/` contains two independent scripts, each printing one line and
nothing at all when there is nothing to show:

| Script | Output | Example |
|---|---|---|
| `progress.sh` | Companion being raised: icon, number, 10-cell progress bar, percent — or, while eggs are on offer, the eggs and the budget against the hatch price | `● #172 ░░░░░░░░░░ 9%` / `🥚 ×3 · 1.2M / 5.0M` |
| `previous.sh` | Last companion whose line completed: icon, number, name (silent until then) | `● #26 Raichu` |

Requirements: `bash` and `jq`. Call either or both from your own `~/.claude/statusline.sh` and
place the output where you like, e.g.:

```bash
POKE=/path/to/PokeTokenBar/scripts/claude-statusline
PROGRESS=$("$POKE/progress.sh")     # may be empty
PREVIOUS=$("$POKE/previous.sh")     # may be empty
```

The **icon** is the actual sprite in terminals that implement kitty's graphics protocol with
Unicode placeholders — kitty and Ghostty — and a `●` in the sprite's dominant colour everywhere
else (including Windows Terminal, and inside tmux). Ordinary inline-image protocols cannot be used
because Claude Code redraws the status line as text, which wipes them; placeholders survive because
the image is displayed by regular characters. The scripts claim kitty image ids 200 and 201.

Two things to know when laying out the line: the segments contain ANSI colour escapes and, in
kitty/Ghostty, combining marks — strip both before measuring their width; and Claude Code draws
its status line a few columns narrower than the `$COLUMNS` it exports (4 on 2.1.x), so a
right-aligned segment needs that margin or it is truncated with `…`.

#### Spinner verbs

The app keeps `claude-settings.json` in the data directory: a Claude Code settings fragment
whose `spinnerVerbs` are about the companion (`<icon> getting fed`, `Warming up <icon> egg`,
`Playing with <icon>`, …; egg-themed while eggs are on offer), updated when the species changes.
It never edits your own Claude Code
settings; load it with the CLI's `--settings` flag, for instance via a shell function:

```bash
claude() { command claude --settings "$HOME/.local/share/PokeTokenBar/claude-settings.json" "$@"; }
```

Notes:

- Claude Code merges settings from all sources and concatenates arrays, so a `spinnerVerbs` entry
  in `~/.claude/settings.json` is *added to* these rather than replaced. Remove it there to see
  only the companion's verbs.
- The `<icon>` is the same kitty placeholder as in the status line, so it needs a kitty/Ghostty
  terminal and the status line to have transmitted the image at least once in that session.
  Elsewhere it appears as escape text; if you use another terminal, edit the templates in
  `SpinnerVerbs.cs` to use the species name instead.
- Claude Code's spinner animation re-colours the verb per character, which breaks the placeholder
  while it animates (escape text flickers in). `"prefersReducedMotion": true` in Claude Code's
  settings renders it steadily.

### Known limitations

- The popup docks in the corner nearest the tray (top-right; bottom-right on Windows) rather than
  under the icon: the tray protocols do not report the icon's position.
- On GNOME, the appindicator extension opens the menu on a single left click; the popup opens on a
  **double-click** or middle-click.
- Windows and macOS builds compile and use the same code paths but have not been exercised on real
  hardware. The status-line scripts on Windows require Git Bash and `jq`, and depend on Claude Code
  running its status-line command through bash there.

