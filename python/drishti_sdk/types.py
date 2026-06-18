from __future__ import annotations

from typing import Any, Literal, TypedDict, TypeAlias

JsonPrimitive: TypeAlias = str | int | float | bool | None
JsonValue: TypeAlias = JsonPrimitive | dict[str, "JsonValue"] | list["JsonValue"]

ALERT_TYPES: tuple[str, ...] = (
    "52w_high",
    "52w_low",
    "earnings",
    "high_growth_concalls",
    "price_alert",
    "rvol_alert",
    "volume_alert",
)

KnownAlertType: TypeAlias = Literal[
    "52w_high",
    "52w_low",
    "earnings",
    "high_growth_concalls",
    "price_alert",
    "rvol_alert",
    "volume_alert",
]

AlertType: TypeAlias = KnownAlertType | str


AttachmentLookupStatus: TypeAlias = Literal[
    "ready", "not_found", "invalid_id", "no_attachment", "no_transcript"
]


class AttachmentLookupItem(TypedDict, total=False):
    id: str
    status: AttachmentLookupStatus
    url: str | None
    expires_in: int | None
    message: str | None


class BatchAttachmentLookupResponse(TypedDict):
    data: list[AttachmentLookupItem]


class AnnouncementCategoriesData(TypedDict):
    important: list[str]
    not_important: list[str]


class AnnouncementCategoriesResponse(TypedDict):
    data: AnnouncementCategoriesData


class SymbolMetadata(TypedDict, total=False):
    symbol: str
    company_name: str | None
    logo: str | None
    market_cap: int | float | None
    sector: str | None
    basic_industry: str | None
    industry: str | None
    macro_economic_indicator: str | None
    theme: str | None
    scrip_code: str | None


class SymbolMetadataResponse(TypedDict):
    data: list[SymbolMetadata]


class AnnouncementListItem(TypedDict, total=False):
    id: str
    symbol: str
    company_name: str | None
    image: str | None
    date: str | None
    headline: str | None
    title: str | None
    summary: str | None
    category: str | None


class AnnouncementWebSocketItem(AnnouncementListItem, total=False):
    attachment_url: str | None


class AnnouncementDetail(AnnouncementListItem, total=False):
    long_summary: str | None
    related_categories: list[str]
    descriptor: str | None
    important: bool


class AnnouncementWebSocketDetail(AnnouncementDetail, total=False):
    attachment_url: str | None


AnnouncementSummary: TypeAlias = AnnouncementDetail


class AnnouncementBatchResponse(TypedDict):
    data: list[AnnouncementListItem | AnnouncementDetail]


class EarningsListItem(TypedDict, total=False):
    id: str
    symbol: str
    scrip_code: str | None
    company_name: str | None
    image: str | None
    quarter: str | None
    date: str | None
    summary: str | None


class EarningsWebSocketItem(EarningsListItem, total=False):
    attachment_url: str | None


class EarningsDetail(EarningsListItem, total=False):
    earnings_significant: bool
    earnings_table: dict[str, object] | None


class EarningsWebSocketDetail(EarningsDetail, total=False):
    attachment_url: str | None


class NewsItem(TypedDict, total=False):
    id: str
    title: str | None
    specific_title: str | None
    summary: str | None
    long_summary: str | None
    company: str | None
    source: str | None
    symbol: str | None
    sentiment: str | None
    article_type: str | None
    scrip_code: str | None
    date: str | None
    link: str | None


class AlertMeta(TypedDict):
    primary_drivers: list[str]


class Alert(TypedDict, total=False):
    id: str
    symbol: str
    type: AlertType | None
    reason: str | None
    timestamp: str | None
    meta: AlertMeta


class ConcallSentimentKeyIndicators(TypedDict, total=False):
    positive: list[str]
    negative: list[str]


class ConcallSentiment(TypedDict, total=False):
    key_indicators: ConcallSentimentKeyIndicators


class ConcallSentimentAnalysis(TypedDict, total=False):
    sentiment: ConcallSentiment


class ConcallListItem(TypedDict, total=False):
    id: str
    symbol: str
    short_analysis: JsonValue
    transcript_url: str | None
    audio_url: str | None
    sentiment_analysis: ConcallSentimentAnalysis
    quarter: str | None
    date: str | None


class ConcallDetail(ConcallListItem, total=False):
    expanded_analysis: JsonValue


Concall: TypeAlias = ConcallDetail


