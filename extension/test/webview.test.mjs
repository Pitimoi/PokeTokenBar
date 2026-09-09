import assert from 'node:assert/strict';
import { test } from 'node:test';
import { escapeHtml, isSpriteFileName } from '../out/guards.js';

test('escapes every HTML-significant character', () => {
  assert.equal(escapeHtml('<img src=x onerror=alert(1)>'), '&lt;img src=x onerror=alert(1)&gt;');
  assert.equal(escapeHtml('a & b'), 'a &amp; b');
  assert.equal(escapeHtml('say "hi"'), 'say &quot;hi&quot;');
  assert.equal(escapeHtml("it's"), 'it&#39;s');
});

test('escapes ampersands before the entities escaping introduces', () => {
  // The wrong order yields &amp;lt;, which is visible rather than effective.
  assert.equal(escapeHtml('&lt;'), '&amp;lt;');
});

test('leaves ordinary values untouched', () => {
  assert.equal(escapeHtml('claude-opus-5'), 'claude-opus-5');
  assert.equal(escapeHtml('1.2M tokens'), '1.2M tokens');
});

test('a script payload cannot survive escaping into a tag', () => {
  const escaped = escapeHtml('</style><script>fetch("http://evil")</script>');
  assert.doesNotMatch(escaped, /<script/);
  assert.doesNotMatch(escaped, /<\/style/);
});

test('accepts sprite filenames the cache produces', () => {
  for (const name of ['1-s.png', '25-a.gif', '150-shs.png', '649-sha.gif', '1400-s.png']) {
    assert.ok(isSpriteFileName(name), `rejected ${name}`);
  }
});

test('rejects any sprite filename that could escape the cache directory', () => {
  const hostile = [
    '../../../etc/passwd',
    '/etc/passwd',
    '25-s.png/../evil',
    '25-s.exe',
    '25-s.png.exe',
    'abc-s.png',
    '25-x.png',
    '25-s.svg',
    '',
    null,
    undefined,
    '12345-s.png',
  ];

  for (const name of hostile) {
    assert.equal(isSpriteFileName(name), false, `accepted ${JSON.stringify(name)}`);
  }
});

test('rejects windows-style traversal', () => {
  const backslash = ['..', '..', 'windows', 'system32'].join('\\') + '\\cmd.exe';
  assert.equal(isSpriteFileName(backslash), false);
});
