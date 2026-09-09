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
