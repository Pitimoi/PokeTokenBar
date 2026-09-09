import * as vscode from 'vscode';
import { GetInfo, GetTodayUsage, ScanReport, TodayUsageResponse } from './protocol';
import { Sidecar, SidecarError } from './sidecar';

let sidecar: Sidecar | undefined;
let statusItem: vscode.StatusBarItem;
let output: vscode.LogOutputChannel;
let timer: NodeJS.Timeout | undefined;
let lastScan: ScanReport | undefined;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  output = vscode.window.createOutputChannel('PokeTokenBar', { log: true });
  statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 100);
  statusItem.command = 'poketokenbar.refresh';
  context.subscriptions.push(output, statusItem);

  context.subscriptions.push(
    vscode.commands.registerCommand('poketokenbar.refresh', () => void refresh()),
    vscode.commands.registerCommand('poketokenbar.showDiagnostics', showDiagnostics),
    { dispose: stop },
  );

  // The helper is not started in an untrusted workspace. It reads local AI-tool logs and will
  // later read credentials, and an untrusted folder can contribute configuration, so the
  // conservative order is: trust first, then start.
  if (!vscode.workspace.isTrusted) {
    statusItem.text = '$(shield) Usage: trust required';
    statusItem.tooltip = 'PokeTokenBar does not run in an untrusted workspace.';
    statusItem.show();
    context.subscriptions.push(
      vscode.workspace.onDidGrantWorkspaceTrust(() => void start(context)),
    );
    return;
  }

  await start(context);
}

export function deactivate(): void {
  stop();
}

async function start(context: vscode.ExtensionContext): Promise<void> {
  statusItem.text = '$(sync~spin) Usage';
  statusItem.show();

  try {
    sidecar = await Sidecar.start(context.extensionPath, (line) => output.warn(`helper: ${line}`));
  } catch (error) {
    const message = error instanceof SidecarError ? error.message : String(error);
    output.error(message);
    statusItem.text = '$(error) Usage';
    statusItem.tooltip = `PokeTokenBar could not start its helper: ${message}`;
    return;
  }

  try {
    const info = await sidecar.connection.sendRequest(GetInfo);
    output.info(`helper ${info.version} ready; claude transcripts present: ${info.claudeTranscriptsPresent}`);
    if (!info.claudeTranscriptsPresent) {
      statusItem.text = '$(circle-slash) Usage';
      statusItem.tooltip = 'No Claude Code transcripts found on this machine.';
      return;
    }
  } catch (error) {
    output.error(`handshake failed: ${String(error)}`);
  }

  await refresh();
  scheduleRefresh();
}

function scheduleRefresh(): void {
  if (timer) {
    clearInterval(timer);
  }

  const seconds = vscode.workspace
    .getConfiguration('poketokenbar')
    .get<number>('refreshIntervalSeconds', 300);

  timer = setInterval(() => void refresh(), Math.max(30, seconds) * 1000);
}

async function refresh(): Promise<void> {
  if (!sidecar) {
    return;
  }

  try {
    const response = await sidecar.connection.sendRequest(GetTodayUsage);
    render(response);
  } catch (error) {
    output.error(`refresh failed: ${String(error)}`);
    statusItem.text = '$(warning) Usage';
    statusItem.tooltip = 'PokeTokenBar could not read usage; see the PokeTokenBar output channel.';
  }
}

function render(response: TodayUsageResponse): void {
  lastScan = response.scan;
  const { usage, scan } = response;

  statusItem.text = `$(graph) ${formatTokens(usage.total)}${scan.degraded ? ' $(warning)' : ''}`;

  // Built with MarkdownString rather than string concatenation into HTML: values originate in
  // logs written by other tools. The sidecar already sanitises model names, so this is the
  // second of two independent guards, not the only one.
  const tooltip = new vscode.MarkdownString();
  tooltip.appendMarkdown(`**Usage for ${usage.localDay}**\n\n`);
  tooltip.appendMarkdown(`Total **${formatTokens(usage.total)}** tokens\n\n`);
  for (const model of usage.models.slice(0, 6)) {
    if (model.total > 0) {
      tooltip.appendMarkdown(`- \`${model.model}\` — ${formatTokens(model.total)}\n`);
    }
  }

  if (scan.degraded) {
    tooltip.appendMarkdown(
      `\n_Incomplete: ${scan.filesSkipped} file(s) skipped, ` +
        `${scan.linesTooLong} line(s) too long, ${scan.entriesRejected} entry(ies) rejected._\n`,
    );
  }

  statusItem.tooltip = tooltip;
}

function showDiagnostics(): void {
  if (!lastScan) {
    output.info('No scan has completed yet.');
  } else {
    output.info(
      `files scanned ${lastScan.filesScanned}, skipped ${lastScan.filesSkipped}, ` +
        `lines too long ${lastScan.linesTooLong}, entries rejected ${lastScan.entriesRejected}, ` +
        `duplicates collapsed ${lastScan.duplicatesCollapsed}, ` +
        `elapsed ${lastScan.elapsedMilliseconds} ms`,
    );
  }

  output.show(true);
}

function stop(): void {
  if (timer) {
    clearInterval(timer);
    timer = undefined;
  }

  sidecar?.dispose();
  sidecar = undefined;
}

function formatTokens(total: number): string {
  if (total >= 1_000_000_000) {
    return `${(total / 1_000_000_000).toFixed(1)}B`;
  }
  if (total >= 1_000_000) {
    return `${(total / 1_000_000).toFixed(1)}M`;
  }
  if (total >= 1_000) {
    return `${(total / 1_000).toFixed(1)}K`;
  }
  return String(total);
}