class UpcomingConcallListItem(TypedDict, total=False):
    id: str
    symbol: str
    quarter: str | None
    meeting_date: str | None


class UpcomingConcallDetail(UpcomingConcallListItem, total=False):
    intimation_attachment: str | None


UpcomingConcall: TypeAlias = UpcomingConcallDetail


class SymbolQuarterKey(TypedDict):
    symbol: str
    quarter: str


ConcallTranscriptLookupStatus: TypeAlias = Literal["ready", "not_found", "no_transcript"]


class ConcallTranscriptLookupItem(TypedDict, total=False):
    symbol: str
    quarter: str
    id: str | None
    status: ConcallTranscriptLookupStatus
    transcript_url: str | None
    audio_url: str | None
    expires_in: int | None
    message: str | None


class ConcallTranscriptBatchResponse(TypedDict):
    data: list[ConcallTranscriptLookupItem]


class ConcallArtifactUrlsResponse(TypedDict, total=False):
    transcript_url: str | None
    audio_url: str | None
    expires_in: int | None


class PresignedUrlResponse(TypedDict):
    url: str
    expires_in: int | None


class SummaryDetails(TypedDict):
    portfolio_size: int
    symbols_processed: int
    request_id: str


class SummaryResponse(TypedDict, total=False):
    status: str
    summary: str | None
    details: SummaryDetails | None
    error: str | None


class ProductEntitlement(TypedDict, total=False):
    product: str
    enabled: bool
    rpm: int | None
    daily: int | None


class WebsocketAddonEntitlement(TypedDict, total=False):
    product: str
    enabled: bool
    tier: str


class AccountResponse(TypedDict, total=False):
    account_id: str
    name: str | None
    status: str
    balance: int
    products: list[ProductEntitlement]
    websocket_addons: list[WebsocketAddonEntitlement]
    live_entitlement: dict[str, Any]
    metadata: dict[str, Any]
    created_at: str | None
    updated_at: str | None


class LedgerEntry(TypedDict, total=False):
    entry_id: str
    account_id: str
    api_key: str | None
    entry_type: str
    amount: int
    balance_after: int | None
    reference_id: str | None
    route_id: str | None
    metadata: dict[str, Any]
    created_at: str


class AccountDetailResponse(TypedDict):
    data: AccountResponse

class AccountUsageResponse(TypedDict):
    account_id: str
    balance: int
    debited_today: int
    live_usage: dict[str, int]
    rate_limits: dict[str, dict[str, int]]
    live_entitlement: dict[str, Any]
    reserved: int


class AccountUsageEnvelope(TypedDict):
    data: AccountUsageResponse


class LedgerListResponse(TypedDict):
    data: list[LedgerEntry]


class AccountLimitsResponse(TypedDict):
    rest: dict[str, dict[str, int]]
    websocket: dict[str, Any]


class RequestCounts(TypedDict):
    total: int
    completed: int
    failed: int


class BatchJobResponse(TypedDict, total=False):
    id: str
    object: str
    display_name: str | None
    status: str
    created_at: int
    in_progress_at: int | None
    completed_at: int | None
    failed_at: int | None
    cancelled_at: int | None
    request_counts: RequestCounts
    metadata: dict[str, Any] | None


class BatchJobListItem(TypedDict, total=False):
    id: str
    object: str
    display_name: str | None
    status: str
    created_at: int


class BatchJobListResponse(TypedDict):
    object: str
    data: list[BatchJobListItem]


class BatchJobCancelResponse(TypedDict):
    id: str
    status: str


class PaginatedAnnouncementResponse(TypedDict):
    data: list[AnnouncementListItem | AnnouncementDetail]
    has_next: bool


class LightweightIndexItem(TypedDict, total=False):
    id: str
    symbol: str
    quarter: str | None
    date: str | None


class PaginatedLightweightIndexResponse(TypedDict):
    data: list[LightweightIndexItem]
    has_next: bool


class PaginatedEarningsResponse(TypedDict):
    data: list[EarningsListItem | EarningsDetail]
    has_next: bool


class PaginatedConcallResponse(TypedDict):
    data: list[Concall]
    has_next: bool


class PaginatedUpcomingConcallResponse(TypedDict):
    data: list[UpcomingConcall]
    has_next: bool


class PaginatedAlertResponse(TypedDict):
    data: list[Alert]
    has_next: bool


class PaginatedNewsResponse(TypedDict):
    data: list[NewsItem]
    has_next: bool
