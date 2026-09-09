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
  readonly total: number;
}

export interface DailyUsage {
  readonly localDay: string;
  readonly input: number;
  readonly output: number;
  readonly cacheWrite: number;
  readonly cacheRead: number;
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

export interface TodayUsageResponse {
  readonly usage: DailyUsage;
  readonly scan: ScanReport;
}

export interface SidecarInfoResponse {
  readonly version: string;
  readonly claudeTranscriptsPresent: boolean;
}

export const GetTodayUsage = new RequestType0<TodayUsageResponse, void>('GetTodayUsageAsync');
export const GetInfo = new RequestType0<SidecarInfoResponse, void>('GetInfoAsync');
