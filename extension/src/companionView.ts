import { randomBytes } from 'node:crypto';
import * as vscode from 'vscode';
import {
  commandUri,
  escapeHtml,
  formatTokens,
  isSpriteFileName,
  speciesLabel,
  webviewCommands,
} from './guards';
import { CompanionInfo, UsageResponse } from './protocol';

/**
 * Sidebar view showing the companion.
 *
 * Scripts are disabled outright rather than sandboxed, and the view is still clickable. Buttons
 * are `command:` links, which VS Code delivers by listening for clicks from outside the content
 * frame — so they work even though the frame's sandbox omits `allow-scripts`. `enableCommandUris`
 * is given the exact list of ids the view emits, and VS Code drops any clicked link whose id is
 * not in it, which keeps a crafted string in a transcript from reaching the command registry.
 *
 * A strict CSP is set anyway and every interpolated value is escaped, because relying on a
 * single control is how one wrong refactor becomes an exploit.
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
    view.webview.options = this.options();
    this.render();
  }

  update(response: UsageResponse): void {
    this.latest = response;
    if (!this.view) {
      this.log('companion view not open yet — click the status bar item to reveal it');
      return;
    }

    // Re-narrowed on every update: the sprite directory is reported by the sidecar, so it is
    // not known until the first response arrives.
    this.view.webview.options = this.options();
    this.render();
  }

  /** Replaces only the companion, after a spend, leaving the usage totals as they were. */
  updateCompanion(companion: CompanionInfo): void {
    if (this.latest) {
      this.update({ ...this.latest, companion });
    }
  }

  private options(): vscode.WebviewOptions {
    return {
      enableScripts: false,
      enableCommandUris: [...webviewCommands],
      localResourceRoots: this.resourceRoots(),
    };
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

    const body = `
      <div class="budget">${escapeHtml(formatTokens(companion.budget))}</div>
      <div class="meta">banked · spend it below</div>
      ${companion.hasCompanion ? this.companion(companion, webview) : this.offer(companion)}
      ${this.refusal(companion)}
      <table>
        <tr><th>Today</th><td>${escapeHtml(formatTokens(today.total))}</td></tr>
        <tr><th>Week</th><td>${escapeHtml(formatTokens(week.total))}</td></tr>
        <tr><th>Month</th><td>${escapeHtml(formatTokens(month.total))}</td></tr>
      </table>
      ${this.pokedex(companion, webview)}
    `;

    return this.shell(body, webview.cspSource);
  }

  /**
   * The three eggs. Each is a link only while it is affordable — an unaffordable egg renders as
   * plain text, so the button cannot be pressed into a refusal.
   */
  private offer(companion: CompanionInfo): string {
    // Coerced rather than trusted: `undefined <= 0` is false, so a response missing the field
    // would fall through and render an egg tray with no eggs in it.
    const count = Number.isInteger(companion.offerCount) ? companion.offerCount : 0;
    if (count <= 0) {
      return '<div class="meta empty">No eggs on offer</div>';
    }

    const price = formatTokens(companion.hatchPrice);
    const eggs = Array.from({ length: count }, (_, index) => {
      const label = `Take egg ${index + 1} for ${price}`;
      const egg = `<span class="egg">🥚</span><span class="price">${escapeHtml(price)}</span>`;

      return companion.canHatch
        ? `<li><a class="pick" href="${escapeHtml(
            commandUri('poketokenbar.chooseEgg', [index]),
          )}" title="${escapeHtml(label)}">${egg}</a></li>`
        : `<li><span class="pick dim" title="${escapeHtml(label)}">${egg}</span></li>`;
    }).join('');

    return `
      <div class="section">Choose an egg</div>
      <ul class="offer">${eggs}</ul>
      <div class="meta">${
        companion.canHatch
          ? 'It hatches the moment you take it'
          : `${escapeHtml(formatTokens(companion.hatchPrice - companion.budget))} more to afford one`
      }</div>
    `;
  }

  private companion(companion: CompanionInfo, webview: vscode.Webview): string {
    const percent = Math.round(Math.max(0, Math.min(1, companion.stageProgress)) * 100);
    const cost = formatTokens(companion.clickCost);

    const feed = companion.canAdvance
      ? `<a class="feed" href="${escapeHtml(
          commandUri('poketokenbar.feedCompanion'),
        )}">Feed ${escapeHtml(cost)}</a>`
      : `<span class="feed dim">Feed ${escapeHtml(cost)}</span>`;

    return `
      <div class="pet">${this.spriteTag(
        companion.spriteFileName,
        companion.spriteDirectory,
        webview,
      )}</div>
      <div class="name">${escapeHtml(speciesLabel(companion.speciesId, companion.speciesName))}</div>
      <div class="meta">#${companion.speciesId}</div>
      <div class="meta">${escapeHtml(companion.rarity)} · stage
        ${escapeHtml(`${companion.stageIndex + 1} / ${companion.totalForms}`)}</div>
      <div class="bar"><div class="fill" style="width:${percent}%"></div></div>
      <div class="meta">${escapeHtml(formatTokens(companion.tokensAtStage))} /
        ${escapeHtml(formatTokens(companion.stageThreshold))} · ${percent}%</div>
      <div class="actions">${feed}</div>
      ${companion.justHatched ? '<div class="event">It hatched!</div>' : ''}
      ${companion.justGraduated ? '<div class="event">A line completed!</div>' : ''}
      ${companion.justEvolved.length > 0 ? '<div class="event">It evolved!</div>' : ''}
    `;
  }

  /**
   * Explains a refused spend. Rendered from a fixed table rather than the sidecar's string, so
   * nothing the sidecar sends can become display text through this path.
   */
  private refusal(companion: CompanionInfo): string {
    const reasons: Readonly<Record<string, string>> = {
      NotEnoughBudget: 'Not enough banked yet.',
      NoSuchEgg: 'That egg is no longer on offer.',
      AlreadyHasCompanion: 'You already have a companion.',
      NoCompanion: 'Nothing to feed — take an egg first.',
    };

    const reason = reasons[companion.refusal];
    return reason ? `<div class="refusal">${escapeHtml(reason)}</div>` : '';
  }

  /**
   * The Pokédex: every species ever owned, newest first, as a scrolling list. Rendered from
   * filenames the sidecar supplies, each validated before it is joined to a directory.
   */
  private pokedex(companion: CompanionInfo, webview: vscode.Webview): string {
    const owned = companion.pokedex ?? [];
    const names = companion.names ?? {};
    const sprites = companion.collectionSprites ?? {};
    const graduated = new Set(companion.graduated ?? []);

    if (owned.length === 0) {
      return '<div class="meta empty">Your Pokédex is empty</div>';
    }

    const rows = [...owned]
      .reverse()
      .map((id) => {
        const label = speciesLabel(id, names[String(id)]);
        const file = sprites[String(id)];
        const art =
          isSpriteFileName(file) && companion.spriteDirectory
            ? `<img src="${escapeHtml(
                webview
                  .asWebviewUri(
                    vscode.Uri.joinPath(vscode.Uri.file(companion.spriteDirectory), file!),
                  )
                  .toString(),
              )}" alt="" />`
            : '<span class="noart">?</span>';

        // A completed line is worth marking: the Pokédex holds every form raised, so without
        // it there is nothing to distinguish a species carried to its end from one passed
        // through on the way.
        const mark = graduated.has(id) ? '<span class="star" title="Line completed">★</span>' : '';

        return `<li>${art}<span class="dex">#${String(id).padStart(3, '0')}</span>
          <span class="who">${escapeHtml(label)}</span>${mark}</li>`;
      })
      .join('');

    return `
      <div class="section">Pokédex ${owned.length} · completed ${graduated.size}</div>
      <ul class="pokedex">${rows}</ul>
    `;
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
    // and default-src 'none' means anything not listed here cannot load at all. Command links
    // are unaffected: the click is intercepted and never navigates.
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
    a { text-decoration: none; color: inherit; }
    .budget { font-size: 1.6em; font-weight: 600; font-variant-numeric: tabular-nums; }
    .pet { min-height: 96px; display: flex; align-items: center; justify-content: center; }
    .pet img { image-rendering: pixelated; transform: scale(2); }
    .placeholder { font-size: 32px; opacity: 0.4; }
    .name { font-weight: 600; margin-top: 8px; }
    .meta { font-size: 0.85em; opacity: 0.75; margin-top: 2px; }
    .event { margin-top: 6px; color: var(--vscode-charts-green); font-size: 0.85em; }
    .refusal { margin-top: 8px; font-size: 0.85em; color: var(--vscode-errorForeground); }
    .bar { height: 6px; margin: 8px auto 0; width: 85%; border-radius: 3px;
           background: var(--vscode-progressBar-background, rgba(128,128,128,0.25)); }
    .fill { height: 100%; border-radius: 3px; background: var(--vscode-charts-blue); }
    .section { margin-top: 14px; font-size: 0.8em; text-transform: uppercase;
               letter-spacing: 0.05em; opacity: 0.6; }
    .empty { margin-top: 14px; font-style: italic; }
    .actions { margin-top: 10px; }
    .feed { display: inline-block; padding: 5px 14px; border-radius: 4px; font-size: 0.9em;
            background: var(--vscode-button-background);
            color: var(--vscode-button-foreground); }
    .feed.dim { background: var(--vscode-button-secondaryBackground, rgba(128,128,128,0.2));
                color: var(--vscode-button-secondaryForeground, inherit); opacity: 0.5; }
    .offer { list-style: none; padding: 0; margin: 8px 0 0; display: flex;
             justify-content: center; gap: 10px; }
    .pick { display: flex; flex-direction: column; align-items: center; gap: 2px;
            padding: 8px 10px; border-radius: 6px;
            background: var(--vscode-button-secondaryBackground, rgba(128,128,128,0.15)); }
    .pick .egg { font-size: 34px; line-height: 1; }
    .pick .price { font-size: 0.7em; opacity: 0.8; font-variant-numeric: tabular-nums; }
    .pick.dim { opacity: 0.4; }
    .pokedex { list-style: none; padding: 0; margin: 8px 0 0; text-align: left;
               max-height: 260px; overflow-y: auto; }
    .pokedex li { display: flex; align-items: center; gap: 6px; padding: 2px 4px;
                  font-size: 0.85em; border-bottom: 1px solid
                  var(--vscode-editorWidget-border, rgba(128,128,128,0.18)); }
    .pokedex img { width: 32px; height: 32px; object-fit: contain;
                   image-rendering: pixelated; flex: none; }
    .noart { width: 32px; text-align: center; opacity: 0.4; flex: none; }
    .dex { opacity: 0.5; font-variant-numeric: tabular-nums; flex: none; }
    .who { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; flex: 1; }
    .star { color: var(--vscode-charts-yellow); flex: none; }
    table { margin: 12px auto 0; font-size: 0.85em; border-collapse: collapse; }
    th { text-align: left; font-weight: 400; opacity: 0.75; padding-right: 10px; }
    td { text-align: right; font-variant-numeric: tabular-nums; }
  </style>
</head>
<body>${body}</body>
</html>`;
  }
}
