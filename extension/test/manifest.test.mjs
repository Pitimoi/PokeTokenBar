import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

const manifest = JSON.parse(readFileSync('package.json', 'utf8'));
const containers = manifest.contributes.viewsContainers ?? {};

test('view container icons are image files, not codicon references', () => {
  // A $(codicon) here is silently ignored, leaving nothing clickable in the activity bar and
  // no error anywhere to explain why the view cannot be reached.
  for (const [location, list] of Object.entries(containers)) {
    for (const container of list) {
      assert.ok(container.icon, `${location}/${container.id} has no icon`);
      assert.doesNotMatch(
        container.icon,
        /^\$\(/,
        `${location}/${container.id} uses codicon syntax, which does not work here`,
      );
      assert.ok(
        existsSync(join('.', container.icon)),
        `${location}/${container.id} icon missing on disk: ${container.icon}`,
      );
    }
  }
});

test('view container icons are single-colour and theme-aware', () => {
  // A hard-coded fill is ignored by VS Code, so the icon disappears against one of the two
  // theme backgrounds.
  for (const list of Object.values(containers)) {
    for (const container of list) {
      if (!container.icon.endsWith('.svg')) {
        continue;
      }

      const svg = readFileSync(join('.', container.icon), 'utf8');
      assert.match(svg, /currentColor/, `${container.icon} must use currentColor`);
    }
  }
});

test('every contributed view belongs to a declared container', () => {
  const declared = new Set(Object.values(containers).flat().map((c) => c.id));
  const builtIn = new Set(['explorer', 'scm', 'debug', 'test']);

  for (const containerId of Object.keys(manifest.contributes.views ?? {})) {
    assert.ok(
      declared.has(containerId) || builtIn.has(containerId),
      `views."${containerId}" has no matching container`,
    );
  }
});

test('the status bar item points at a contributed command', () => {
  // The item is the only entry point users reliably find, so its command must exist.
  const commands = new Set(manifest.contributes.commands.map((c) => c.command));
  const source = readFileSync('src/extension.ts', 'utf8');
  const assigned = source.match(/statusItem\.command = '([^']+)'/);

  assert.ok(assigned, 'status bar item must have a command');
  assert.ok(commands.has(assigned[1]), `${assigned[1]} is not contributed in package.json`);
});

test('the main entry point exists once compiled', () => {
  assert.ok(existsSync(manifest.main.replace(/^\.\//, '')), `missing ${manifest.main}`);
});

test('workspace trust is declared as limited', () => {
  // The helper reads local AI-tool logs; an untrusted folder must not start it.
  assert.equal(manifest.capabilities.untrustedWorkspaces.supported, 'limited');
});
