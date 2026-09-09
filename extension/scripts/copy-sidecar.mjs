// Development helper: stages the locally built sidecar where the extension expects it.
//
// The extension resolves its executable from its own install directory and never from a
// setting, so a dev build has to be copied in rather than pointed at.
//
// The whole output directory is copied, not just the executable: a framework-dependent build's
// .exe is only an apphost and fails with "The application to execute does not exist" without
// its .dll and runtimeconfig.json beside it. Release builds stage a single NativeAOT binary
// per RID here instead, which is one of the concrete reasons AOT is worth the trouble.
import { access, chmod, cp, mkdir, readdir } from 'node:fs/promises';
import { constants } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const extensionRoot = join(here, '..');
const isWindows = process.platform === 'win32';
const name = isWindows ? 'PokeTokenBar.Sidecar.exe' : 'PokeTokenBar.Sidecar';
const buildRoot = join(extensionRoot, '..', 'dotnet', 'src', 'PokeTokenBar.Sidecar', 'bin');

const candidates = [
  join(buildRoot, 'Release', 'net10.0'),
  join(buildRoot, 'Debug', 'net10.0'),
];

let source;
for (const candidate of candidates) {
  try {
    await access(join(candidate, name), constants.R_OK);
    source = candidate;
    break;
  } catch {
    // Try the next configuration.
  }
}

if (!source) {
  console.error('No built sidecar found. Run "dotnet build -c Release" in ../dotnet first.');
  console.error('Looked in:');
  for (const candidate of candidates) {
    console.error(`  ${join(candidate, name)}`);
  }
  process.exit(1);
}

const target = join(extensionRoot, 'server');
await mkdir(target, { recursive: true });
await cp(source, target, { recursive: true, force: true });

if (!isWindows) {
  // Owner-only write, so the extension's own permission check passes.
  await chmod(join(target, name), 0o755);
}

const staged = await readdir(target);
console.log(`staged ${staged.length} file(s) from ${source}`);
console.log(`     -> ${target}`);
