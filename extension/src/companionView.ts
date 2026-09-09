import { randomBytes } from 'node:crypto';
import * as vscode from 'vscode';
import { escapeHtml, formatTokens, isSpriteFileName } from './guards';
import { UsageResponse } from './protocol';

/**
 * Sidebar view showing the companion.
 *
 * Scripts are disabled outright rather than sandboxed. A sprite and a progress bar need no
 * JavaScript, and with `enableScripts: false` the path from a crafted transcript to script
 * running next to the extension host does not exist to be mitigated. A strict CSP is set
 * anyway, and every interpolated value is escaped, because relying on a single control is how
 * one wrong refactor becomes an exploit.
 */
export class CompanionViewProvider implements vscode.WebviewViewProvider {
  public static readonly viewType = 'poketokenbar.companion';

  private view?: vscode.WebviewView;
  private latest?: UsageResponse;

  /**
   * @param log Receives lifecycle notes. A webview view is resolved lazily, only once it first
   * becomes visible, so without this there is no way to tell "never opened" from "opened but
   * blank" — two states needing opposite fixes.
   */
  constructor(private readonly log: (message: string) => void = () => {}) {}

  resolveWebviewView(view: vscode.WebviewView): void {
    this.log('companion view opened; rendering');
    this.view = view;
    view.webview.options = { enableScripts: false, localResourceRoots: this.resourceRoots() };
    this.render();
  }

  update(response: UsageResponse): void {
    this.latest = response;
    if (!this.view) {
      this.log('companion view not open yet — click the status bar item to reveal it');
      return;
    }

    if (this.view) {
      // Re-narrowed on every update: the sprite directory is reported by the sidecar, so it is
      // not known until the first response arrives.
      this.view.webview.options = {
        enableScripts: false,
        localResourceRoots: this.resourceRoots(),
      };
      this.render();
    }
  }

  private resourceRoots(): vscode.Uri[] {
    const directory = this.latest?.companion.spriteDirectory;
    return directory ? [vscode.Uri.file(directory)] : [];
  }

  private render(): void {
    if (!this.view) {
      return;
    }

    this.view.webview.html = this.latest
      ? this.page(this.latest, this.view.webview)
      : this.shell('Waiting for the first scan…', '');
  }

  private page(response: UsageResponse, webview: vscode.Webview): string {
    const { companion, today, week, month } = response;

    const sprite = this.spriteTag(companion.spriteFileName, companion.spriteDirectory, webview);
    const percent = Math.round(Math.max(0, Math.min(1, companion.stageProgress)) * 100);
    const stage = `${companion.stageIndex + 1} / ${companion.totalForms}`;

    const body = `
      <div class="pet">${sprite}</div>
      <div class="name">#${companion.speciesId}</div>
      <div class="meta">${escapeHtml(companion.rarity)} · stage ${escapeHtml(stage)}</div>
      <div class="bar"><div class="fill" style="width:${percent}%"></div></div>
      <div class="meta">${escapeHtml(formatTokens(companion.tokensAtStage))} /
        ${escapeHtml(formatTokens(companion.stageThreshold))} · ${percent}%</div>
      ${companion.justGraduated ? '<div class="event">A line completed!</div>' : ''}
      ${companion.justEvolved.length > 0 ? '<div class="event">It evolved!</div>' : ''}
      <table>
        <tr><th>Today</th><td>${escapeHtml(formatTokens(today.total))}</td></tr>
        <tr><th>Week</th><td>${escapeHtml(formatTokens(week.total))}</td></tr>
        <tr><th>Month</th><td>${escapeHtml(formatTokens(month.total))}</td></tr>
      </table>
      ${companion.graduatedCount > 0
        ? `<div class="meta">${companion.graduatedCount} completed</div>`
        : ''}
    `;

    return this.shell(body, webview.cspSource);
  }

  /**
   * Builds the sprite element, or a placeholder. The filename is validated before being joined
   * to the directory, so a value the sidecar should never have sent cannot become a path.
   */
  private spriteTag(
    fileName: string | null | undefined,
    directory: string,
    webview: vscode.Webview,
  ): string {
    if (!isSpriteFileName(fileName) || !directory) {
      return '<div class="placeholder">?</div>';
    }

    const uri = webview.asWebviewUri(vscode.Uri.joinPath(vscode.Uri.file(directory), fileName!));
    return `<img src="${escapeHtml(uri.toString())}" alt="companion" />`;
  }

  private shell(body: string, cspSource: string): string {
    // A nonce for the one inline style block. Scripts are not merely nonce-gated but disabled,
    // and default-src 'none' means anything not listed here cannot load at all.
    const nonce = randomBytes(16).toString('base64');
    const imgSource = cspSource || "'none'";

    return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8" />
  <meta http-equiv="Content-Security-Policy"
        content="default-src 'none'; img-src ${imgSource}; style-src 'nonce-${nonce}';" />
  <style nonce="${nonce}">
    body { font-family: var(--vscode-font-family); color: var(--vscode-foreground);
           text-align: center; padding: 12px 8px; }
    .pet { min-height: 96px; display: flex; align-items: center; justify-content: center; }
    .pet img { image-rendering: pixelated; transform: scale(2); }
    .placeholder { font-size: 32px; opacity: 0.4; }
    .name { font-weight: 600; margin-top: 8px; }
    .meta { font-size: 0.85em; opacity: 0.75; margin-top: 2px; }
    .event { margin-top: 6px; color: var(--vscode-charts-green); font-size: 0.85em; }
    .bar { height: 6px; margin: 8px auto 0; width: 85%; border-radius: 3px;
           background: var(--vscode-progressBar-background, rgba(128,128,128,0.25)); }
    .fill { height: 100%; border-radius: 3px; background: var(--vscode-charts-blue); }
    table { margin: 12px auto 0; font-size: 0.85em; border-collapse: collapse; }
    th { text-align: left; font-weight: 400; opacity: 0.75; padding-right: 10px; }
    td { text-align: right; font-variant-numeric: tabular-nums; }
  </style>
</head>
<body>${body}</body>
</html>`;
  }
}
