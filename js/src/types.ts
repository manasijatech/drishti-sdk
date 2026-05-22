export type JsonPrimitive = string | number | boolean | null;
export type JsonValue = JsonPrimitive | JsonObject | JsonValue[];
export type JsonObject = { [key: string]: JsonValue };

export type PaginatedResponse<TItem> = {
  data: TItem[];
  has_next: boolean;
  missing_ids?: string[];
};

export type AttachmentLookupStatus = "ready" | "not_found" | "invalid_id" | "no_attachment" | "no_transcript";

export type AttachmentLookupItem = {
  id: string;
  status: AttachmentLookupStatus;
  url?: string | null;
  expires_in?: number | null;
  message?: string | null;
};

export type BatchAttachmentLookupResponse = {
  data: AttachmentLookupItem[];
};

export type StringListResponse = {
  data: string[];
};

export type SymbolMetadata = {
  symbol: string;
  company_name?: string | null;
  logo?: string | null;
  market_cap?: number | null;
  sector?: string | null;
  basic_industry?: string | null;
  industry?: string | null;
  macro_economic_indicator?: string | null;
  theme?: string | null;
  scrip_code?: string | null;
};

export type SymbolMetadataResponse = {
  data: SymbolMetadata[];
};

export type AnnouncementListItem = {
  id: string;
  symbol: string;
  company_name?: string | null;
  image?: string | null;
  date?: string | null;
  headline?: string | null;
  title?: string | null;
  summary?: string | null;
  category?: string | null;
  attachment_url?: string | null;
};

export type AnnouncementDetail = AnnouncementListItem & {
  long_summary?: string | null;
  related_categories?: string[];
  descriptor?: string | null;
  important?: boolean;
};

/** Alias matching alpha-api `AnnouncementSummary` (detail rows with `detailed=true`). */
export type AnnouncementSummary = AnnouncementDetail;

export type AnnouncementBatchResponse = {
  data: Array<AnnouncementListItem | AnnouncementDetail>;
  missing_ids: string[];
};

export type EarningsListItem = {
  id: string;
  symbol: string;
  company_name?: string | null;
  image?: string | null;
  date?: string | null;
  summary?: string | null;
  attachment_url?: string | null;
};

export type EarningsDetail = EarningsListItem & {
  earnings_significant?: boolean;
  earnings_table?: JsonObject | null;
};

export type NewsItem = {
  id: string;
  title?: string | null;
  specific_title?: string | null;
  summary?: string | null;
  long_summary?: string | null;
  company?: string | null;
  source?: string | null;
  symbol?: string | null;
  sentiment?: string | null;
  article_type?: string | null;
  scrip_code?: string | null;
  date?: string | null;
  link?: string | null;
};

export type AlertMeta = {
  primary_drivers: string[];
};

export type Alert = {
  id: string;
  symbol: string;
  type?: string | null;
  reason?: string | null;
  timestamp?: string | null;
  meta: AlertMeta;
};

export type ConcallListItem = {
  id: string;
  symbol: string;
  short_analysis?: JsonValue;
  transcript_url?: string | null;
  audio_url?: string | null;
  sentiment_analysis?: JsonValue;
  quarter?: string | null;
  date?: string | null;
};

/** Alias for list/detail concall payloads (`expanded_analysis` when `detailed=true`). */
export type Concall = ConcallListItem & {
  expanded_analysis?: JsonValue;
};

export type ConcallDetail = Concall;

export type ConcallTranscriptLookupStatus =
  | "ready"
  | "not_found"
  | "no_transcript";

export type SymbolQuarterKey = {
  symbol: string;
  quarter: string;
};

export type ConcallTranscriptLookupItem = {
  symbol: string;
  quarter: string;
  id?: string | null;
  status: ConcallTranscriptLookupStatus;
  transcript_url?: string | null;
  audio_url?: string | null;
  expires_in?: number | null;
  message?: string | null;
};

export type ConcallTranscriptBatchResponse = {
  data: ConcallTranscriptLookupItem[];
};

export type ConcallArtifactUrlsResponse = {
  transcript_url?: string | null;
  audio_url?: string | null;
  expires_in?: number | null;
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
  live_entitlement: Record<string, JsonValue>;
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
export type AccountUsageResponse = {
  account_id: string;
  balance: number;
  debited_today: number;
  live_usage: Record<string, number>;
  rate_limits: Record<string, Record<string, number>>;
  live_entitlement: Record<string, JsonValue>;
  reserved: number;
};
export type AccountUsageEnvelope = { data: AccountUsageResponse };
export type LedgerListResponse = { data: LedgerEntry[] };
export type AccountLimitsResponse = {
  rest: Record<string, Record<string, number>>;
  websocket: Record<string, JsonValue>;
};

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
