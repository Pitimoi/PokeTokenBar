// Builds an installable VSIX for the current platform.
//
// The sidecar is published self-contained and single-file, so the installed extension needs no
// .NET runtime on the target machine — that is what makes the VSIX standalone. NativeAOT would
// produce a far smaller binary and is the intended release path, but it requires a full C++
// toolchain (see "Releasing" in the root README), so this uses the self-contained runtime
// instead: larger, but buildable anywhere the SDK is.
//
// Trimming is deliberately off. StreamJsonRpc's Microsoft.VisualStudio.Threading dependency
// emits trim warnings, and trimming an assembly that warns can remove code it needs at runtime;
// a 75 MB helper that works beats a 30 MB one that might not.
import { execFileSync } from 'node:child_process';
import { chmodSync, copyFileSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { verifyVsix } from './verify-vsix.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const extensionRoot = join(here, '..');

// A VSIX carries one platform's binary, so the dotnet RID and the vsce target must agree.
const targets = {
  'win32-x64': 'win-x64',
  'win32-arm64': 'win-arm64',
  'darwin-x64': 'osx-x64',
  'darwin-arm64': 'osx-arm64',
  'linux-x64': 'linux-x64',
  'linux-arm64': 'linux-arm64',
};

const vsceTarget = `${process.platform}-${process.arch}`;
const rid = targets[vsceTarget];
if (!rid) {
  console.error(`Unsupported platform ${vsceTarget}. Supported: ${Object.keys(targets).join(', ')}`);
  process.exit(1);
}

const run = (command, args, cwd = extensionRoot) => {
  console.log(`> ${command} ${args.join(' ')}`);
  // No shell: arguments stay arguments, and nothing is word-split or expanded.
  execFileSync(command, args, { cwd, stdio: 'inherit', shell: false });
};

// Local tools are run through node rather than npx. Without a shell, execFile cannot resolve
// npx on Windows, where it is npx.cmd; invoking the package's own entry point sidesteps that
// and pins the version to the one installed here.
const runLocal = (binary, args) =>
  run(process.execPath, [join(extensionRoot, 'node_modules', binary), ...args]);

const exeName = process.platform === 'win32' ? 'PokeTokenBar.Sidecar.exe' : 'PokeTokenBar.Sidecar';
const staging = join(extensionRoot, '.sidecar-publish');
const server = join(extensionRoot, 'server');

console.log(`Packaging for ${vsceTarget} (dotnet RID ${rid})\n`);

rmSync(staging, { recursive: true, force: true });
run('dotnet', [
  'publish',
  join('..', 'dotnet', 'src', 'PokeTokenBar.Sidecar'),
  '-c', 'Release',
  '-r', rid,
  '--self-contained', 'true',
  '-p:PublishAot=false',
  '-p:PublishSingleFile=true',
  '-p:DebugType=none',
  '-o', staging,
]);

// Only the executable ships. Symbols would add weight and disclose paths for no user benefit.
rmSync(server, { recursive: true, force: true });
mkdirSync(server, { recursive: true });

const published = readdirSync(staging).filter((f) => !f.endsWith('.pdb'));
if (!published.includes(exeName)) {
  console.error(`Publish produced no ${exeName}. Got: ${published.join(', ')}`);
  process.exit(1);
}

for (const file of published) {
  copyFileSync(join(staging, file), join(server, file));
}

if (process.platform !== 'win32') {
  // Owner-only write, which is also what the extension's own permission check requires.
  chmodSync(join(server, exeName), 0o755);
}

rmSync(staging, { recursive: true, force: true });

const megabytes = (statSync(join(server, exeName)).size / 1024 / 1024).toFixed(1);
console.log(`\nStaged ${published.length} file(s), sidecar ${megabytes} MB\n`);

runLocal(join('typescript', 'bin', 'tsc'), ['-p', './']);
runLocal(join('@vscode', 'vsce', 'vsce'), ['package', '--target', vsceTarget]);

const built = readdirSync(extensionRoot)
  .filter((f) => f.endsWith('.vsix') && f.includes(vsceTarget))
  .map((f) => ({ f, mtime: statSync(join(extensionRoot, f)).mtimeMs }))
  .sort((a, b) => b.mtime - a.mtime)[0]?.f;

if (!built) {
  console.error('vsce produced no .vsix for this target.');
  process.exit(1);
}

// Never hand over an archive that has not been opened and checked. The first VSIX built here
// looked fine and failed at activation, because its one runtime dependency was excluded and
// nothing verified that dependencies were present.
const manifest = JSON.parse(readFileSync(join(extensionRoot, 'package.json'), 'utf8'));
const { entries, problems } = verifyVsix(join(extensionRoot, built), manifest, exeName);
const sizeMb = (statSync(join(extensionRoot, built)).size / 1024 / 1024).toFixed(1);

console.log(`\nBuilt ${built} (${sizeMb} MB, ${entries.length} entries)`);

if (problems.length > 0) {
  console.error('\nThis VSIX is not installable:');
  for (const problem of problems) {
    console.error(`  ${problem}`);
  }
  process.exit(1);
}

console.log('verified: required files present, no dev files or dev dependencies leaked');
console.log('\nInstall with:');
console.log(`  code --install-extension ${built} --force`);
