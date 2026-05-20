import { MarketStackApiError } from "./errors.js";
import type {
  AccountDetailResponse,
  AccountLimitsResponse,
  AccountUsageEnvelope,
  Alert,
  AnnouncementBatchResponse,
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
  JsonValue,
  LedgerListResponse,
  NewsItem,
  PaginatedResponse,
  StringListResponse,
  SummaryResponse,
  SymbolMetadataResponse,
} from "./types.js";

export const DEFAULT_BASE_URL = "https://developers.manasija.in";

export type JsonBody = Record<string, JsonValue> | JsonValue[] | null;
export type QueryValue = string | number | boolean | null | undefined;
export type QueryParams = Record<string, QueryValue | QueryValue[]>;
export type PathParams = Record<string, string | number>;
export type RequestOptions = Readonly<{
  body?: JsonBody | FormData;
  query?: QueryParams;
  pathParams?: PathParams;
  headers?: Record<string, string>;
}>;

export type MarketStackClientOptions = Readonly<{
  apiKey: string;
  baseUrl?: string;
  headers?: Record<string, string>;
  fetchImpl?: typeof fetch;
}>;

function joinUrl(base: string, path: string): string {
  const b = base.replace(/\/+$/, "");
  const p = path.replace(/^\/+/, "");
  return `${b}/${p}`;
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
    throw new MarketStackApiError(response.status, data);
  }
  return data as TResponse;
}

export class MarketStackClient {
  private readonly baseUrl: string;
  private readonly apiKey: string;
  private readonly extraHeaders: Record<string, string>;
  private readonly fetchImpl: typeof fetch;

