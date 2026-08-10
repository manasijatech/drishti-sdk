export type JsonPrimitive = string | number | boolean | null;
export type JsonValue = JsonPrimitive | JsonObject | JsonValue[];
export type JsonObject = { [key: string]: JsonValue };

export type PaginatedResponse<TItem> = {
  data: TItem[];
  has_next: boolean;
};

export type AttachmentLookupStatus = "ready" | "not_found" | "invalid_id" | "no_attachment";

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

export type AnnouncementCategoriesData = {
  important: string[];
  not_important: string[];
};

export type AnnouncementCategoriesResponse = {
  data: AnnouncementCategoriesData;
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
};

export type AnnouncementWebSocketItem = AnnouncementListItem & {
  attachment_url?: string | null;
};

export type AnnouncementDetail = AnnouncementListItem & {
  long_summary?: string | null;
  related_categories?: string[];
  descriptor?: string | null;
  important?: boolean;
  extracted_information?: JsonValue;
};

export type AnnouncementWebSocketDetail = AnnouncementDetail & {
  attachment_url?: string | null;
};

/** Alias matching drishti-api `AnnouncementSummary` (detail rows with `detailed=true`). */
export type AnnouncementSummary = AnnouncementDetail;

export type AnnouncementBatchResponse = {
  data: Array<AnnouncementListItem | AnnouncementDetail>;
};

export type LightweightIndexItem = {
  id: string;
  symbol: string;
  quarter?: string | null;
  date?: string | null;
};

export type EarningsListItem = {
  id: string;
  symbol: string;
  scrip_code?: string | null;
  company_name?: string | null;
  image?: string | null;
  quarter?: string | null;
  date?: string | null;
  summary?: string | null;
};

export type EarningsWebSocketItem = EarningsListItem & {
  attachment_url?: string | null;
};

export type EarningsDetail = EarningsListItem & {
  earnings_significant?: boolean;
  earnings_table?: JsonObject | null;
};

export type EarningsWebSocketDetail = EarningsDetail & {
  attachment_url?: string | null;
};

export type UpcomingEarningsListItem = {
  id: string;
  event_id?: string | null;
  bm_desc?: string | null;
  body?: string | null;
  company?: string | null;
  date?: string | null;
  purpose?: string | null;
  symbol: string;
  title?: string | null;
  quarter?: string | null;
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

/** Known public alert types returned by Drishti alerts routes. */
export const ALERT_TYPES = [
  "52w_high",
  "52w_low",
  "earnings",
  "high_growth_concalls",
  "price_alert",
  "rvol_alert",
  "volume_alert",
] as const;

export type KnownAlertType = (typeof ALERT_TYPES)[number];

/** Public alert type with forward-compatible string fallback. */
export type AlertType = KnownAlertType | (string & {});

export type AlertMeta = {
  primary_drivers: string[];
};

/** Delayed market-price context included with price alerts. */
export type AlertPrice = {
  value: number;
  change_percent: number;
  as_of: string;
};

export type Alert = {
  id: string;
  symbol: string;
  type?: AlertType | null;
  /** WebSocket compatibility alias for `type`; REST responses use `type`. */
  alert_type?: AlertType | null;
  reason?: string | null;
  timestamp?: string | null;
  price?: AlertPrice | null;
  meta: AlertMeta;
};

export type ConcallSentimentKeyIndicators = {
  positive?: string[];
  negative?: string[];
};

export type ConcallSentiment = {
  key_indicators?: ConcallSentimentKeyIndicators;
};

export type ConcallSentimentAnalysis = {
  sentiment?: ConcallSentiment;
};

export type ConcallListItem = {
  id: string;
  symbol: string;
  short_analysis?: JsonValue;
  transcript_url?: string | null;
  audio_url?: string | null;
  sentiment_analysis?: ConcallSentimentAnalysis;
  quarter?: string | null;
  date?: string | null;
};

/** Alias for list/detail concall payloads (`expanded_analysis` when `detailed=true`). */
export type Concall = ConcallListItem & {
  expanded_analysis?: JsonValue;
};

export type ConcallDetail = Concall;

export type UpcomingConcallListItem = {
  id: string;
  symbol: string;
  quarter?: string | null;
  meeting_date?: string | null;
};

export type UpcomingConcallDetail = UpcomingConcallListItem & {
  intimation_attachment?: string | null;
};

export type UpcomingConcall = UpcomingConcallListItem | UpcomingConcallDetail;

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

export type SummaryStatus = "success" | "success_no_news" | "success_no_signal" | "error";

export type SummaryInputType = "portfolio" | "watchlist";

export type SummaryMode = "exposure" | "intraday_movements" | "news_context";

export type SummaryDetails = {
  portfolio_size: number;
  submitted_symbol_count?: number;
  symbols_processed: number;
  request_id: string;
  mode?: SummaryMode;
  input_type?: SummaryInputType;
};

export type SummaryResponse = {
  status: SummaryStatus;
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
  retention_policy?: Record<string, JsonValue>;
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
  account: Record<string, Record<string, number>>;
  websocket: Record<string, JsonValue>;
};

export type RequestCounts = { total: number; completed: number; failed: number };

/** Batch job statuses returned by GET /v1/batch/jobs/{job_id}. */
export const BATCH_JOB_STATUSES = [
  "pending",
  "running",
  "succeeded",
  "partial",
  "failed",
  "cancelled",
] as const;

/** Terminal batch job statuses — polling should stop when status is one of these. */
export const BATCH_JOB_TERMINAL_STATUSES = [
  "succeeded",
  "partial",
  "failed",
  "cancelled",
] as const;

export type BatchJobStatus = (typeof BATCH_JOB_STATUSES)[number];
export type BatchJobTerminalStatus = (typeof BATCH_JOB_TERMINAL_STATUSES)[number];

export type BatchBillingSummary = {
  submit_credits_charged?: number;
  item_credits_charged?: number;
  total_credits_charged?: number;
  billable_items?: number;
  free_items?: number;
  failed_items?: number;
};

export type BatchJobResponse = {
  id: string;
  object: string;
  display_name?: string | null;
  status: BatchJobStatus;
  created_at: number;
  in_progress_at?: number | null;
  completed_at?: number | null;
  failed_at?: number | null;
  cancelled_at?: number | null;
  request_counts: RequestCounts;
  metadata?: Record<string, JsonValue> | null;
  billing?: BatchBillingSummary | null;
};

export type BatchJobListItem = {
  id: string;
  object: string;
  display_name?: string | null;
  status: BatchJobStatus;
  created_at: number;
};

export type BatchJobListResponse = { object: string; data: BatchJobListItem[] };
export type BatchJobCancelResponse = { id: string; status: "cancelled" };

export type BatchResultLineStatus = SummaryStatus;

export type BatchResultResponseBody = {
  summary?: string | null;
  details?: SummaryDetails | null;
};

export type BatchResultResponse = {
  status_code: number;
  body?: BatchResultResponseBody | null;
};

export type BatchResultLine = {
  id: string;
  custom_id: string;
  status: BatchResultLineStatus;
  response?: BatchResultResponse | null;
  error?: string | null;
};
