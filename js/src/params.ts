/** Query parameter models aligned with drishti-api /v1 route definitions. */

import type { AlertType, BatchResultLine, SummaryMode, SymbolQuarterKey } from "./types.js";

export type NewsSentiment = "positive" | "negative" | "neutral";

/** ISO date or datetime string (e.g. 2026-05-01 or 2026-05-11T23:59:59Z). */
export type IsoDateTimeParam = string;

/** Fiscal quarter key accepted by detail routes (e.g. q4_26). */
export type FiscalQuarterParam = string;

export type NewsQueryParams = {
  /** Serialized as comma-separated `symbols`. */
  symbols?: string[];
  /** Serialized as comma-separated `scrip_codes`. */
  scrip_codes?: string[];
  sentiment?: NewsSentiment;
  /** Query key: `from`. */
  from?: IsoDateTimeParam;
  to?: IsoDateTimeParam;
  page?: number;
  limit?: number;
};

export type PaginatedFeedQueryParams = {
  symbols?: string[];
  scrip_codes?: string[];
  from?: IsoDateTimeParam;
  to?: IsoDateTimeParam;
  detailed?: boolean;
  page?: number;
  limit?: number;
};

/** GET /v1/earnings/index and GET /v1/concalls/index */
export type IndexQueryParams = {
  symbols?: string[];
  scrip_codes?: string[];
  from?: IsoDateTimeParam;
  to?: IsoDateTimeParam;
  page?: number;
  limit?: number;
};

/** GET /v1/announcements list filter. */
export type AnnouncementsListQueryParams = PaginatedFeedQueryParams & {
  categories?: string[];
  important?: boolean;
};

export type AnnouncementsQueryParams = AnnouncementsListQueryParams;

/** GET /v1/earnings/index */
export type EarningsIndexQueryParams = IndexQueryParams & {
  quarter?: FiscalQuarterParam;
};

/** GET /v1/concalls/index */
export type ConcallsIndexQueryParams = IndexQueryParams & {
  quarter?: FiscalQuarterParam;
};

/** GET /v1/earnings — same filters as announcements except categories. */
export type EarningsQueryParams = PaginatedFeedQueryParams & {
  ids?: string[];
  quarter?: FiscalQuarterParam;
};

export type UpcomingEarningsQueryParams = {
  symbols?: string[];
  scrip_codes?: string[];
  page?: number;
  limit?: number;
};

export type ConcallsQueryParams = PaginatedFeedQueryParams;

export type UpcomingConcallsQueryParams = {
  symbols?: string[];
  scrip_codes?: string[];
  detailed?: boolean;
  page?: number;
  limit?: number;
};

export type AlertsQueryParams = {
  symbols?: string[];
  scrip_codes?: string[];
  /** Alert type filter. Query key: `type`. */
  type?: AlertType[];
  from?: IsoDateTimeParam;
  to?: IsoDateTimeParam;
  important?: boolean;
  page?: number;
  limit?: number;
};

/** GET /v1/announcements/attachments and GET /v1/earnings/attachments */
export type DocumentIdsQueryParams = {
  ids: string[];
};

/** GET /v1/symbols/metadata */
export type SymbolMetadataQueryParams = {
  symbols?: string[];
  scrip_codes?: string[];
};

/** Optional flags for GET /v1/earnings/detail and GET /v1/concalls/detail */
export type SymbolQuarterDetailQueryParams = {
  detailed?: boolean;
};

/** GET /v1/earnings/detail and GET /v1/concalls/detail */
export type SymbolQuarterQueryParams = SymbolQuarterDetailQueryParams & {
  symbol?: string;
  scrip_code?: string;
  quarter: string;
};

/** GET /v1/concalls/transcript */
export type SymbolQuarterTranscriptQueryParams = {
  symbol?: string;
  scrip_code?: string;
  quarter: string;
};

/** Optional `X-Alpha-Content-Retention` header for AI content routes. */
export type ContentRetentionHeader = "none";

export type ConcallTranscriptBatchParams = {
  items: SymbolQuarterKey[];
};

export type BatchJobIdParams = {
  job_id: string | number;
};

export type DailySummaryParams = {
  body: DailySummaryRequestBody;
  contentRetention?: ContentRetentionHeader;
};

/** GET /v1/account/ledger */
export type AccountLedgerQueryParams = {
  limit?: number;
};

/** GET /v1/batch/jobs */
export type BatchJobsListQueryParams = {
  limit?: number;
};

export type DailySummaryPortfolioItem = {
  symbol: string;
  exposure?: number;
  label?: string;
};

export type DailySummaryItem = {
  symbol: string;
  exposure?: number;
  label?: string;
};


export type DailySummaryRequestBody = {
  mode?: SummaryMode;
  portfolio?: DailySummaryPortfolioItem[];
  symbols?: string[];
  items?: DailySummaryItem[];
};

export type BatchSummaryInputLine = DailySummaryRequestBody & {
  custom_id: string;
};

export function buildBatchInputJsonl(lines: BatchSummaryInputLine[]): string {
  if (lines.length === 0) {
    throw new Error("At least one batch input line is required");
  }
  return `${lines.map((line) => JSON.stringify(line)).join("\n")}\n`;
}

export function parseBatchResultJsonl(content: string): BatchResultLine[] {
  const results: BatchResultLine[] = [];
  for (const rawLine of content.split("\n")) {
    if (!rawLine.trim()) {
      continue;
    }
    results.push(JSON.parse(rawLine) as BatchResultLine);
  }
  return results;
}

export type QueryPrimitive = string | number | boolean;

function joinList(values: string[]): string {
  return values.join(",");
}

/**
 * Convert a params object to flat query-string fields for HTTP.
 * Keys listed in `listKeys` are joined with commas (drishti-api list filter convention).
 */
export function serializeQueryParams<T extends object>(
  params: T,
  listKeys: readonly (keyof T & string)[] = []
): Record<string, QueryPrimitive> {
  const listKeySet = new Set<string>(listKeys);
  const out: Record<string, QueryPrimitive> = {};
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null) continue;
    if (listKeySet.has(key) && Array.isArray(value)) {
      if (value.length > 0) out[key] = joinList(value);
      continue;
    }
    if (typeof value === "string" || typeof value === "number" || typeof value === "boolean") {
      out[key] = value;
    }
  }
  return out;
}

/** Announcements list vs by-id modes use different query keys. */
export function serializeAnnouncementsQueryParams(
  params: AnnouncementsQueryParams
): Record<string, QueryPrimitive> {
  return serializeQueryParams(params, ["symbols", "scrip_codes", "categories"]);
}
