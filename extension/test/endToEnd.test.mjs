import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import Module from 'node:module';
import { join } from 'node:path';
import { describe, test } from 'node:test';

// Stub vscode before the dynamic imports below, so the view renders outside an extension host.
const realLoad = Module._load;
Module._load = function load(request, parent, isMain) {
  if (request === 'vscode') {
    return {
      Uri: {
        file: (p) => ({ fsPath: p, toString: () => `file://${p}` }),
        joinPath: (base, ...rest) => ({
          fsPath: `${base.fsPath}/${rest.join('/')}`,
          toString: () => `file://${base.fsPath}/${rest.join('/')}`,
        }),
      },
    };
  }
  return realLoad(request, parent, isMain);
};

const executable = join(
  'server',
  process.platform === 'win32' ? 'PokeTokenBar.Sidecar.exe' : 'PokeTokenBar.Sidecar',
);
const staged = existsSync(executable);

const { Sidecar } = await import('../out/sidecar.js');
const { GetUsage } = await import('../out/protocol.js');
const { CompanionViewProvider } = await import('../out/companionView.js');

/**
 * The whole path in one assertion: the real sidecar scans real transcripts, banks a budget,
 * caches a sprite, and the real view renders it. Everything in between is genuine; only the
 * editor is stubbed.
 */
describe('end to end', { skip: staged ? false : 'run "npm run copy-sidecar" first' }, () => {
  test('real usage produces a game state that renders safely', async () => {
    const sidecar = await Sidecar.start(process.cwd(), () => {});
    try {
      const usage = await sidecar.connection.sendRequest(GetUsage);
      const companion = usage.companion;

      // Whichever state the save is in, there is always something to do: a companion to feed
      // or eggs to choose from. Neither is a dead end.
      assert.ok(Number.isInteger(companion.offerCount), 'offerCount must be present');
      assert.ok(
        companion.hasCompanion || companion.offerCount > 0,
        'a save with neither a companion nor an offer would be unplayable',
      );
      assert.ok(companion.budget >= 0);
      assert.ok(companion.hatchPrice > 0 && companion.clickCost > 0);
      assert.equal(companion.refusal, '', 'a plain refresh asks for no spend');
      assert.ok(companion.spriteDirectory.length > 0);

      if (companion.hasCompanion) {
        assert.ok(companion.speciesId > 0, 'an active companion must have a species');
        assert.ok(companion.totalForms >= 1);
        assert.ok(companion.stageIndex < companion.totalForms, 'stage must be inside the line');
        assert.ok(companion.stageProgress >= 0 && companion.stageProgress <= 1);
        assert.ok(companion.stageThreshold > 0);
        assert.match(companion.rarity, /^(Common|Uncommon|Rare|Legendary)$/);
      } else {
        assert.equal(companion.speciesId, 0, 'an unchosen egg must not reveal its species');
        assert.equal(companion.spriteFileName, null, 'nor its artwork');
      }

      const webview = {
        cspSource: 'vscode-resource://test',
        options: undefined,
        html: '',
        asWebviewUri: (uri) => ({ toString: () => uri.toString().replace('file://', 'https://cdn/') }),
      };

      const provider = new CompanionViewProvider();
      provider.resolveWebviewView({ webview });
      provider.update(usage);

      assert.equal(webview.options.enableScripts, false);
      assert.equal(webview.options.localResourceRoots.length, 1);
      assert.equal(webview.options.localResourceRoots[0].fsPath, companion.spriteDirectory);
      assert.match(webview.html, /default-src 'none'/);
      assert.doesNotMatch(webview.html, /<script/);

      // Nothing rendered from real log data may carry unescaped markup.
      const body = webview.html.slice(webview.html.indexOf('<body>'));
      assert.doesNotMatch(body, /on[a-z]+=/i, 'no inline event handlers may appear');

      if (companion.spriteFileName) {
        assert.match(webview.html, /<img src="https:\/\/cdn\//);
      }

      // Command links are what make the view clickable with scripts off, so the allowlist has
      // to reach the real webview options — not only the unit-tested render.
      assert.ok(Array.isArray(webview.options.enableCommandUris));
    } finally {
      sidecar.dispose();
    }
  });
});