  constructor(options: MarketStackClientOptions) {
    this.baseUrl = options.baseUrl ?? DEFAULT_BASE_URL;
    this.apiKey = options.apiKey;
    this.extraHeaders = { ...options.headers };
    this.fetchImpl = options.fetchImpl ?? fetch;
    if (!this.apiKey || this.apiKey.trim().length === 0) {
      throw new Error("MarketStackClient requires a non-empty apiKey");
    }
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
    const response = await this.fetchImpl(url.toString(), {
      method: method.toUpperCase(),
      headers,
      body,
    });
    return parseResponse<TResponse>(response);
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

  getNews(params: { query?: QueryParams } = {}): Promise<PaginatedResponse<NewsItem>> {
    return this.get<PaginatedResponse<NewsItem>>("/v1/news", { query: params.query });
  }

  getSymbolsMetadata(params: { query?: QueryParams } = {}): Promise<SymbolMetadataResponse> {
    return this.get<SymbolMetadataResponse>("/v1/symbols/metadata", { query: params.query });
  }

  getAnnouncementsCategories(params: { query?: QueryParams } = {}): Promise<StringListResponse> {
    return this.get<StringListResponse>("/v1/announcements/categories", { query: params.query });
  }

  getAnnouncements(
    params: { query?: QueryParams } = {}
  ): Promise<PaginatedResponse<AnnouncementListItem | AnnouncementDetail>> {
    return this.get<PaginatedResponse<AnnouncementListItem | AnnouncementDetail>>("/v1/announcements", {
      query: params.query,
    });
  }

  async getAnnouncementsItems(
    params: { query?: QueryParams } = {}
  ): Promise<AnnouncementBatchResponse> {
    const result = await this.get<PaginatedResponse<AnnouncementListItem | AnnouncementDetail>>("/v1/announcements", {
      query: params.query,
    });
    return { data: result.data, missing_ids: result.missing_ids ?? [] };
  }

  getAnnouncementsAttachments(params: { query?: QueryParams } = {}): Promise<BatchAttachmentLookupResponse> {
    return this.get<BatchAttachmentLookupResponse>("/v1/announcements/attachments", { query: params.query });
  }

  postDailySummary(params: { body?: JsonBody; query?: QueryParams } = {}): Promise<SummaryResponse> {
    return this.post<SummaryResponse>("/v1/daily-summary", { query: params.query, body: params.body });
  }

  getEarnings(
    params: { query?: QueryParams } = {}
  ): Promise<PaginatedResponse<EarningsListItem | EarningsDetail>> {
    return this.get<PaginatedResponse<EarningsListItem | EarningsDetail>>("/v1/earnings", { query: params.query });
  }

  getEarningsDetail(params: {
    query: QueryParams & { symbol: string; quarter: string };
  }): Promise<EarningsListItem | EarningsDetail> {
    return this.get<EarningsListItem | EarningsDetail>("/v1/earnings/detail", { query: params.query });
  }

  getEarningsAttachments(params: { query?: QueryParams } = {}): Promise<BatchAttachmentLookupResponse> {
    return this.get<BatchAttachmentLookupResponse>("/v1/earnings/attachments", { query: params.query });
  }

  getConcalls(params: { query?: QueryParams } = {}): Promise<PaginatedResponse<Concall>> {
    return this.get<PaginatedResponse<Concall>>("/v1/concalls", { query: params.query });
  }

  getConcallsDetail(params: { query: QueryParams & { symbol: string; quarter: string } }): Promise<Concall> {
    return this.get<Concall>("/v1/concalls/detail", { query: params.query });
  }

  getConcallsTranscript(params: { query: QueryParams & { symbol: string; quarter: string } }): Promise<ConcallArtifactUrlsResponse> {
    return this.get<ConcallArtifactUrlsResponse>("/v1/concalls/transcript", { query: params.query });
  }

  postConcallsTranscripts(params: {
    body: { items: Array<{ symbol: string; quarter: string }> };
  }): Promise<ConcallTranscriptBatchResponse> {
    return this.post<ConcallTranscriptBatchResponse>("/v1/concalls/transcripts", { body: params.body });
  }

  getAlerts(params: { query?: QueryParams } = {}): Promise<PaginatedResponse<Alert>> {
    return this.get<PaginatedResponse<Alert>>("/v1/alerts", { query: params.query });
  }

  getAccount(params: { query?: QueryParams } = {}): Promise<AccountDetailResponse> {
    return this.get<AccountDetailResponse>("/v1/account", { query: params.query });
  }

  getAccountLimits(params: { query?: QueryParams } = {}): Promise<AccountLimitsResponse> {
    return this.get<AccountLimitsResponse>("/v1/account/limits", { query: params.query });
  }

  getAccountUsage(params: { query?: QueryParams } = {}): Promise<AccountUsageEnvelope> {
    return this.get<AccountUsageEnvelope>("/v1/account/usage", { query: params.query });
  }

  getAccountLedger(params: { query?: QueryParams } = {}): Promise<LedgerListResponse> {
    return this.get<LedgerListResponse>("/v1/account/ledger", { query: params.query });
  }

  postBatchJobs(params: { body?: JsonBody | FormData; query?: QueryParams } = {}): Promise<BatchJobResponse> {
    return this.post<BatchJobResponse>("/v1/batch/jobs", { query: params.query, body: params.body });
  }

  postBatchJobsFile(params: {
    file: Blob;
    filename?: string;
    display_name?: string;
    metadata?: string;
    query?: QueryParams;
  }): Promise<BatchJobResponse> {
    const form = new FormData();
    form.append("file", params.file, params.filename ?? "batch.jsonl");
    if (params.display_name !== undefined) {
      form.append("display_name", params.display_name);
    }
    if (params.metadata !== undefined) {
      form.append("metadata", params.metadata);
    }
    return this.post<BatchJobResponse>("/v1/batch/jobs", { query: params.query, body: form });
  }

  getBatchJobs(params: { query?: QueryParams } = {}): Promise<BatchJobListResponse> {
    return this.get<BatchJobListResponse>("/v1/batch/jobs", { query: params.query });
  }

  getBatchJobsJobId(params: { job_id: string | number; query?: QueryParams }): Promise<BatchJobResponse> {
    return this.get<BatchJobResponse>("/v1/batch/jobs/{job_id}", { pathParams: { job_id: params.job_id }, query: params.query });
  }

  deleteBatchJobsJobId(params: { job_id: string | number; query?: QueryParams }): Promise<BatchJobCancelResponse> {
    return this.delete<BatchJobCancelResponse>("/v1/batch/jobs/{job_id}", { pathParams: { job_id: params.job_id }, query: params.query });
  }

  getBatchJobsJobIdResults(params: { job_id: string | number; query?: QueryParams }): Promise<string> {
    return this.get<string>("/v1/batch/jobs/{job_id}/results", { pathParams: { job_id: params.job_id }, query: params.query });
  }
}
