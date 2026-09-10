import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { Sidecar } from '../out/sidecar.js';
import { ChooseEgg, GetInfo, GetUsage } from '../out/protocol.js';

const executable = join(
  'server',
  process.platform === 'win32' ? 'PokeTokenBar.Sidecar.exe' : 'PokeTokenBar.Sidecar',
);

// These exercise the real binary. Without a staged build there is nothing to assert against,
// and skipping beats a false green.
const staged = existsSync(executable);

describe('sidecar protocol', { skip: staged ? false : 'run "npm run copy-sidecar" first' }, () => {
  /** Speaks raw Content-Length framing so malformed payloads can be sent deliberately. */
  function rawSession() {
    const child = spawn(executable, [], { stdio: ['pipe', 'pipe', 'pipe'] });
    child.stdin.on('error', () => {});
    const state = { responses: [], stderr: '', exited: false };
    child.on('exit', () => {
      state.exited = true;
    });

    let buffer = Buffer.alloc(0);
    child.stdout.on('data', (chunk) => {
      buffer = Buffer.concat([buffer, chunk]);
      for (;;) {
        const separator = buffer.indexOf('\r\n\r\n');
        if (separator < 0) return;
        const header = buffer.subarray(0, separator).toString('ascii');
        const match = /Content-Length: *(\d+)/i.exec(header);
        if (!match) return;
        const length = Number(match[1]);
        if (buffer.length < separator + 4 + length) return;
        state.responses.push(buffer.subarray(separator + 4, separator + 4 + length).toString('utf8'));
        buffer = buffer.subarray(separator + 4 + length);
      }
    });
    child.stderr.on('data', (chunk) => {
      state.stderr += chunk.toString();
    });

    return {
      state,
      send(raw) {
        const body = Buffer.from(raw, 'utf8');
        child.stdin.write(`Content-Length: ${body.length}\r\n\r\n`);
        child.stdin.write(body);
      },
      kill: () => child.kill(),
    };
  }

  const settle = (ms = 900) => new Promise((resolve) => setTimeout(resolve, ms));

  test('rejects an unknown method without executing anything', async () => {
    const session = rawSession();
    after(() => session.kill());
    await settle(250);
    session.send('{"jsonrpc":"2.0","id":1,"method":"NoSuchMethod","params":[]}');
    await settle();

    const [response] = session.state.responses;
    assert.ok(response, 'expected a response');
    assert.match(response, /-32601/);
    assert.equal(session.state.exited, false, 'must stay alive after an unknown method');
  });

  test('does not dispatch to arbitrary types by name', async () => {
    // The contract is a closed set; a reflection-dispatching server could be steered into
    // types it never meant to expose.
    const session = rawSession();
    after(() => session.kill());
    await settle(250);
    session.send(
      '{"jsonrpc":"2.0","id":2,"method":"System.IO.File.Delete","params":["C:/Windows/win.ini"]}',
    );
    await settle();

    assert.match(session.state.responses.join(''), /-32601/);
    assert.equal(session.state.exited, false);
  });

  test('rejects wrong parameter shapes and stays alive', async () => {
    const session = rawSession();
    after(() => session.kill());
    await settle(250);
    session.send('{"jsonrpc":"2.0","id":3,"method":"GetUsageAsync","params":{"path":"C:/secret"}}');
    session.send('{"jsonrpc":"2.0","id":4,"method":"GetInfoAsync","params":["../../etc/passwd",1,2]}');
    await settle();

    const all = session.state.responses.join('');
    assert.match(all, /-32602/);
    assert.equal(session.state.exited, false);
  });

  test('leaks no filesystem paths in error responses', async () => {
    const session = rawSession();
    after(() => session.kill());
    await settle(250);
    session.send('{"jsonrpc":"2.0","id":5,"method":"NoSuchMethod","params":[]}');
    session.send('{"jsonrpc":"2.0","id":6,"method":"GetInfoAsync","params":[1,2,3,4]}');
    await settle();

    const all = session.state.responses.join('\n');
    const paths = all.match(/[A-Za-z]:\\\\[^"\s,}]{6,}|\/(?:Users|home)\/[^"\s,}]{4,}/g) ?? [];
    assert.deepEqual(paths, [], `error responses disclosed paths: ${JSON.stringify(paths)}`);
  });

  test('an unparseable payload is survivable because exit is observable', async () => {
    // The formatter raises an unhandled reader exception on malformed JSON, so the process
    // dies. Nothing reachable sends such input — only this extension writes frames — so the
    // guarantee provided is detection plus restart, not immunity. This pins the detection.
    const session = rawSession();
    after(() => session.kill());
    await settle(250);
    session.send('{"jsonrpc":"2.0","id":7,"method":');
    await settle(1_200);

    assert.equal(session.state.exited, true, 'expected the known crash, so restart is warranted');
    assert.match(session.state.stderr, /JsonReaderException/);
  });

  test('typed client round-trips all three windows', async () => {
    const sidecar = await Sidecar.start(process.cwd(), () => {});
    try {
      const info = await sidecar.connection.sendRequest(GetInfo);
      assert.match(info.version, /^\d+\.\d+/);

      const usage = await sidecar.connection.sendRequest(GetUsage);
      for (const totals of [usage.today, usage.week, usage.month]) {
        assert.match(totals.fromDay, /^\d{4}-\d{2}-\d{2}$/);
        assert.match(totals.toDay, /^\d{4}-\d{2}-\d{2}$/);
        assert.ok(totals.fromDay <= totals.toDay, 'range bounds must be ordered');
        assert.ok(Number.isFinite(totals.total) && totals.total >= 0);
        assert.ok(Number.isFinite(totals.cost) && totals.cost >= 0);
      }

      // Windows nest, so a wider window can never report less.
      assert.ok(usage.week.total >= usage.today.total, 'week must include today');
      assert.ok(usage.month.total >= usage.week.total, 'month must include the week');
    } finally {
      sidecar.dispose();
    }
  });

  // Only refusals are exercised against the live binary. It reads and writes the real save for
  // this machine, so an accepted spend would take tokens out of the user's own game; the
  // accepting paths are covered in dotnet/tests against a temporary file.
  test('an offer index outside the offer is refused, and nothing is spent', async () => {
    const sidecar = await Sidecar.start(process.cwd(), () => {});
    try {
      const before = (await sidecar.connection.sendRequest(GetUsage)).companion;

      for (const index of [-1, 99, 2_147_483_647, -2_147_483_648]) {
        const after = await sidecar.connection.sendRequest(ChooseEgg, index);

        // Which refusal depends on the save this machine happens to hold: a save with a
        // companion active rejects any index before it looks at the index at all. Whichever
        // it is, the index must not select an egg and must not spend.
        assert.match(
          after.refusal,
          /^(NoSuchEgg|AlreadyHasCompanion|NotEnoughBudget)$/,
          `index ${index} must be refused`,
        );
        if (!before.hasCompanion && before.canHatch) {
          assert.equal(after.refusal, 'NoSuchEgg', `index ${index} is not an egg on offer`);
        }

        assert.equal(after.budget, before.budget, `index ${index} must not spend`);
        assert.equal(after.hasCompanion, before.hasCompanion);
        assert.deepEqual(after.pokedex, before.pokedex);
      }
    } finally {
      sidecar.dispose();
    }
  });

  test('a refusal names a reason and never a path', async () => {
    const sidecar = await Sidecar.start(process.cwd(), () => {});
    try {
      const response = await sidecar.connection.sendRequest(ChooseEgg, 99);

      assert.match(response.refusal, /^[A-Za-z]+$/, 'the reason is a closed enum name');
      const paths =
        JSON.stringify(response).match(/[A-Za-z]:\\\\[^"\s,}]{6,}/g)?.filter(
          (found) => !found.includes('sprites'),
        ) ?? [];
      assert.deepEqual(paths, [], `a refusal disclosed paths: ${JSON.stringify(paths)}`);
    } finally {
      sidecar.dispose();
    }
  });

  test('a spend method given the wrong parameter shape is rejected, not obeyed', async () => {
    const session = rawSession();
    after(() => session.kill());
    await settle(250);
    session.send('{"jsonrpc":"2.0","id":8,"method":"ChooseEggAsync","params":["../../etc/passwd"]}');
    session.send('{"jsonrpc":"2.0","id":9,"method":"ChooseEggAsync","params":[{"index":0}]}');
    session.send('{"jsonrpc":"2.0","id":10,"method":"AdvanceCompanionAsync","params":[1,2,3]}');
    await settle(1_500);

    assert.match(session.state.responses.join(''), /-32602/);
    assert.equal(session.state.exited, false);
  });

  test('sanitised model names carry no markup', async () => {
    const sidecar = await Sidecar.start(process.cwd(), () => {});
    try {
      const usage = await sidecar.connection.sendRequest(GetUsage);
      for (const model of usage.month.models) {
        assert.doesNotMatch(model.model, /[<>()'"]/, `model name carried markup: ${model.model}`);
      }
    } finally {
      sidecar.dispose();
    }
  });
});
