/** Query parameter models aligned with drishti-api /v1 route definitions. */

import type { SymbolQuarterKey } from "./types.js";

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

/** GET /v1/announcements list filter. */
export type AnnouncementsListQueryParams = PaginatedFeedQueryParams & {
  categories?: string[];
};

export type AnnouncementsQueryParams = AnnouncementsListQueryParams;

/** GET /v1/earnings — same filters as announcements except categories. */
export type EarningsQueryParams = PaginatedFeedQueryParams;

export type ConcallsQueryParams = PaginatedFeedQueryParams;

export type AlertsQueryParams = {
  symbols?: string[];
  scrip_codes?: string[];
  /** Alert type filter. Query key: `type`. */
  type?: string[];
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

/** GET /v1/earnings/detail, GET /v1/concalls/detail, GET /v1/concalls/transcript */
export type SymbolQuarterQueryParams = SymbolQuarterDetailQueryParams & {
  symbol?: string;
  scrip_code?: string;
  quarter: string;
};

export type ConcallTranscriptBatchParams = {
  items: SymbolQuarterKey[];
};

export type BatchJobIdParams = {
  job_id: string | number;
};

export type DailySummaryParams = {
  body: DailySummaryRequestBody;
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
  exposure: number;
};

export type DailySummaryRequestBody = {
  portfolio: DailySummaryPortfolioItem[];
};

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
