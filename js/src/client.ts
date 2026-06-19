import { DrishtiApiError } from "./errors.js";
import { DrishtiWebSocketSession, type DrishtiWebSocketSessionOptions } from "./websocket.js";
import {
  serializeAnnouncementsQueryParams,
  serializeQueryParams,
  type AccountLedgerQueryParams,
  type AlertsQueryParams,
  type AnnouncementsQueryParams,
  type BatchJobsListQueryParams,
  type ConcallsIndexQueryParams,
  type ConcallsQueryParams,
  type ContentRetentionHeader,
  type UpcomingConcallsQueryParams,
  type UpcomingEarningsQueryParams,
  type DocumentIdsQueryParams,
  type EarningsIndexQueryParams,
  type EarningsQueryParams,
  type NewsQueryParams,
  type SymbolMetadataQueryParams,
  type SymbolQuarterQueryParams,
  type SymbolQuarterTranscriptQueryParams,
  type ConcallTranscriptBatchParams,
  type BatchJobIdParams,
  type DailySummaryParams,
} from "./params.js";
import type {
  AccountDetailResponse,
  AccountLimitsResponse,
  AccountUsageEnvelope,
  Alert,
  AnnouncementDetail,
  AnnouncementListItem,
  EarningsDetail,
  EarningsListItem,
  BatchAttachmentLookupResponse,
  BatchJobCancelResponse,
  BatchJobListResponse,
  BatchJobResponse,
  Concall,
  ConcallArtifactUrlsResponse,
  ConcallTranscriptBatchResponse,
  UpcomingConcall,
  UpcomingEarningsListItem,
  JsonValue,
  LedgerListResponse,
  LightweightIndexItem,
  NewsItem,
  PaginatedResponse,
  AnnouncementCategoriesResponse,
  SummaryResponse,
  SymbolMetadataResponse,
} from "./types.js";

export const DEFAULT_BASE_URL = "https://developers.manasija.in";

export type JsonBody = Record<string, JsonValue> | JsonValue[] | null;
export type QueryValue = string | number | boolean | null | undefined;
/** Serialized query string map used by low-level `request()`. Prefer endpoint param types on client methods. */
export type QueryParams = Record<string, QueryValue | QueryValue[]>;
export type PathParams = Record<string, string | number>;
export type RequestOptions = Readonly<{
  body?: JsonBody | FormData;
  query?: QueryParams;
  pathParams?: PathParams;
  headers?: Record<string, string>;
  retry?: RetryOptions;
}>;

export type RetryOptions = Readonly<{
  maxRetries?: number;
  initialDelayMs?: number;
  maxDelayMs?: number;
  multiplier?: number;
  retryOnStatuses?: number[];
}>;

export type DrishtiClientOptions = Readonly<{
  apiKey: string;
  baseUrl?: string;
  headers?: Record<string, string>;
  fetchImpl?: typeof fetch;
  retry?: RetryOptions;
}>;

export type BatchWaitOptions = Readonly<{
  pollIntervalMs?: number;
  timeoutMs?: number;
  terminalStatuses?: string[];
}>;

function normalizeEarningsItemPayload(item: unknown): unknown {
  if (!item || typeof item !== "object" || Array.isArray(item)) {
    return item;
  }
  const record = item as Record<string, unknown>;
  if (record.earnings_table === undefined && record.earnings_table_extraction !== undefined) {
    record.earnings_table = record.earnings_table_extraction;
  }
  return record;
}

function joinUrl(base: string, path: string): string {
  const b = base.replace(/\/+$/, "");
  const p = path.replace(/^\/+/, "");
  return `${b}/${p}`;
}

function contentRetentionHeaders(
  contentRetention: ContentRetentionHeader | undefined,
): Record<string, string> | undefined {
  if (contentRetention === undefined) {
    return undefined;
  }
  return { "X-Alpha-Content-Retention": contentRetention };
}

async function parseResponse<TResponse extends JsonValue | string | null>(response: Response): Promise<TResponse> {
  if (response.status === 204) {
    return null as TResponse;
  }
  const contentType = response.headers.get("content-type") ?? "";
  let data: JsonValue | string | null;
  if (contentType.includes("application/json")) {
    const text = await response.text();
    data = text.length > 0 ? (JSON.parse(text) as JsonValue) : null;
  } else {
    data = await response.text();
  }
  if (!response.ok) {
    throw new DrishtiApiError(response.status, data);
  }
  return data as TResponse;
}

