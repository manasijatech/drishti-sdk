export type JsonPrimitive = string | number | boolean | null;
export type JsonValue = JsonPrimitive | JsonObject | JsonValue[];
export type JsonObject = { [key: string]: JsonValue };

export type PaginatedResponse<TItem> = {
  data: TItem[];
  has_next: boolean;
};

export type Attachment = {
  has_attachment: boolean;
  url: string;
  mime?: string | null;
};

export type Source = {
  name: string;
  url: string;
};

export type AnnouncementMetadata = {
  hash?: string | null;
  is_earnings?: boolean | null;
  category?: string | null;
  related_categories: string[];
  descriptor?: string | null;
  important?: boolean | null;
};

export type AnnouncementDetail = {
  id: string;
  symbol: string;
  date?: string | null;
  headline?: string | null;
  summary?: string | null;
  tags: string[];
  category?: string | null;
  important: boolean;
  attachment?: Attachment | null;
  sources: Source[];
  full_summary?: string | null;
  metadata?: AnnouncementMetadata | null;
  is_earnings?: boolean | null;
  earnings_significant?: boolean | null;
};

export type Alert = {
  id: string;
  symbol: string;
  type?: string | null;
  reason?: string | null;
  timestamp?: string | null;
  meta: Record<string, JsonValue>;
};

export type Concall = {
  id: string;
  symbol: string;
  summary?: string | null;
  analysis?: JsonValue;
  short_analysis?: JsonValue;
  quarter?: string | null;
  month?: string | null;
  filename?: string | null;
  type?: string | null;
  date?: string | null;
};

export type MarketReport = {
  id: string;
  type?: string | null;
  summary?: string | null;
  date?: string | null;
};

export type PresignedUrlResponse = {
  url: string;
  expires_in?: number | null;
};

export type SummaryDetails = {
  portfolio_size: number;
  symbols_processed: number;
  request_id: string;
};

export type SummaryResponse = {
  status: string;
  summary?: string | null;
  details?: SummaryDetails | null;
  error?: string | null;
};

export type ProductEntitlement = { product: string; enabled: boolean; rpm?: number | null; daily?: number | null };
export type WebsocketAddonEntitlement = { product: string; enabled: boolean; tier: string };

export type AccountResponse = {
  account_id: string;
  name?: string | null;
  status: string;
  balance: number;
  products: ProductEntitlement[];
  websocket_addons: WebsocketAddonEntitlement[];
  metadata: Record<string, JsonValue>;
  created_at?: string | null;
  updated_at?: string | null;
};

export type LedgerEntry = {
  entry_id: string;
  account_id: string;
  api_key?: string | null;
  entry_type: string;
  amount: number;
  balance_after?: number | null;
  reference_id?: string | null;
  route_id?: string | null;
  metadata: Record<string, JsonValue>;
  created_at: string;
};

export type AccountDetailResponse = { data: AccountResponse };
export type AccountListResponse = { data: AccountResponse[] };
export type AccountUsageResponse = {
  account_id: string;
  balance: number;
  debited_today: number;
  live_usage: Record<string, number>;
  rate_limits: Record<string, Record<string, number>>;
  reserved: number;
};
export type AccountUsageEnvelope = { data: AccountUsageResponse };
export type LedgerListResponse = { data: LedgerEntry[] };
export type AccountLimitsResponse = Record<string, Record<string, number>>;

export type ApiKeyPayload = {
  api_key: string;
  account_id?: string | null;
  user_id: string;
  status: string;
  rpm?: number | null;
  daily?: number | null;
  apis: string[] | string;
  ws_channels: string[] | string;
  issued_at: string;
  updated_at: string;
  deleted_at?: string | null;
};

export type ApiKeyCreateResponse = { data: ApiKeyPayload };
export type ApiKeyGetResponse = { data: ApiKeyPayload };
export type ApiKeyDeleteResponse = { data: Record<string, JsonValue> };
export type MetaPage = { total: number; page: number; per_page: number; total_pages: number; has_next: boolean };
export type LinksPage = { self: string; next?: string | null; last: string };
export type ApiKeyListResponse = { data: ApiKeyPayload[]; meta: MetaPage; links?: LinksPage | null };
export type MigrateResponse = { data: Record<string, number> };
export type CacheClearResponse = { data: Record<string, number> };
export type UsagePoint = { bucket: string; count: number };
export type UsageResponse = { data: { usage: UsagePoint[] } | Record<string, UsagePoint[]> };

export type ApiKeyAdminPayload = {
  api_key: string;
  account_id: string;
  user_id: string;
  status: string;
  rpm?: number | null;
  daily?: number | null;
  allowed_products: string[] | string;
  allowed_ws_products: string[] | string;
  issued_at: string;
  updated_at: string;
  deleted_at?: string | null;
};

export type ApiKeyAdminListResponse = { data: ApiKeyAdminPayload[] };
export type UsageHistoryPoint = {
  bucket_date: string;
  api_key?: string | null;
  route_id?: string | null;
  request_count: number;
  credits_debited: number;
};
export type UsageHistoryEnvelope = { data: Record<string, JsonValue> };

export type AdminApiKeyDetail = {
  api_key: ApiKeyAdminPayload;
  live_usage: Record<string, number>;
  usage_history: UsageHistoryPoint[];
  recent_ledger: LedgerEntry[];
};

export type AdminApiKeyDetailResponse = { data: AdminApiKeyDetail };

export type AdminAccountDashboard = {
  account: AccountResponse;
  api_keys: ApiKeyAdminPayload[];
  usage_history: UsageHistoryPoint[];
  recent_ledger: LedgerEntry[];
};

export type AdminAccountDashboardResponse = { data: AdminAccountDashboard };

export type RequestCounts = { total: number; completed: number; failed: number };
export type BatchJobResponse = {
  id: string;
  object: string;
  display_name?: string | null;
  status: string;
  created_at: number;
  in_progress_at?: number | null;
  completed_at?: number | null;
  failed_at?: number | null;
  cancelled_at?: number | null;
  request_counts: RequestCounts;
  metadata?: Record<string, JsonValue> | null;
};

export type BatchJobListItem = {
  id: string;
  object: string;
  display_name?: string | null;
  status: string;
  created_at: number;
};

export type BatchJobListResponse = { object: string; data: BatchJobListItem[] };
export type BatchJobCancelResponse = { id: string; status: string };
