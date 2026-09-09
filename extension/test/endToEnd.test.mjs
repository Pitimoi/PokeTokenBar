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
 * The whole path in one assertion: the real sidecar scans real transcripts, advances the
 * companion, caches a sprite, and the real view renders it. Everything in between is genuine;
 * only the editor is stubbed.
 */
describe('end to end', { skip: staged ? false : 'run "npm run copy-sidecar" first' }, () => {
  test('real usage produces a companion that renders safely', async () => {
    const sidecar = await Sidecar.start(process.cwd(), () => {});
    try {
      const usage = await sidecar.connection.sendRequest(GetUsage);
      const companion = usage.companion;

      assert.ok(companion.speciesId > 0, 'a companion must exist');
      assert.ok(companion.totalForms >= 1);
      assert.ok(companion.stageIndex < companion.totalForms, 'stage must be inside the line');
      assert.ok(companion.stageProgress >= 0 && companion.stageProgress <= 1);
      assert.ok(companion.stageThreshold > 0);
      assert.match(companion.rarity, /^(Common|Uncommon|Rare|Legendary)$/);
      assert.ok(companion.spriteDirectory.length > 0);

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
    } finally {
      sidecar.dispose();
    }
  });
});
