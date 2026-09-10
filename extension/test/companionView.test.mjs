import assert from 'node:assert/strict';
import Module from 'node:module';
import { test } from 'node:test';

// Stub the vscode module so the view can be rendered outside an extension host. Installed
// before the dynamic import below, which is what routes companionView's require through it.
const realLoad = Module._load;
Module._load = function load(request, parent, isMain) {
  if (request === 'vscode') {
    return {
      Uri: {
        file: (p) => ({ fsPath: p, toString: () => `file://${p.replace(/\\/g, '/')}` }),
        joinPath: (base, ...parts) => ({
          fsPath: `${base.fsPath}/${parts.join('/')}`,
          toString: () => `file://${base.fsPath.replace(/\\/g, '/')}/${parts.join('/')}`,
        }),
      },
    };
  }
  return realLoad(request, parent, isMain);
};

const { CompanionViewProvider } = await import('../out/companionView.js');

const SPRITE_DIR = 'C:\\Users\\someone\\AppData\\Local\\PokeTokenBar\\sprites';

function fakeView() {
  const webview = {
    cspSource: 'vscode-resource://fake',
    options: undefined,
    html: '',
    asWebviewUri: (uri) => ({ toString: () => uri.toString().replace('file://', 'https://cdn/') }),
  };
  return { webview };
}

function response(overrides = {}) {
  const totals = { fromDay: '2026-09-09', toDay: '2026-09-09', total: 1234, cost: 0, models: [] };
  return {
    today: totals,
    week: totals,
    month: totals,
    scan: { degraded: false },
    companion: {
      isEgg: false,
      speciesId: 3,
      stageIndex: 2,
      totalForms: 3,
      stageProgress: 0.768,
      tokensAtStage: 287989214,
      stageThreshold: 375000000,
      rarity: 'Common',
      reachedForms: [1, 2, 3],
      justEvolved: [],
      justGraduated: null,
      justHatched: null,
      graduatedCount: 0,
      graduated: [],
      names: { 3: 'venusaur' },
      collectionSprites: {},
      spriteFileName: '3-a.gif',
      spriteDirectory: SPRITE_DIR,
      ...overrides,
    },
  };
}

function render(overrides) {
  const provider = new CompanionViewProvider();
  const view = fakeView();
  provider.resolveWebviewView(view);
  provider.update(response(overrides));
  return { html: view.webview.html, options: view.webview.options };
}

test('scripts are disabled outright, not merely nonce-gated', () => {
  // With no script execution there is no path from a crafted transcript to code running
  // beside the extension host, so the class is removed rather than mitigated.
  const { options } = render();
  assert.equal(options.enableScripts, false);
});

test('sets a strict content security policy', () => {
  const { html } = render();
  assert.match(html, /Content-Security-Policy/);
  assert.match(html, /default-src 'none'/);
  assert.match(html, /style-src 'nonce-[A-Za-z0-9+/=]+'/);
});

test('the style nonce differs between renders', () => {
  const first = render().html.match(/nonce-([A-Za-z0-9+/=]+)/)[1];
  const second = render().html.match(/nonce-([A-Za-z0-9+/=]+)/)[1];
  assert.notEqual(first, second);
});

test('never grants script-src', () => {
  const { html } = render();
  assert.doesNotMatch(html, /script-src/);
  assert.doesNotMatch(html, /<script/);
});

test('resource roots are narrowed to the sprite directory alone', () => {
  const { options } = render();
  assert.equal(options.localResourceRoots.length, 1);
  assert.equal(options.localResourceRoots[0].fsPath, SPRITE_DIR);
});

test('renders the sprite through asWebviewUri', () => {
  const { html } = render();
  assert.match(html, /<img src="https:\/\/cdn\/.*3-a\.gif"/);
});

test('a filename the sidecar should never send is refused, not joined', () => {
  const { html } = render({ spriteFileName: '../../../../Windows/win.ini' });
  assert.doesNotMatch(html, /<img/);
  assert.doesNotMatch(html, /win\.ini/);
  assert.match(html, /placeholder/);
});

