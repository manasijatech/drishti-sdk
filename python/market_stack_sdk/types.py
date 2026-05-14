from __future__ import annotations

from typing import Any, TypedDict, TypeAlias

JsonPrimitive: TypeAlias = str | int | float | bool | None
JsonValue: TypeAlias = JsonPrimitive | dict[str, "JsonValue"] | list["JsonValue"]


class Attachment(TypedDict, total=False):
    has_attachment: bool
    url: str
    mime: str | None


class AttachmentLookupItem(TypedDict, total=False):
    id: str
    status: str
    url: str | None
    expires_in: int | None
    message: str | None


class BatchAttachmentLookupResponse(TypedDict):
    data: list[AttachmentLookupItem]


class StringListResponse(TypedDict):
    data: list[str]


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


class Source(TypedDict):
    name: str
    url: str


class AnnouncementMetadata(TypedDict, total=False):
    hash: str | None
    is_earnings: bool | None
    category: str | None
    related_categories: list[str]
    descriptor: str | None
    important: bool | None


class AnnouncementDetail(TypedDict, total=False):
    id: str
    symbol: str
    date: str | None
    headline: str | None
    summary: str | None
    tags: list[str]
    category: str | None
    important: bool
    attachment: Attachment | None
    sources: list[Source]
    full_summary: str | None
    metadata: AnnouncementMetadata | None
    is_earnings: bool | None
    earnings_significant: bool | None


class AnnouncementBatchResponse(TypedDict):
    data: list[AnnouncementDetail]
    missing_ids: list[str]


class NewsItem(TypedDict, total=False):
    id: str
    title: str | None
    description: str | None
    content: str | None
    source: str | None
    symbol: str | None
    sentiment: str | None
    date: str | None
    link: str | None
    image: str | None


class Alert(TypedDict, total=False):
    id: str
    symbol: str
    alert_type: str | None
    reason: str | None
    date: str | None
    meta: dict[str, Any]


class Concall(TypedDict, total=False):
    id: str
    symbol: str
    summary: str | None
    analysis: JsonValue
    short_analysis: JsonValue
    quarter: str | None
    month: str | None
    filename: str | None
    type: str | None
    date: str | None


class PresignedUrlResponse(TypedDict, total=False):
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
    reserved: int


class AccountUsageEnvelope(TypedDict):
    data: AccountUsageResponse


class LedgerListResponse(TypedDict):
    data: list[LedgerEntry]


AccountLimitsResponse: TypeAlias = dict[str, dict[str, int]]


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
    data: list[AnnouncementDetail]
    has_next: bool


class PaginatedConcallResponse(TypedDict):
    data: list[Concall]
    has_next: bool


class PaginatedAlertResponse(TypedDict):
    data: list[Alert]
    has_next: bool


class PaginatedNewsResponse(TypedDict):
    data: list[NewsItem]
    has_next: bool
