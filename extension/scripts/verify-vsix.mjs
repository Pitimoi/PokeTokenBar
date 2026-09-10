// Checks a built VSIX for everything it must contain, then for anything it must not.
//
// The completeness half exists because the first VSIX shipped without its one runtime
// dependency and failed at activation with "Cannot find module 'vscode-jsonrpc/node'". The
// earlier check only looked for leaked development files and for a hand-written list of
// expected paths — a list that omitted the dependency, so it passed. Absence of the wrong
// things is not presence of the right ones.
import { readFileSync } from 'node:fs';

/** Reads entry names and sizes from a zip's central directory. */
function zipEntries(path) {
  const buffer = readFileSync(path);
  let end = buffer.length - 22;
  while (end >= 0 && buffer.readUInt32LE(end) !== 0x06054b50) {
    end--;
  }
  if (end < 0) {
    throw new Error(`${path} is not a zip archive`);
  }

  let offset = buffer.readUInt32LE(end + 16);
  const count = buffer.readUInt16LE(end + 10);
  const entries = [];

  for (let i = 0; i < count; i++) {
    const nameLength = buffer.readUInt16LE(offset + 28);
    const extraLength = buffer.readUInt16LE(offset + 30);
    const commentLength = buffer.readUInt16LE(offset + 32);
    entries.push({
      name: buffer.toString('utf8', offset + 46, offset + 46 + nameLength),
      size: buffer.readUInt32LE(offset + 24),
    });
    offset += 46 + nameLength + extraLength + commentLength;
  }

  return entries;
}

export function verifyVsix(vsixPath, manifest, exeName) {
  const entries = zipEntries(vsixPath);
  const names = new Set(entries.map((e) => e.name));
  const problems = [];

  const required = [
    'extension/package.json',
    `extension/${manifest.main.replace(/^\.\//, '')}`,
    `extension/server/${exeName}`,
    `extension/${manifest.icon}`,
    ...Object.values(manifest.contributes.viewsContainers ?? {})
      .flat()
      .map((container) => `extension/${container.icon}`),
  ];

  for (const path of required) {
    if (!names.has(path)) {
      problems.push(`missing required file: ${path}`);
    }
  }

  // Every runtime dependency must actually be inside the archive. This is the check that was
  // absent when the first VSIX shipped broken.
  for (const dependency of Object.keys(manifest.dependencies ?? {})) {
    const prefix = `extension/node_modules/${dependency}/`;
    if (![...names].some((name) => name.startsWith(prefix))) {
      problems.push(`runtime dependency not bundled: ${dependency}`);
    }
  }

  // Development dependencies must not be, or the archive balloons and ships a toolchain.
  for (const dependency of Object.keys(manifest.devDependencies ?? {})) {
    const prefix = `extension/node_modules/${dependency}/`;
    if ([...names].some((name) => name.startsWith(prefix))) {
      problems.push(`dev dependency leaked: ${dependency}`);
    }
  }

  const leaked = [...names].filter((name) =>
    /^extension\/(src|test|scripts)\//.test(name) || /\.(ts|map|pdb)$/.test(name));
  for (const name of leaked) {
    problems.push(`development file leaked: ${name}`);
  }

  return { entries, problems };
}

if (process.argv[2]) {
  const manifest = JSON.parse(readFileSync('package.json', 'utf8'));
  const exeName = process.platform === 'win32' ? 'PokeTokenBar.Sidecar.exe' : 'PokeTokenBar.Sidecar';
  const { entries, problems } = verifyVsix(process.argv[2], manifest, exeName);

  console.log(`${entries.length} entries`);
  if (problems.length > 0) {
    console.error('\nFAILED:');
    for (const problem of problems) {
      console.error(`  ${problem}`);
    }
    process.exit(1);
  }
  console.log('all required files present, nothing leaked');
}