test('a missing sprite degrades to a placeholder', () => {
  const { html } = render({ spriteFileName: null });
  assert.doesNotMatch(html, /<img/);
  assert.match(html, /placeholder/);
});

test('a hostile rarity string renders as text, not markup', () => {
  // Values reaching here originate in logs written by other tools.
  const { html } = render({ rarity: '<img src=x onerror=alert(1)>' });
  assert.doesNotMatch(html, /<img src=x/);
  assert.match(html, /&lt;img src=x onerror=alert\(1\)&gt;/);
});

test('a rarity that tries to break out of the style block cannot', () => {
  const { html } = render({ rarity: '</style><script>fetch("http://evil")</script>' });
  assert.doesNotMatch(html, /<script/);
  assert.doesNotMatch(html, /<\/style><script/);
});

test('progress width is clamped so it cannot overflow the bar', () => {
  const high = render({ stageProgress: 47 }).html;
  const low = render({ stageProgress: -3 }).html;
  assert.match(high, /width:100%/);
  assert.match(low, /width:0%/);
});

test('announces an evolution and a graduation when they happen', () => {
  assert.match(render({ justEvolved: [3] }).html, /It evolved!/);
  assert.match(render({ justGraduated: 3 }).html, /A line completed!/);
  assert.doesNotMatch(render().html, /It evolved!|A line completed!/);
});

test('renders a waiting state before the first response', () => {
  const provider = new CompanionViewProvider();
  const view = fakeView();
  provider.resolveWebviewView(view);

  assert.match(view.webview.html, /Waiting for the first scan/);
  assert.equal(view.webview.options.enableScripts, false);
  // Nothing is known yet, so nothing local may be loaded.
  assert.equal(view.webview.options.localResourceRoots.length, 0);
});

test('shows an empty state before anything is collected', () => {
  const { html } = render({ graduated: [] });
  assert.match(html, /Nothing collected yet/);
  assert.doesNotMatch(html, /class="collection"/);
});

test('renders the collection newest first with names and sprites', () => {
  const { html } = render({
    graduated: [3, 6, 9],
    names: { 3: 'venusaur', 6: 'charizard', 9: 'blastoise' },
    collectionSprites: { 3: '3-s.png', 6: '6-s.png', 9: '9-s.png' },
  });

  assert.match(html, /Collected 3/);
  const order = ['Blastoise', 'Charizard', 'Venusaur'].map((n) => html.indexOf(n));
  assert.ok(order[0] < order[1] && order[1] < order[2], 'newest collected must come first');
  assert.match(html, /9-s\.png/);
});

test('a collected species with no sprite falls back to its dex number', () => {
  const { html } = render({
    graduated: [42],
    names: { 42: 'golbat' },
    collectionSprites: {},
  });

  assert.match(html, /class="dex">#42/);
  assert.match(html, /Golbat/);
});

test('a collected species with no known name shows its dex number', () => {
  const { html } = render({ graduated: [777], names: {}, collectionSprites: {} });
  assert.match(html, /#777/);
});

test('a hostile collection sprite filename is refused, not joined', () => {
  const { html } = render({
    graduated: [3],
    names: { 3: 'venusaur' },
    collectionSprites: { 3: '../../../../Windows/win.ini' },
  });

  assert.doesNotMatch(html, /win\.ini/);
  assert.match(html, /class="dex">#3/);
});

test('a hostile collected name renders as text', () => {
  const { html } = render({
    graduated: [3],
    names: { 3: '<img src=x onerror=alert(1)>' },
    collectionSprites: {},
  });

  assert.doesNotMatch(html, /<img src=x/);
  assert.match(html, /&lt;img src=x/);
});

test('survives a sidecar that omits the collection fields entirely', () => {
  // An installed extension can meet an older sidecar; a missing field must not throw.
  const provider = new CompanionViewProvider();
  const view = fakeView();
  provider.resolveWebviewView(view);

  const stale = response();
  delete stale.companion.graduated;
  delete stale.companion.names;
  delete stale.companion.collectionSprites;

  provider.update(stale);
  assert.match(view.webview.html, /Nothing collected yet/);
});
