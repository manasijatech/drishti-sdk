import { MarketStackApiError } from "./errors.js";
import type {
  AccountDetailResponse,
  AccountLimitsResponse,
  AccountListResponse,
  AccountUsageEnvelope,
  AdminAccountDashboardResponse,
  AdminApiKeyDetailResponse,
  Alert,
  AnnouncementDetail,
  ApiKeyAdminListResponse,
  ApiKeyAdminPayload,
  ApiKeyCreateResponse,
  ApiKeyDeleteResponse,
  ApiKeyGetResponse,
  ApiKeyListResponse,
  BatchJobCancelResponse,
  BatchJobListResponse,
  BatchJobResponse,
  CacheClearResponse,
  Concall,
  JsonValue,
  LedgerEntry,
  LedgerListResponse,
  MarketReport,
  MigrateResponse,
  PaginatedResponse,
  PresignedUrlResponse,
  SummaryResponse,
  UsageHistoryEnvelope,
  UsageResponse,
} from "./types.js";

const runtimeEnv = globalThis as { process?: { env?: Record<string, string | undefined> } };
export const DEFAULT_BASE_URL = runtimeEnv.process?.env?.ALPHA_API_BASE_URL ?? "http://127.0.0.1:8000";

export type JsonBody = Record<string, JsonValue> | JsonValue[] | null;
export type QueryValue = string | number | boolean | null | undefined;
export type QueryParams = Record<string, QueryValue | QueryValue[]>;
export type PathParams = Record<string, string | number>;
export type RequestOptions = Readonly<{
  body?: JsonBody;
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
    const body = options.body === undefined || options.body === null ? undefined : JSON.stringify(options.body);
    if (body !== undefined) {
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

  getAnnouncements(params: { query?: QueryParams } = {}): Promise<PaginatedResponse<AnnouncementDetail>> {
    return this.get<PaginatedResponse<AnnouncementDetail>>("/v1/announcements", { query: params.query });
  }

  getAnnouncementsAnnouncementId(params: { announcement_id: string | number; query?: QueryParams }): Promise<AnnouncementDetail> {
    return this.get<AnnouncementDetail>("/v1/announcements/{announcement_id}", { pathParams: { announcement_id: params.announcement_id }, query: params.query });
  }

  getAnnouncementsAnnouncementIdAttachment(params: { announcement_id: string | number; query?: QueryParams }): Promise<PresignedUrlResponse> {
    return this.get<PresignedUrlResponse>("/v1/announcements/{announcement_id}/attachment", { pathParams: { announcement_id: params.announcement_id }, query: params.query });
  }

  postDailySummary(params: { body?: JsonBody; query?: QueryParams } = {}): Promise<SummaryResponse> {
    return this.post<SummaryResponse>("/v1/daily-summary", { query: params.query, body: params.body });
  }

  getEarnings(params: { query?: QueryParams } = {}): Promise<PaginatedResponse<AnnouncementDetail>> {
    return this.get<PaginatedResponse<AnnouncementDetail>>("/v1/earnings", { query: params.query });
  }

  getEarningsEarningsId(params: { earnings_id: string | number; query?: QueryParams }): Promise<AnnouncementDetail> {
    return this.get<AnnouncementDetail>("/v1/earnings/{earnings_id}", { pathParams: { earnings_id: params.earnings_id }, query: params.query });
  }

  getEarningsEarningsIdAttachment(params: { earnings_id: string | number; query?: QueryParams }): Promise<PresignedUrlResponse> {
    return this.get<PresignedUrlResponse>("/v1/earnings/{earnings_id}/attachment", { pathParams: { earnings_id: params.earnings_id }, query: params.query });
  }

  getConcalls(params: { query?: QueryParams } = {}): Promise<PaginatedResponse<Concall>> {
    return this.get<PaginatedResponse<Concall>>("/v1/concalls", { query: params.query });
  }

  getConcallsConcallId(params: { concall_id: string | number; query?: QueryParams }): Promise<Concall> {
    return this.get<Concall>("/v1/concalls/{concall_id}", { pathParams: { concall_id: params.concall_id }, query: params.query });
  }

  getConcallsConcallIdTranscript(params: { concall_id: string | number; query?: QueryParams }): Promise<PresignedUrlResponse> {
    return this.get<PresignedUrlResponse>("/v1/concalls/{concall_id}/transcript", { pathParams: { concall_id: params.concall_id }, query: params.query });
  }

  getAlerts(params: { query?: QueryParams } = {}): Promise<PaginatedResponse<Alert>> {
    return this.get<PaginatedResponse<Alert>>("/v1/alerts", { query: params.query });
  }

  getAlertsAlertId(params: { alert_id: string | number; query?: QueryParams }): Promise<Alert> {
    return this.get<Alert>("/v1/alerts/{alert_id}", { pathParams: { alert_id: params.alert_id }, query: params.query });
  }

  getReports(params: { query?: QueryParams } = {}): Promise<PaginatedResponse<MarketReport>> {
    return this.get<PaginatedResponse<MarketReport>>("/v1/reports", { query: params.query });
  }

  getReportsReportId(params: { report_id: string | number; query?: QueryParams }): Promise<MarketReport> {
    return this.get<MarketReport>("/v1/reports/{report_id}", { pathParams: { report_id: params.report_id }, query: params.query });
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

  postAdminAccounts(params: { body?: JsonBody; query?: QueryParams } = {}): Promise<AccountDetailResponse> {
    return this.post<AccountDetailResponse>("/v1/admin/accounts", { query: params.query, body: params.body });
  }

  getAdminAccounts(params: { query?: QueryParams } = {}): Promise<AccountListResponse> {
    return this.get<AccountListResponse>("/v1/admin/accounts", { query: params.query });
  }

  getAdminAccountsAccountId(params: { account_id: string | number; query?: QueryParams }): Promise<AccountDetailResponse> {
    return this.get<AccountDetailResponse>("/v1/admin/accounts/{account_id}", { pathParams: { account_id: params.account_id }, query: params.query });
  }

  patchAdminAccountsAccountId(params: { account_id: string | number; body?: JsonBody; query?: QueryParams }): Promise<AccountDetailResponse> {
    return this.patch<AccountDetailResponse>("/v1/admin/accounts/{account_id}", { pathParams: { account_id: params.account_id }, query: params.query, body: params.body });
  }

  postAdminAccountsAccountIdCredits(params: { account_id: string | number; body?: JsonBody; query?: QueryParams }): Promise<{ data: LedgerEntry }> {
    return this.post<{ data: LedgerEntry }>("/v1/admin/accounts/{account_id}/credits", { pathParams: { account_id: params.account_id }, query: params.query, body: params.body });
  }

  getAdminAccountsAccountIdLedger(params: { account_id: string | number; query?: QueryParams }): Promise<LedgerListResponse> {
    return this.get<LedgerListResponse>("/v1/admin/accounts/{account_id}/ledger", { pathParams: { account_id: params.account_id }, query: params.query });
  }

  postAdminAccountsAccountIdApiKeys(params: { account_id: string | number; body?: JsonBody; query?: QueryParams }): Promise<{ data: ApiKeyAdminPayload }> {
    return this.post<{ data: ApiKeyAdminPayload }>("/v1/admin/accounts/{account_id}/api-keys", { pathParams: { account_id: params.account_id }, query: params.query, body: params.body });
  }

  patchAdminAccountsAccountIdApiKeysApiKey(params: { account_id: string | number; api_key: string | number; body?: JsonBody; query?: QueryParams }): Promise<{ data: ApiKeyAdminPayload }> {
    return this.patch<{ data: ApiKeyAdminPayload }>("/v1/admin/accounts/{account_id}/api-keys/{api_key}", { pathParams: { account_id: params.account_id, api_key: params.api_key }, query: params.query, body: params.body });
  }

  getAdminAccountsAccountIdApiKeys(params: { account_id: string | number; query?: QueryParams }): Promise<ApiKeyAdminListResponse> {
    return this.get<ApiKeyAdminListResponse>("/v1/admin/accounts/{account_id}/api-keys", { pathParams: { account_id: params.account_id }, query: params.query });
  }

  getAdminAccountsAccountIdDashboard(params: { account_id: string | number; query?: QueryParams }): Promise<AdminAccountDashboardResponse> {
    return this.get<AdminAccountDashboardResponse>("/v1/admin/accounts/{account_id}/dashboard", { pathParams: { account_id: params.account_id }, query: params.query });
  }

  getAdminAccountsAccountIdUsage(params: { account_id: string | number; query?: QueryParams }): Promise<UsageHistoryEnvelope> {
    return this.get<UsageHistoryEnvelope>("/v1/admin/accounts/{account_id}/usage", { pathParams: { account_id: params.account_id }, query: params.query });
  }

  getAdminAccountsAccountIdApiKeysApiKey(params: { account_id: string | number; api_key: string | number; query?: QueryParams }): Promise<AdminApiKeyDetailResponse> {
    return this.get<AdminApiKeyDetailResponse>("/v1/admin/accounts/{account_id}/api-keys/{api_key}", { pathParams: { account_id: params.account_id, api_key: params.api_key }, query: params.query });
  }

  getAdminAccountsAccountIdApiKeysApiKeyUsage(params: { account_id: string | number; api_key: string | number; query?: QueryParams }): Promise<UsageHistoryEnvelope> {
    return this.get<UsageHistoryEnvelope>("/v1/admin/accounts/{account_id}/api-keys/{api_key}/usage", { pathParams: { account_id: params.account_id, api_key: params.api_key }, query: params.query });
  }

  postApiKeys(params: { body?: JsonBody; query?: QueryParams } = {}): Promise<ApiKeyCreateResponse> {
    return this.post<ApiKeyCreateResponse>("/v1/api-keys", { query: params.query, body: params.body });
  }

  getApiKeysApiKey(params: { api_key: string | number; query?: QueryParams }): Promise<ApiKeyGetResponse> {
    return this.get<ApiKeyGetResponse>("/v1/api-keys/{api_key}", { pathParams: { api_key: params.api_key }, query: params.query });
  }

  patchApiKeysApiKey(params: { api_key: string | number; body?: JsonBody; query?: QueryParams }): Promise<ApiKeyGetResponse> {
    return this.patch<ApiKeyGetResponse>("/v1/api-keys/{api_key}", { pathParams: { api_key: params.api_key }, query: params.query, body: params.body });
  }

  deleteApiKeysApiKey(params: { api_key: string | number; query?: QueryParams }): Promise<ApiKeyDeleteResponse> {
    return this.delete<ApiKeyDeleteResponse>("/v1/api-keys/{api_key}", { pathParams: { api_key: params.api_key }, query: params.query });
  }

  getApiKeys(params: { query?: QueryParams } = {}): Promise<ApiKeyListResponse> {
    return this.get<ApiKeyListResponse>("/v1/api-keys", { query: params.query });
  }

  postApiKeysMigrate(params: { body?: JsonBody; query?: QueryParams } = {}): Promise<MigrateResponse> {
    return this.post<MigrateResponse>("/v1/api-keys/migrate", { query: params.query, body: params.body });
  }

  deleteApiKeysCache(params: { query?: QueryParams } = {}): Promise<CacheClearResponse> {
    return this.delete<CacheClearResponse>("/v1/api-keys/cache", { query: params.query });
  }

  getApiKeysApiKeyUsage(params: { api_key: string | number; query?: QueryParams }): Promise<UsageResponse> {
    return this.get<UsageResponse>("/v1/api-keys/{api_key}/usage", { pathParams: { api_key: params.api_key }, query: params.query });
  }

  postBatchJobs(params: { body?: JsonBody; query?: QueryParams } = {}): Promise<BatchJobResponse> {
    return this.post<BatchJobResponse>("/v1/batch/jobs", { query: params.query, body: params.body });
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
