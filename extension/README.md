# PokeTokenBar — VS Code host

Status bar item showing today's AI-tool token usage, fed by the C# sidecar in `../dotnet`.
Architecture and the security invariants this host has to uphold are in
[`dotnet/README.md`](https://github.com/kalmanbalint/PokeTokenBar/blob/VS_csharp-port/dotnet/README.md).

## Running it

The extension is not installed anywhere — it runs from source, so it will not appear in the
Extensions list until it is packaged into a VSIX and installed. To run it:

**F5** with either the repository root or this `extension/` folder open. Both have a `.vscode/`
with an `extensionHost` launch configuration; F5 without one does nothing useful, and the
repository root looks like a Swift package to VS Code, which is why the configuration is needed
rather than optional.

The launch configuration runs a build task first. That task is not a convenience: the extension
resolves its executable from its own install directory and never from a setting, so the sidecar
has to be *staged* into `server/` rather than pointed at. Without it you get
`helper executable missing or not executable`.

From a terminal instead:

```bash
dotnet build -c Release ../dotnet/PokeTokenBar.slnx
npm install
npm run dev      # stages the sidecar into server/, then compiles
npm test         # 17 tests; the protocol suite drives the real binary
```

`server/` is gitignored — it holds per-platform binaries — but it is deliberately *not*
`.vscodeignore`d, because the binary ships inside the VSIX.

## What to expect

**F5 opens a second window**, titled `[Extension Development Host]`. The item appears in *that*
window's status bar, not the window you pressed F5 in — easy to miss with several windows open,
and the likeliest reason to conclude nothing happened.

**Click the `$(graph)` item to open the companion.** The sidebar view is lazy — VS Code does
not resolve it until it first becomes visible, so nothing renders until you open it. There is
also a pokeball icon in the activity bar.

A `$(graph)` item on the right of the status bar showing today's total. Hovering shows today,
week and month with estimated cost and a per-model breakdown. Clicking refreshes.

Other states, all of them intentional rather than error paths:

| Status item | Meaning |
|---|---|
| `$(shield) Usage: trust required` | Untrusted workspace. The helper is not started at all. |
| `$(circle-slash) Usage` | No Claude Code transcripts on this machine. |
| `$(warning)` suffix | Numbers are incomplete; run **PokeTokenBar: Show scan diagnostics**. |
| `$(error) Usage` | Helper could not start, or exited five times. Details in the output channel. |

Cost is an estimate of what those tokens would bill at published API rates, matching `ccusage`.
It is not what a subscription charges.

## When nothing appears

The extension's log settles it, rather than guesswork. Open **Output → PokeTokenBar** in the
development host window, or read it from disk — VS Code persists a `LogOutputChannel` under:

```
%APPDATA%/Code/logs/<session>/window<N>/exthost/undefined_publisher.poketokenbar/PokeTokenBar.log
```

Read it in this order:

| Log line | Meaning |
|---|---|
| `status bar shows "..."` | The status bar rendered. If the companion is missing, look for the next two lines. |
| `companion view not open yet` | Expected until you open it: webview views are resolved lazily. Click the status bar item or the activity bar icon. |
| `companion view opened; rendering` | The view is live. A blank panel after this is a rendering fault, not a discovery one. |
| `helper ... ready` but no `status bar shows` | The scan is still running or the refresh threw. |
| `helper executable missing` | The build task did not stage the sidecar; run `npm run build:all`. |
| No log file at all | The extension never activated. Check `exthost.log` in the same folder for `_doActivateExtension ... poketokenbar`. |

## Verifying the trust gate

The one assertion not covered by an automated test is that the helper does not start in an
untrusted workspace. `mayStartHelper(false)` is unit tested, but the end-to-end behaviour needs
a real editor. To check it by hand: F5, then in the new window open a folder you have never
trusted — somewhere outside your usual roots, since a subfolder of a trusted folder inherits
trust. The item should read `Usage: trust required`, and no `PokeTokenBar.Sidecar` process
should exist. Granting trust should start it without a reload.
