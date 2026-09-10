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
const { webviewCommands } = await import('../out/guards.js');

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
      budget: 500_000_000,
      earned: 1_200_000_000,
      spent: 700_000_000,
      hatchPrice: 5_000_000,
      clickCost: 25_000_000,
      offerCount: 0,
      hasCompanion: true,
      canHatch: false,
      canAdvance: true,
      refusal: '',
      speciesId: 3,
      speciesName: 'venusaur',
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
      pokedex: [],
      graduated: [],
      names: { 3: 'venusaur' },
      collectionSprites: {},
      spriteFileName: '3-a.gif',
      spriteDirectory: SPRITE_DIR,
      ...overrides,
    },
  };
}

/** An offer state: three eggs, no companion. */
function offer(overrides = {}) {
  return response({
    hasCompanion: false,
    offerCount: 3,
    canHatch: true,
    canAdvance: false,
    spriteFileName: null,
    ...overrides,
  });
}

function render(overrides) {
  const provider = new CompanionViewProvider();
  const view = fakeView();
  provider.resolveWebviewView(view);
  provider.update(response(overrides));
  return { html: view.webview.html, options: view.webview.options };
}

function renderOffer(overrides) {
  const provider = new CompanionViewProvider();
  const view = fakeView();
  provider.resolveWebviewView(view);
  provider.update(offer(overrides));
  return { html: view.webview.html, options: view.webview.options };
}

test('scripts are disabled outright, not merely nonce-gated', () => {
  // With no script execution there is no path from a crafted transcript to code running
  // beside the extension host, so the class is removed rather than mitigated.
  const { options } = render();
  assert.equal(options.enableScripts, false);
});

test('the view is clickable without ever enabling scripts', () => {
  // VS Code delivers webview link clicks from outside the content frame, so command links
  // work although the frame's sandbox omits allow-scripts. If that ever stops being true the
  // buttons go dead, which is why both halves are asserted together.
  const { options, html } = renderOffer();
  assert.equal(options.enableScripts, false);
  assert.match(html, /href="command:poketokenbar\.chooseEgg/);
});

test('command uris are allowlisted rather than enabled wholesale', () => {
  // `true` would let any string that reached an href invoke any command in the window.
  const { options } = render();
  assert.ok(Array.isArray(options.enableCommandUris), 'must be an allowlist, not a boolean');
  assert.deepEqual(options.enableCommandUris, [...webviewCommands]);
});

test('every command link the view emits is in the allowlist', () => {
  // A link whose id is not allowlisted is dropped by VS Code, producing a button that looks
  // fine and silently does nothing.
  for (const html of [render().html, renderOffer().html]) {
    const ids = [...html.matchAll(/href="command:([^"?]+)/g)].map((match) => match[1]);
    for (const id of ids) {
      assert.ok(webviewCommands.includes(id), `${id} is not allowlisted`);
    }
  }
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
  assert.doesNotMatch(html, /win\.ini/);
  assert.match(html, /placeholder/);
});

