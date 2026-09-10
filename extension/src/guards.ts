/**
 * Security-relevant decisions, kept free of any `vscode` import so they can be driven from
 * plain node. Verifying the extension must not require launching an editor.
 */

/**
 * Whether the helper process may be started. The helper reads local AI-tool logs and will read
 * credentials, and a folder the user opens can contribute configuration, so trust is required
 * before anything is spawned.
 */
export function mayStartHelper(isTrusted: boolean): boolean {
  return isTrusted;
}

/**
 * Explains why a helper binary must not be executed, or `undefined` when it is acceptable.
 *
 * A group- or world-writable binary can be swapped between the check and the spawn, which would
 * turn an editor-launched process into a persistence hook. Windows governs this with ACLs, where
 * the POSIX mode carries no meaning.
 */
export function binaryPermissionRefusal(
  mode: number,
  platform: NodeJS.Platform,
): string | undefined {
  if (platform === 'win32') {
    return undefined;
  }

  if ((mode & 0o022) !== 0) {
    const octal = (mode & 0o777).toString(8);
    return `it is writable by group or others (mode ${octal})`;
  }

  return undefined;
}

/** How many times the helper may be restarted before giving up. */
export const maxRestartAttempts = 5;

/**
 * Delay before restarting the helper after it exits, in milliseconds.
 *
 * The helper can die on input its formatter cannot parse — an evaluation-only JSON formatter
 * raises unhandled reader exceptions on malformed or deeply nested payloads. Nothing reachable
 * sends such input today, since only this extension writes frames, but a helper that stays dead
 * leaves a stale status bar until the window is reloaded. Backoff bounds the damage if it starts
 * crash-looping for a reason we did not anticipate.
 */
export function restartDelayMs(attempt: number): number {
  const clamped = Math.max(1, Math.min(attempt, maxRestartAttempts));
  return 1_000 * 2 ** (clamped - 1);
}

/**
 * Escapes text for interpolation into webview HTML.
 *
 * Values reaching the webview originate in logs written by other tools, so they are untrusted
 * even after the sidecar sanitises them. This is the second of two independent guards; neither
 * is the only thing standing between a crafted transcript and the extension host.
 */
export function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

/**
 * Whether a sprite filename is one the sidecar's cache could have produced.
 *
 * Mirrors the check on the sidecar side deliberately. The filename is joined to a directory to
 * build a webview resource URI, so accepting anything else would be a path escape — and a
 * guard that exists on only one side of a boundary protects only one side of it.
 */
export function isSpriteFileName(fileName: string | null | undefined): boolean {
  return typeof fileName === 'string' && /^[0-9]{1,4}-(sh)?[as]\.(png|gif)$/.test(fileName);
}

/**
 * Display label for a species: its name in title case, or the dex number when unknown.
 *
 * The name arrives sanitised from the sidecar and is escaped again before rendering; this only
 * shapes it for reading. Hyphens survive because several names genuinely contain them
 * (mr-mime, porygon-z).
 */
export function speciesLabel(id: number, name: string | undefined): string {
  if (!name) {
    return `#${id}`;
  }

  return name
    .split('-')
    .map((part) => (part.length > 0 ? part[0]!.toUpperCase() + part.slice(1) : part))
    .join('-');
}

/**
 * The only commands the companion view may invoke by link.
 *
 * This exact list is handed to `enableCommandUris`, which VS Code matches against the command
 * id of every clicked `command:` link — anything else is dropped before it reaches the command
 * registry. Two consequences worth keeping in mind when editing it: adding an id here widens
 * what a webview can trigger, and emitting a link whose id is *not* here produces a button that
 * silently does nothing.
 */
export const webviewCommands = [
  'poketokenbar.chooseEgg',
  'poketokenbar.feedCompanion',
] as const;

/**
 * Builds a `command:` URI for a webview link.
 *
 * Arguments travel as a JSON array in the query. The result still has to be HTML-escaped at the
 * point of interpolation: this produces a URI, not an attribute value.
 */
export function commandUri(command: string, args: readonly unknown[] = []): string {
  const query = args.length > 0 ? `?${encodeURIComponent(JSON.stringify(args))}` : '';
  return `command:${command}${query}`;
}

/**
 * Validates an offer index arriving as a command argument, or `undefined` to refuse it.
 *
 * A registered command is invokable by anything in the window — the Command Palette, another
 * extension, a task — not only by the link that this view renders. The sidecar range-checks the
 * index against the live offer regardless; this only keeps the host from forwarding a value that
 * is not an index at all.
 */
export function parseOfferIndex(value: unknown, offerCount: number): number | undefined {
  if (typeof value !== 'number' || !Number.isInteger(value)) {
    return undefined;
  }

  return value >= 0 && value < offerCount ? value : undefined;
}

/** Compact token count for a status bar, which has very little room. */
export function formatTokens(total: number): string {
  if (!Number.isFinite(total) || total < 0) {
    return '—';
  }
  if (total >= 1_000_000_000) {
    return `${(total / 1_000_000_000).toFixed(1)}B`;
  }
  if (total >= 1_000_000) {
    return `${(total / 1_000_000).toFixed(1)}M`;
  }
  if (total >= 1_000) {
    return `${(total / 1_000).toFixed(1)}K`;
  }
  return String(total);
}
