import * as vscode from 'vscode';
import {
  formatTokens,
  maxRestartAttempts,
  mayStartHelper,
  restartDelayMs,
  speciesLabel,
} from './guards';
import { GetInfo, GetUsage, ScanReport, UsageResponse, UsageTotals } from './protocol';
import { CompanionViewProvider } from './companionView';
import { Sidecar, SidecarError } from './sidecar';

let sidecar: Sidecar | undefined;
let statusItem: vscode.StatusBarItem;
let output: vscode.LogOutputChannel;
let timer: NodeJS.Timeout | undefined;
let lastScan: ScanReport | undefined;
let restartAttempts = 0;
let restartTimer: NodeJS.Timeout | undefined;
let stopped = false;
let companionView: CompanionViewProvider;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  output = vscode.window.createOutputChannel('PokeTokenBar', { log: true });
  statusItem = vscode.window.createStatusBarItem(
    'poketokenbar.usage',
    vscode.StatusBarAlignment.Right,
    10_000,
  );
  statusItem.name = 'PokeTokenBar usage';
  statusItem.command = 'poketokenbar.showCompanion';
  context.subscriptions.push(output, statusItem);

  companionView = new CompanionViewProvider((message) => output.info(message));
  context.subscriptions.push(
    vscode.window.registerWebviewViewProvider(CompanionViewProvider.viewType, companionView),
    vscode.commands.registerCommand('poketokenbar.showCompanion', showCompanion),
    vscode.commands.registerCommand('poketokenbar.refresh', () => void refresh()),
    vscode.commands.registerCommand('poketokenbar.showDiagnostics', showDiagnostics),
    { dispose: stop },
  );

  // The helper is not started in an untrusted workspace. It reads local AI-tool logs and will
  // later read credentials, and an untrusted folder can contribute configuration, so the
  // conservative order is: trust first, then start.
  if (!mayStartHelper(vscode.workspace.isTrusted)) {
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
    sidecar = await Sidecar.start(
      context.extensionPath,
      (line) => output.warn(`helper: ${line}`),
      (code) => onHelperExit(context, code),
    );
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

  restartAttempts = 0;
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
    const response = await sidecar.connection.sendRequest(GetUsage);
    render(response);
  } catch (error) {
    output.error(`refresh failed: ${String(error)}`);
    statusItem.text = '$(warning) Usage';
    statusItem.tooltip = 'PokeTokenBar could not read usage; see the PokeTokenBar output channel.';
  }
}

function render(response: UsageResponse): void {
  lastScan = response.scan;
  const { today, week, month, scan } = response;

  statusItem.text = `$(graph) ${formatTokens(today.total)}${scan.degraded ? ' $(warning)' : ''}`;

  // Built with MarkdownString rather than string concatenation into HTML: values originate in
  // logs written by other tools. The sidecar already sanitises model names, so this is the
  // second of two independent guards, not the only one.
  const tooltip = new vscode.MarkdownString();
  tooltip.appendMarkdown(`**Today** ${formatTokens(today.total)} · ${formatCost(today.cost)}\n\n`);
  tooltip.appendMarkdown(`Week ${formatTokens(week.total)} · ${formatCost(week.cost)}\n\n`);
  tooltip.appendMarkdown(`Month ${formatTokens(month.total)} · ${formatCost(month.cost)}\n\n`);
  appendModels(tooltip, today);

  if (scan.degraded) {
    tooltip.appendMarkdown(
      `\n_Incomplete: ${scan.filesSkipped} file(s) skipped, ` +
        `${scan.linesTooLong} line(s) too long, ${scan.entriesRejected} entry(ies) rejected._\n`,
    );
  }

  statusItem.tooltip = tooltip;
  companionView.update(response);
  output.info(
    `status bar shows "${statusItem.text}"; companion #${response.companion.speciesId} ` +
      `stage ${response.companion.stageIndex + 1}/${response.companion.totalForms} ` +
      `sprite ${response.companion.spriteFileName ?? "none"}`,
  );
}

function appendModels(tooltip: vscode.MarkdownString, totals: UsageTotals): void {
  const shown = totals.models.filter((model) => model.total > 0).slice(0, 6);
  if (shown.length === 0) {
    return;
  }

  tooltip.appendMarkdown('---\n\n');
  for (const model of shown) {
    const cost = model.cost > 0 ? ` · ${formatCost(model.cost)}` : '';
    tooltip.appendMarkdown(`- \`${model.model}\` — ${formatTokens(model.total)}${cost}\n`);
  }
}

function formatCost(cost: number): string {
  if (!Number.isFinite(cost) || cost <= 0) {
    return '$0.00';
  }
  return cost < 0.01 ? '<$0.01' : `$${cost.toFixed(2)}`;
}

/** Reveals the companion view. VS Code generates the `.focus` command for a contributed view. */
function showCompanion(): void {
  void vscode.commands.executeCommand(`${CompanionViewProvider.viewType}.focus`);
  void refresh();
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

/**
 * Restarts the helper after an unexpected exit, with backoff and a hard attempt cap. The helper
 * is a plain child process: it can be killed, run out of memory, or die on input its formatter
 * cannot parse, and none of those should leave a stale number on screen forever.
 */
function onHelperExit(context: vscode.ExtensionContext, code: number | null): void {
  if (stopped) {
    return;
  }

  sidecar = undefined;
  if (timer) {
    clearInterval(timer);
    timer = undefined;
  }

  restartAttempts += 1;
  if (restartAttempts > maxRestartAttempts) {
    output.error(`helper exited (code ${code ?? 'null'}); giving up after ${maxRestartAttempts} restarts`);
    statusItem.text = '$(error) Usage';
    statusItem.tooltip = 'PokeTokenBar helper keeps exiting; see the PokeTokenBar output channel.';
    return;
  }

  const delay = restartDelayMs(restartAttempts);
  output.warn(`helper exited (code ${code ?? 'null'}); restarting in ${delay} ms (attempt ${restartAttempts})`);
  statusItem.text = '$(sync~spin) Usage';
  restartTimer = setTimeout(() => void start(context), delay);
}

function stop(): void {
  stopped = true;

  if (timer) {
    clearInterval(timer);
    timer = undefined;
  }

  if (restartTimer) {
    clearTimeout(restartTimer);
    restartTimer = undefined;
  }

  sidecar?.dispose();
  sidecar = undefined;
}

