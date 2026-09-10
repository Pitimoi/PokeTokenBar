// Builds a VSIX and installs it into the local VS Code.
//
// Run by a person, not by CI: it mutates the editor installation. Once installed the extension
// starts itself — activation is onStartupFinished, so VS Code spawns the sidecar without F5 and
// without a development host — but VS Code has to be reloaded for an existing window to pick up
// a newly installed or replaced extension.
import { execFileSync } from 'node:child_process';
import { readdirSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const extensionRoot = join(here, '..');

execFileSync(process.execPath, [join(here, 'package-vsix.mjs')], {
  cwd: extensionRoot,
  stdio: 'inherit',
  shell: false,
});

const vsix = readdirSync(extensionRoot)
  .filter((f) => f.endsWith('.vsix'))
  .map((f) => ({ f, mtime: statSync(join(extensionRoot, f)).mtimeMs }))
  .sort((a, b) => b.mtime - a.mtime)[0]?.f;

if (!vsix) {
  console.error('No .vsix was produced.');
  process.exit(1);
}

console.log(`\nInstalling ${vsix}\n`);

// shell:true is needed on Windows, where the CLI is code.cmd and Node refuses to spawn a .cmd
// without a shell. It is acceptable *here* and nowhere else in this repository: every argument
// below is a literal or a filename this script just generated, so there is no external input to
// be word-split or expanded. The extension's own process spawning stays shell:false.
try {
  execFileSync('code', ['--install-extension', vsix, '--force'], {
    cwd: extensionRoot,
    stdio: 'inherit',
    shell: process.platform === 'win32',
  });
} catch {
  console.error('\nCould not run the "code" CLI.');
  console.error('In VS Code: Command Palette -> "Shell Command: Install \'code\' command in PATH".');
  console.error(`Or install by hand: code --install-extension ${vsix} --force`);
  console.error('Or: Extensions view -> ... menu -> "Install from VSIX...".');
  process.exit(1);
}

console.log('\nInstalled. Reload VS Code (Developer: Reload Window) for open windows to pick it up.');
console.log('After that it starts on its own — no F5. Click the $(graph) status bar item.');