test('a missing sprite degrades to a placeholder', () => {
  const { html } = render({ spriteFileName: null });
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

test('announces a hatch, an evolution and a graduation when they happen', () => {
  assert.match(render({ justHatched: 1 }).html, /It hatched!/);
  assert.match(render({ justEvolved: [3] }).html, /It evolved!/);
  assert.match(render({ justGraduated: 3 }).html, /A line completed!/);
  assert.doesNotMatch(render().html, /It hatched!|It evolved!|A line completed!/);
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

test('shows what is banked, and the ledger it came from', () => {
  const { html } = render({ budget: 250_000_000, earned: 1_000_000_000, spent: 750_000_000 });
  assert.match(html, /class="budget">250\.0M</);
  assert.match(html, /1\.0B earned/);
  assert.match(html, /750\.0M spent/);
});

test('offers one clickable egg per egg on offer', () => {
  const { html } = renderOffer();
  const links = [...html.matchAll(/href="command:poketokenbar\.chooseEgg\?([^"]*)"/g)];

  assert.equal(links.length, 3);
  // The index travels as a JSON array in the query, which is what VS Code hands the command.
  assert.deepEqual(
    links.map((match) => JSON.parse(decodeURIComponent(match[1]))),
    [[0], [1], [2]],
  );
});

test('an unaffordable egg is not clickable at all', () => {
  // Rendering a live button that can only be refused invites the click and then punishes it.
  const { html } = renderOffer({ canHatch: false, budget: 1_000_000 });
  assert.doesNotMatch(html, /href="command:poketokenbar\.chooseEgg/);
  assert.match(html, /class="pick dim"/);
  assert.match(html, /4\.0M more to afford one/);
});

test('the feed button is live only when the budget covers a press', () => {
  assert.match(render({ canAdvance: true }).html, /href="command:poketokenbar\.feedCompanion"/);

  const poor = render({ canAdvance: false, budget: 1_000 }).html;
  assert.doesNotMatch(poor, /href="command:poketokenbar\.feedCompanion"/);
  assert.match(poor, /class="feed dim"/);
});

test('an offer shows no companion and no eggs are offered beside one', () => {
  assert.doesNotMatch(renderOffer().html, /href="command:poketokenbar\.feedCompanion"/);
  assert.doesNotMatch(render().html, /href="command:poketokenbar\.chooseEgg/);
});

test('explains a refusal from a fixed table, never from the sidecar string', () => {
  // The refusal is a sidecar-supplied string; echoing it would make this the one path where
  // sidecar text becomes display text.
  assert.match(render({ refusal: 'NotEnoughBudget' }).html, /Not enough banked yet/);
  assert.match(render({ refusal: 'NoSuchEgg' }).html, /no longer on offer/);

  const hostile = render({ refusal: '<img src=x onerror=alert(1)>' }).html;
  assert.doesNotMatch(hostile, /<img src=x/);
  assert.doesNotMatch(hostile, /onerror/);
  assert.doesNotMatch(render().html, /class="refusal"/);
});

test('shows an empty state before anything is collected', () => {
  const { html } = render({ pokedex: [] });
  assert.match(html, /Pokédex is empty/);
  assert.doesNotMatch(html, /class="pokedex"/);
});

test('renders the pokedex as a scrolling list in dex order', () => {
  const { html } = render({
    // Deliberately out of order, and not merely reversed, so neither sort direction nor
    // insertion order can pass by accident.
    pokedex: [9, 3, 6],
    graduated: [3],
    names: { 3: 'venusaur', 6: 'charizard', 9: 'blastoise' },
    collectionSprites: { 3: '3-s.png', 6: '6-s.png', 9: '9-s.png' },
  });

  assert.match(html, /Pokédex 3 · completed 1/);
  assert.match(html, /class="pokedex"/);
  assert.match(html, /overflow-y: auto/);

  // Scoped to the list: the active companion's own name appears in the heading above it.
  const list = html.slice(html.indexOf('class="pokedex"'));
  const order = ['Venusaur', 'Charizard', 'Blastoise'].map((n) => list.indexOf(n));
  assert.ok(order.every((at) => at >= 0), 'every owned species must be listed');
  assert.ok(order[0] < order[1] && order[1] < order[2], 'must read #003, #006, #009');
  assert.match(html, /9-s\.png/);
});

test('a pokedex past ten entries still sorts numerically, not as text', () => {
  const { html } = render({ pokedex: [25, 3, 133, 9], names: {}, collectionSprites: {} });

  const list = html.slice(html.indexOf('class="pokedex"'));
  const order = ['#003', '#009', '#025', '#133'].map((dex) => list.indexOf(dex));
  assert.ok(order.every((at) => at >= 0));
  assert.deepEqual([...order].sort((a, b) => a - b), order, 'ids must ascend numerically');
});

test('the pokedex holds every form raised, marking only completed lines', () => {
  const { html } = render({
    pokedex: [1, 2, 3],
    graduated: [3],
    names: { 1: 'bulbasaur', 2: 'ivysaur', 3: 'venusaur' },
    collectionSprites: {},
  });

  assert.equal([...html.matchAll(/class="star"/g)].length, 1);
  for (const dex of ['#001', '#002', '#003']) {
    assert.match(html, new RegExp(dex));
  }
});

test('a collected species with no sprite falls back to its dex number', () => {
  const { html } = render({ pokedex: [42], names: { 42: 'golbat' }, collectionSprites: {} });

  assert.match(html, /#042/);
  assert.match(html, /Golbat/);
});

test('a collected species with no known name shows its dex number', () => {
  const { html } = render({ pokedex: [777], names: {}, collectionSprites: {} });
  assert.match(html, /#777/);
});

test('a hostile pokedex sprite filename is refused, not joined', () => {
  const { html } = render({
    pokedex: [3],
    names: { 3: 'venusaur' },
    collectionSprites: { 3: '../../../../Windows/win.ini' },
  });

  assert.doesNotMatch(html, /win\.ini/);
  assert.match(html, /class="noart"/);
});

test('a hostile collected name renders as text', () => {
  const { html } = render({
    pokedex: [3],
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
  delete stale.companion.pokedex;
  delete stale.companion.graduated;
  delete stale.companion.names;
  delete stale.companion.collectionSprites;

  provider.update(stale);
  assert.match(view.webview.html, /Pokédex is empty/);
});

test('a spend replaces the companion without disturbing the usage totals', () => {
  const provider = new CompanionViewProvider();
  const view = fakeView();
  provider.resolveWebviewView(view);
  provider.update(response({ budget: 500_000_000 }));

  provider.updateCompanion({ ...response().companion, budget: 475_000_000 });

  assert.match(view.webview.html, /class="budget">475\.0M</);
  assert.match(view.webview.html, /<th>Today<\/th><td>1\.2K</);
});
