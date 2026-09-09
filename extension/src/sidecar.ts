import { ChildProcess, spawn } from 'node:child_process';
import { constants } from 'node:fs';
import { access, stat } from 'node:fs/promises';
import * as path from 'node:path';
import {
  createMessageConnection,
  MessageConnection,
  StreamMessageReader,
  StreamMessageWriter,
} from 'vscode-jsonrpc/node';
import { binaryPermissionRefusal } from './guards';

export class SidecarError extends Error {}

/**
 * Owns the helper process and the JSON-RPC connection to it.
 *
 * The executable is located relative to the extension's own install directory and never from
 * configuration, because a folder the user opens can contribute configuration. It is spawned
 * with an argument array and no shell, so nothing is word-split or expanded.
 */
export class Sidecar {
  private constructor(
    private readonly process: ChildProcess,
    readonly connection: MessageConnection,
  ) {}

  static async start(extensionPath: string, onStderr: (line: string) => void): Promise<Sidecar> {
    const executable = await Sidecar.locate(extensionPath);

    const child = spawn(executable, [], {
      stdio: ['pipe', 'pipe', 'pipe'],
      // No shell, no cwd inherited from a workspace folder, and a minimal environment: the
      // sidecar reads nothing from its environment by design, so passing the editor's own
      // environment through would only widen what could influence it.
      shell: false,
      windowsHide: true,
      env: { PATH: process.env.PATH ?? '' },
    });

    if (!child.stdout || !child.stdin) {
      child.kill();
      throw new SidecarError('helper process started without usable stdio');
    }

    child.stderr?.setEncoding('utf8');
    child.stderr?.on('data', (chunk: string) => {
      for (const line of chunk.split('\n')) {
        if (line.trim().length > 0) {
          onStderr(line.trim());
        }
      }
    });

    const connection = createMessageConnection(
      new StreamMessageReader(child.stdout),
      new StreamMessageWriter(child.stdin),
    );
    connection.listen();

    return new Sidecar(child, connection);
  }

  dispose(): void {
    this.connection.dispose();
    this.process.kill();
  }

  /**
   * Resolves the bundled executable and refuses one that other local users could replace.
   * Without this a writable install directory would turn a signed, editor-launched process
   * into a persistence hook.
   */
  private static async locate(extensionPath: string): Promise<string> {
    const name = process.platform === 'win32' ? 'PokeTokenBar.Sidecar.exe' : 'PokeTokenBar.Sidecar';
    const executable = path.join(extensionPath, 'server', name);

    try {
      await access(executable, constants.X_OK);
    } catch {
      throw new SidecarError(
        `helper executable missing or not executable at ${executable}. ` +
          'For a development build run "npm run copy-sidecar".',
      );
    }

    const info = await stat(executable);
    const refusal = binaryPermissionRefusal(info.mode, process.platform);
    if (refusal) {
      throw new SidecarError(`refusing to run ${executable}: ${refusal}`);
    }

    return executable;
  }
}
