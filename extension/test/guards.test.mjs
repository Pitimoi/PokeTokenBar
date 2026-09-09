import assert from 'node:assert/strict';
import { test } from 'node:test';
import { binaryPermissionRefusal, formatTokens, mayStartHelper } from '../out/guards.js';

test('helper is not started in an untrusted workspace', () => {
  assert.equal(mayStartHelper(false), false);
  assert.equal(mayStartHelper(true), true);
});

test('refuses a group-writable binary', () => {
  const refusal = binaryPermissionRefusal(0o775, 'linux');
  assert.ok(refusal, 'expected a refusal');
  assert.match(refusal, /writable by group or others/);
});

test('refuses a world-writable binary', () => {
  assert.ok(binaryPermissionRefusal(0o757, 'darwin'));
  assert.ok(binaryPermissionRefusal(0o777, 'linux'));
});

test('accepts an owner-only-writable binary', () => {
  assert.equal(binaryPermissionRefusal(0o755, 'linux'), undefined);
  assert.equal(binaryPermissionRefusal(0o700, 'darwin'), undefined);
});

test('reports the offending mode so the message is actionable', () => {
  assert.match(binaryPermissionRefusal(0o777, 'linux'), /777/);
});

test('POSIX mode is not consulted on Windows, where ACLs govern', () => {
  // A stat() mode on NTFS routinely reports 0666/0777 and means nothing about who can write.
  assert.equal(binaryPermissionRefusal(0o777, 'win32'), undefined);
});

test('formats token counts compactly', () => {
  assert.equal(formatTokens(0), '0');
  assert.equal(formatTokens(999), '999');
  assert.equal(formatTokens(1_500), '1.5K');
  assert.equal(formatTokens(2_400_000), '2.4M');
  assert.equal(formatTokens(6_250_103_348), '6.3B');
});

test('formats implausible totals as unknown rather than NaN', () => {
  assert.equal(formatTokens(Number.NaN), '—');
  assert.equal(formatTokens(Number.POSITIVE_INFINITY), '—');
  assert.equal(formatTokens(-1), '—');
});