export class DrishtiClient {
  private readonly baseUrl: string;
  private readonly apiKey: string;
  private readonly extraHeaders: Record<string, string>;
  private readonly fetchImpl?: typeof fetch;
  private readonly retryOptions: Required<RetryOptions>;

  constructor(options: DrishtiClientOptions) {
    this.baseUrl = options.baseUrl ?? DEFAULT_BASE_URL;
    this.apiKey = options.apiKey;
    this.extraHeaders = { ...options.headers };
    this.fetchImpl = options.fetchImpl;
    this.retryOptions = {
      maxRetries: options.retry?.maxRetries ?? 2,
      initialDelayMs: options.retry?.initialDelayMs ?? 300,
      maxDelayMs: options.retry?.maxDelayMs ?? 5000,
      multiplier: options.retry?.multiplier ?? 2,
      retryOnStatuses: options.retry?.retryOnStatuses ?? [408, 409, 425, 429, 500, 502, 503, 504],
    };
    if (!this.apiKey || this.apiKey.trim().length === 0) {
      throw new Error("DrishtiClient requires a non-empty apiKey");
    }
  }

  private resolveRetryOptions(override?: RetryOptions): Required<RetryOptions> {
    return {
      maxRetries: override?.maxRetries ?? this.retryOptions.maxRetries,
      initialDelayMs: override?.initialDelayMs ?? this.retryOptions.initialDelayMs,
      maxDelayMs: override?.maxDelayMs ?? this.retryOptions.maxDelayMs,
      multiplier: override?.multiplier ?? this.retryOptions.multiplier,
      retryOnStatuses: override?.retryOnStatuses ?? this.retryOptions.retryOnStatuses,
    };
  }

  private shouldRetry(error: unknown, response: Response | null, retryOnStatuses: number[]): boolean {
    if (response) {
      return retryOnStatuses.includes(response.status);
    }
    return error instanceof TypeError;
  }

  private computeDelayMs(attempt: number, options: Required<RetryOptions>): number {
    const exp = options.initialDelayMs * Math.pow(options.multiplier, Math.max(0, attempt - 1));
    return Math.min(options.maxDelayMs, exp);
  }

  private mergeHeaders(init?: HeadersInit): Headers {
    const h = new Headers(init);
    for (const [k, v] of Object.entries(this.extraHeaders)) {
      h.set(k, v);
    }
    h.set("X-API-Key", this.apiKey);
    return h;
  }

  private buildPath(pathTemplate: string, pathParams?: PathParams): string {
    if (!pathParams) {
      return pathTemplate;
    }
    let resolved = pathTemplate.replace(/:([a-zA-Z0-9_]+)/g, (_whole: string, key: string): string => {
      const value = pathParams[key];
      if (value === undefined || value === null) {
        throw new Error(`Missing required path param: ${key}`);
      }
      return encodeURIComponent(String(value));
    });
    resolved = resolved.replace(/\{([a-zA-Z0-9_]+)\}/g, (_whole: string, key: string): string => {
      const value = pathParams[key];
      if (value === undefined || value === null) {
        throw new Error(`Missing required path param: ${key}`);
      }
      return encodeURIComponent(String(value));
    });
    return resolved;
  }

  private appendQuery(url: URL, query?: QueryParams): void {
    if (!query) {
      return;
    }
    for (const [key, rawValue] of Object.entries(query)) {
      if (rawValue === undefined || rawValue === null) {
        continue;
      }
      if (Array.isArray(rawValue)) {
        for (const value of rawValue) {
          if (value !== undefined && value !== null) {
            url.searchParams.append(key, String(value));
          }
        }
        continue;
      }
      url.searchParams.set(key, String(rawValue));
    }
  }

