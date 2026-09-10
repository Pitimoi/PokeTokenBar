import { RequestType0 } from 'vscode-jsonrpc/node';

/**
 * Mirror of the sidecar's contract. Kept deliberately parameterless: the sidecar resolves its
 * own transcript roots and endpoints, so nothing this side sends can select what it reads.
 * Adding a parameter that names a path, URL, or executable is the change that would breach the
 * trust boundary — see dotnet/README.md, invariant 3.
 */

export interface ModelUsage {
  readonly model: string;
  readonly input: number;
  readonly output: number;
  readonly cacheWrite: number;
  readonly cacheRead: number;
  readonly cost: number;
  readonly total: number;
}

/** Usage over an inclusive range of local days; a single day has equal bounds. */
export interface UsageTotals {
  readonly fromDay: string;
  readonly toDay: string;
  readonly input: number;
  readonly output: number;
  readonly cacheWrite: number;
  readonly cacheRead: number;
  readonly cost: number;
  readonly models: readonly ModelUsage[];
  readonly total: number;
}

export interface ScanReport {
  readonly filesScanned: number;
  readonly filesSkipped: number;
  readonly linesTooLong: number;
  readonly entriesRejected: number;
  readonly duplicatesCollapsed: number;
  readonly elapsedMilliseconds: number;
  readonly degraded: boolean;
}

export interface CompanionInfo {
  readonly speciesId: number;
  /** Sanitised species name, or empty when unknown — fall back to the dex number. */
  readonly speciesName: string;
  readonly stageIndex: number;
  readonly totalForms: number;
  /** Progress through the current form, 0 to 1. */
  readonly stageProgress: number;
  readonly tokensAtStage: number;
  readonly stageThreshold: number;
  readonly rarity: string;
  readonly reachedForms: readonly number[];
  readonly justEvolved: readonly number[];
  readonly justGraduated: number | null;
  readonly graduatedCount: number;
  readonly graduated: readonly number[];
  /** Names for every species mentioned, keyed by dex id as a string over the wire. */
  readonly names: Readonly<Record<string, string>>;
  /** Sprite filenames for the collection, keyed by dex id. Validate before joining. */
  readonly collectionSprites: Readonly<Record<string, string>>;
  /**
   * Cache-relative filename, never a URL and never a path. Validate it with
   * `isSpriteFileName` before joining it to `spriteDirectory` — the sidecar should only ever
   * send a name it generated, and a guard on one side of a boundary protects one side of it.
   */
  readonly spriteFileName: string | null;
  readonly spriteDirectory: string;
}

export interface UsageResponse {
  readonly today: UsageTotals;
  readonly week: UsageTotals;
  readonly month: UsageTotals;
  readonly companion: CompanionInfo;
  readonly scan: ScanReport;
}

export interface SidecarInfoResponse {
  readonly version: string;
  readonly claudeTranscriptsPresent: boolean;
}

export const GetUsage = new RequestType0<UsageResponse, void>('GetUsageAsync');
export const GetInfo = new RequestType0<SidecarInfoResponse, void>('GetInfoAsync');
