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

export interface UsageResponse {
  readonly today: UsageTotals;
  readonly week: UsageTotals;
  readonly month: UsageTotals;
  readonly scan: ScanReport;
}

export interface SidecarInfoResponse {
  readonly version: string;
  readonly claudeTranscriptsPresent: boolean;
}

export const GetUsage = new RequestType0<UsageResponse, void>('GetUsageAsync');
export const GetInfo = new RequestType0<SidecarInfoResponse, void>('GetInfoAsync');