  async request<TResponse extends JsonValue | string | null>(method: string, path: string, options: RequestOptions = {}): Promise<TResponse> {
    const resolvedPath = this.buildPath(path, options.pathParams).replace(/^\/+/, "");
    const url = new URL(joinUrl(this.baseUrl, resolvedPath));
    this.appendQuery(url, options.query);
    const headers = this.mergeHeaders({ Accept: "application/json", ...options.headers });
    const isFormData = typeof FormData !== "undefined" && options.body instanceof FormData;
    let body: BodyInit | undefined;
    if (options.body === undefined || options.body === null) {
      body = undefined;
    } else if (isFormData) {
      body = options.body;
    } else {
      body = JSON.stringify(options.body);
      headers.set("Content-Type", "application/json");
    }
    const fetchFn = this.fetchImpl
      ? this.fetchImpl.bind(globalThis)
      : ((input: RequestInfo | URL, init?: RequestInit) => globalThis.fetch(input, init));
    const retryOptions = this.resolveRetryOptions(options.retry);
    const maxAttempts = retryOptions.maxRetries + 1;
    let lastError: unknown = null;
    for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
      let response: Response | null = null;
      try {
        response = await fetchFn(url.toString(), {
          method: method.toUpperCase(),
          headers,
          body,
        });
        if (response.ok || attempt >= maxAttempts || !this.shouldRetry(null, response, retryOptions.retryOnStatuses)) {
          return parseResponse<TResponse>(response);
        }
      } catch (error) {
        lastError = error;
        if (attempt >= maxAttempts || !this.shouldRetry(error, null, retryOptions.retryOnStatuses)) {
          throw error;
        }
      }
      const delayMs = this.computeDelayMs(attempt, retryOptions);
      await new Promise((resolve) => setTimeout(resolve, delayMs));
    }
    throw lastError instanceof Error ? lastError : new Error("Request failed after retries");
  }

  get<TResponse extends JsonValue | string | null>(path: string, options: Omit<RequestOptions, "body"> = {}): Promise<TResponse> {
    return this.request<TResponse>("GET", path, options);
  }

  post<TResponse extends JsonValue | string | null>(path: string, options: RequestOptions = {}): Promise<TResponse> {
    return this.request<TResponse>("POST", path, options);
  }

  put<TResponse extends JsonValue | string | null>(path: string, options: RequestOptions = {}): Promise<TResponse> {
    return this.request<TResponse>("PUT", path, options);
  }

  patch<TResponse extends JsonValue | string | null>(path: string, options: RequestOptions = {}): Promise<TResponse> {
    return this.request<TResponse>("PATCH", path, options);
  }

  delete<TResponse extends JsonValue | string | null>(path: string, options: Omit<RequestOptions, "body"> = {}): Promise<TResponse> {
    return this.request<TResponse>("DELETE", path, options);
  }

  getNews(params: NewsQueryParams = {}): Promise<PaginatedResponse<NewsItem>> {
    return this.get<PaginatedResponse<NewsItem>>("/v1/news", {
      query: serializeQueryParams(params, ["symbols", "scrip_codes"]),
    });
  }

  getSymbolsMetadata(params: SymbolMetadataQueryParams): Promise<SymbolMetadataResponse> {
    return this.get<SymbolMetadataResponse>("/v1/symbols/metadata", {
      query: serializeQueryParams(params, ["symbols", "scrip_codes"]),
    });
  }

  getAnnouncementsCategories(): Promise<AnnouncementCategoriesResponse> {
    return this.get<AnnouncementCategoriesResponse>("/v1/announcements/categories");
  }

  getAnnouncements(
    params: AnnouncementsQueryParams = {}
  ): Promise<PaginatedResponse<AnnouncementListItem | AnnouncementDetail>> {
    return this.get<PaginatedResponse<AnnouncementListItem | AnnouncementDetail>>("/v1/announcements", {
      query: serializeAnnouncementsQueryParams(params),
    });
  }

  getAnnouncementsAttachments(params: DocumentIdsQueryParams): Promise<BatchAttachmentLookupResponse> {
    return this.get<BatchAttachmentLookupResponse>("/v1/announcements/attachments", {
      query: serializeQueryParams(params, ["ids"]),
    });
  }

  postDailySummary(params: DailySummaryParams): Promise<SummaryResponse> {
    return this.post<SummaryResponse>("/v1/daily-summary", {
      body: params.body,
      headers: contentRetentionHeaders(params.contentRetention),
    });
  }

  getEarningsIndex(params: EarningsIndexQueryParams = {}): Promise<PaginatedResponse<LightweightIndexItem>> {
    return this.get<PaginatedResponse<LightweightIndexItem>>("/v1/earnings/index", {
      query: serializeQueryParams(params, ["symbols", "scrip_codes"]),
    });
  }

  getEarnings(
    params: EarningsQueryParams = {}
  ): Promise<PaginatedResponse<EarningsListItem | EarningsDetail>> {
    return this.get<PaginatedResponse<EarningsListItem | EarningsDetail>>("/v1/earnings", {
      query: serializeQueryParams(params, ["symbols", "scrip_codes"]),
    }).then((response) => {
      response.data = response.data.map(
        (item) => normalizeEarningsItemPayload(item) as EarningsListItem | EarningsDetail
      );
      return response;
    });
  }

  getEarningsDetail(params: SymbolQuarterQueryParams): Promise<EarningsListItem | EarningsDetail> {
    return this.get<EarningsListItem | EarningsDetail>("/v1/earnings/detail", {
      query: serializeQueryParams(params),
    }).then(
      (response) => normalizeEarningsItemPayload(response) as EarningsListItem | EarningsDetail
    );
  }

  getEarningsAttachments(params: DocumentIdsQueryParams): Promise<BatchAttachmentLookupResponse> {
    return this.get<BatchAttachmentLookupResponse>("/v1/earnings/attachments", {
      query: serializeQueryParams(params, ["ids"]),
    });
  }

  getUpcomingEarnings(
    params: UpcomingEarningsQueryParams = {},
  ): Promise<PaginatedResponse<UpcomingEarningsListItem>> {
    return this.get<PaginatedResponse<UpcomingEarningsListItem>>("/v1/earnings/upcoming", {
      query: serializeQueryParams(params, ["symbols", "scrip_codes"]),
    });
  }

  getConcalls(params: ConcallsQueryParams = {}): Promise<PaginatedResponse<Concall>> {
    return this.get<PaginatedResponse<Concall>>("/v1/concalls", {
      query: serializeQueryParams(params, ["symbols", "scrip_codes"]),
    });
  }

  getConcallsIndex(params: ConcallsIndexQueryParams = {}): Promise<PaginatedResponse<LightweightIndexItem>> {
    return this.get<PaginatedResponse<LightweightIndexItem>>("/v1/concalls/index", {
      query: serializeQueryParams(params, ["symbols", "scrip_codes"]),
    });
  }

  getUpcomingConcalls(
    params: UpcomingConcallsQueryParams = {},
  ): Promise<PaginatedResponse<UpcomingConcall>> {
    return this.get<PaginatedResponse<UpcomingConcall>>("/v1/concalls/upcoming", {
      query: serializeQueryParams(params, ["symbols", "scrip_codes"]),
    });
  }

  getConcallsDetail(params: SymbolQuarterQueryParams): Promise<Concall> {
    return this.get<Concall>("/v1/concalls/detail", { query: serializeQueryParams(params) });
  }

  getConcallsTranscript(params: SymbolQuarterTranscriptQueryParams): Promise<ConcallArtifactUrlsResponse> {
    return this.get<ConcallArtifactUrlsResponse>("/v1/concalls/transcript", {
      query: serializeQueryParams(params),
    });
  }

  postConcallsTranscripts(params: ConcallTranscriptBatchParams): Promise<ConcallTranscriptBatchResponse> {
    return this.post<ConcallTranscriptBatchResponse>("/v1/concalls/transcripts", { body: { items: params.items } });
  }

  getAlerts(params: AlertsQueryParams = {}): Promise<PaginatedResponse<Alert>> {
    return this.get<PaginatedResponse<Alert>>("/v1/alerts", {
      query: serializeQueryParams(params, ["symbols", "scrip_codes", "type"]),
    });
  }

  getAccount(): Promise<AccountDetailResponse> {
    return this.get<AccountDetailResponse>("/v1/account");
  }

  getAccountLimits(): Promise<AccountLimitsResponse> {
    return this.get<AccountLimitsResponse>("/v1/account/limits");
  }

  getAccountUsage(): Promise<AccountUsageEnvelope> {
    return this.get<AccountUsageEnvelope>("/v1/account/usage");
  }

  getAccountLedger(params: AccountLedgerQueryParams = {}): Promise<LedgerListResponse> {
    return this.get<LedgerListResponse>("/v1/account/ledger", { query: serializeQueryParams(params) });
  }

  postBatchJobs(params: {
    file: Blob;
    filename?: string;
    display_name?: string;
    metadata?: string;
    contentRetention?: ContentRetentionHeader;
  }): Promise<BatchJobResponse> {
    return this.postBatchJobsFile(params);
  }

  postBatchJobsFile(params: {
    file: Blob;
    filename?: string;
    display_name?: string;
    metadata?: string;
    contentRetention?: ContentRetentionHeader;
  }): Promise<BatchJobResponse> {
    const form = new FormData();
    form.append("file", params.file, params.filename ?? "batch.jsonl");
    if (params.display_name !== undefined) {
      form.append("display_name", params.display_name);
    }
    if (params.metadata !== undefined) {
      form.append("metadata", params.metadata);
    }
    return this.post<BatchJobResponse>("/v1/batch/jobs", {
      body: form,
      headers: contentRetentionHeaders(params.contentRetention),
    });
  }

  getBatchJobs(params: BatchJobsListQueryParams = {}): Promise<BatchJobListResponse> {
    return this.get<BatchJobListResponse>("/v1/batch/jobs", { query: serializeQueryParams(params) });
  }

  getBatchJobsJobId(params: BatchJobIdParams): Promise<BatchJobResponse> {
    return this.get<BatchJobResponse>("/v1/batch/jobs/{job_id}", { pathParams: { job_id: params.job_id } });
  }

  deleteBatchJobsJobId(params: BatchJobIdParams): Promise<BatchJobCancelResponse> {
    return this.delete<BatchJobCancelResponse>("/v1/batch/jobs/{job_id}", { pathParams: { job_id: params.job_id } });
  }

  getBatchJobsJobIdResults(params: BatchJobIdParams): Promise<string> {
    return this.get<string>("/v1/batch/jobs/{job_id}/results", { pathParams: { job_id: params.job_id } });
  }

  async waitForBatchJobCompletion(
    params: BatchJobIdParams & BatchWaitOptions
  ): Promise<BatchJobResponse> {
    const pollIntervalMs = params.pollIntervalMs ?? 2000;
    const timeoutMs = params.timeoutMs ?? 5 * 60 * 1000;
    const terminalStatuses = new Set(
      (params.terminalStatuses ?? ["succeeded", "partial", "failed", "cancelled", "completed"]).map((s) => s.toLowerCase())
    );
    const startedAt = Date.now();
    while (true) {
      const job = await this.getBatchJobsJobId({ job_id: params.job_id });
      if (terminalStatuses.has(job.status.toLowerCase())) {
        return job;
      }
      if (Date.now() - startedAt >= timeoutMs) {
        throw new Error(`Timed out waiting for batch job ${params.job_id} to complete`);
      }
      await new Promise((resolve) => setTimeout(resolve, pollIntervalMs));
    }
  }

  async submitBatchJobAndWait(
    params: {
      file: Blob;
      filename?: string;
      display_name?: string;
      metadata?: string;
      contentRetention?: ContentRetentionHeader;
    } & BatchWaitOptions
  ): Promise<BatchJobResponse> {
    const job = await this.postBatchJobsFile({
      file: params.file,
      filename: params.filename,
      display_name: params.display_name,
      metadata: params.metadata,
      contentRetention: params.contentRetention,
    });
    return this.waitForBatchJobCompletion({
      job_id: job.id,
      pollIntervalMs: params.pollIntervalMs,
      timeoutMs: params.timeoutMs,
      terminalStatuses: params.terminalStatuses,
    });
  }

  websocket(
    options: Omit<DrishtiWebSocketSessionOptions, "apiKey" | "baseUrl" | "headers"> = {},
  ): DrishtiWebSocketSession {
    return new DrishtiWebSocketSession({
      apiKey: this.apiKey,
      baseUrl: this.baseUrl,
      headers: this.extraHeaders,
      ...options,
    });
  }
}
