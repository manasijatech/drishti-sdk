from __future__ import annotations

from typing import Any, TypedDict, TypeAlias

JsonPrimitive: TypeAlias = str | int | float | bool | None
JsonValue: TypeAlias = JsonPrimitive | dict[str, "JsonValue"] | list["JsonValue"]


class Attachment(TypedDict, total=False):
    has_attachment: bool
    url: str
    mime: str | None


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


class Alert(TypedDict, total=False):
    id: str
    symbol: str
    type: str | None
    reason: str | None
    timestamp: str | None
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


class MarketReport(TypedDict, total=False):
    id: str
    type: str | None
    summary: str | None
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


class AccountListResponse(TypedDict):
    data: list[AccountResponse]


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


class ApiKeyPayload(TypedDict, total=False):
    api_key: str
    account_id: str | None
    user_id: str
    status: str
    rpm: int | None
    daily: int | None
    apis: list[str] | str
    ws_channels: list[str] | str
    issued_at: str
    updated_at: str
    deleted_at: str | None


class MetaPage(TypedDict):
    total: int
    page: int
    per_page: int
    total_pages: int
    has_next: bool


class LinksPage(TypedDict, total=False):
    self: str
    next: str | None
    last: str


class ApiKeyCreateResponse(TypedDict):
    data: ApiKeyPayload


class ApiKeyGetResponse(TypedDict):
    data: ApiKeyPayload


class ApiKeyDeleteResponse(TypedDict):
    data: dict[str, Any]


class ApiKeyListResponse(TypedDict, total=False):
    data: list[ApiKeyPayload]
    meta: MetaPage
    links: LinksPage | None


class MigrateResponse(TypedDict):
    data: dict[str, int]


class CacheClearResponse(TypedDict):
    data: dict[str, int]


class UsagePoint(TypedDict):
    bucket: str
    count: int


class UsageResponse(TypedDict):
    data: dict[str, list[UsagePoint]]


class ApiKeyAdminPayload(TypedDict, total=False):
    api_key: str
    account_id: str
    user_id: str
    status: str
    rpm: int | None
    daily: int | None
    allowed_products: list[str] | str
    allowed_ws_products: list[str] | str
    issued_at: str
    updated_at: str
    deleted_at: str | None


class ApiKeyAdminListResponse(TypedDict):
    data: list[ApiKeyAdminPayload]


class UsageHistoryPoint(TypedDict, total=False):
    bucket_date: str
    api_key: str | None
    route_id: str | None
    request_count: int
    credits_debited: int


class UsageHistoryEnvelope(TypedDict):
    data: dict[str, Any]


class AdminApiKeyDetail(TypedDict):
    api_key: ApiKeyAdminPayload
    live_usage: dict[str, int]
    usage_history: list[UsageHistoryPoint]
    recent_ledger: list[LedgerEntry]


class AdminApiKeyDetailResponse(TypedDict):
    data: AdminApiKeyDetail


class AdminAccountDashboard(TypedDict):
    account: AccountResponse
    api_keys: list[ApiKeyAdminPayload]
    usage_history: list[UsageHistoryPoint]
    recent_ledger: list[LedgerEntry]


class AdminAccountDashboardResponse(TypedDict):
    data: AdminAccountDashboard


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


class PaginatedMarketReportResponse(TypedDict):
    data: list[MarketReport]
    has_next: bool
