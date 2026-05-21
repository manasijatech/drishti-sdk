"""Request parameter models for Alpha API /v1 endpoints."""

from __future__ import annotations

from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field

NewsSentiment = Literal["positive", "negative", "neutral"]


def _format_query_params(raw: dict[str, Any]) -> dict[str, Any] | None:
    out: dict[str, Any] = {}
    for key, value in raw.items():
        if value is None:
            continue
        if key in {"symbols", "categories", "ids", "type"} and isinstance(value, list):
            out[key] = ",".join(str(item) for item in value)
        else:
            out[key] = value
    return out or None


class NewsQueryParams(BaseModel):
    """Query parameters for GET /v1/news."""

    model_config = ConfigDict(populate_by_name=True)

    symbols: list[str] | None = None
    sentiment: NewsSentiment | None = None
    from_: str | None = Field(default=None, alias="from")
    to: str | None = None
    page: int | None = None
    limit: int | None = None

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(
            self.model_dump(exclude_none=True, by_alias=True)
        )


class PaginatedFeedQueryParams(BaseModel):
    """Shared list filters for paginated product feeds."""

    model_config = ConfigDict(populate_by_name=True)

    symbols: list[str] | None = None
    from_: str | None = Field(default=None, alias="from")
    to: str | None = None
    detailed: bool | None = None
    page: int | None = None
    limit: int | None = None

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(
            self.model_dump(exclude_none=True, by_alias=True)
        )


class AnnouncementsListQueryParams(PaginatedFeedQueryParams):
    """GET /v1/announcements list mode."""

    categories: list[str] | None = None


class AnnouncementsByIdsQueryParams(BaseModel):
    """GET /v1/announcements when fetching explicit ObjectIds."""

    model_config = ConfigDict(populate_by_name=True)

    ids: list[str]
    detailed: bool | None = None

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(
            self.model_dump(exclude_none=True, by_alias=True)
        )


class EarningsQueryParams(PaginatedFeedQueryParams):
    """GET /v1/earnings."""


class ConcallsQueryParams(PaginatedFeedQueryParams):
    """GET /v1/concalls."""


class AlertsQueryParams(BaseModel):
    """GET /v1/alerts."""

    model_config = ConfigDict(populate_by_name=True)

    symbols: list[str] | None = None
    type: list[str] | None = None
    from_: str | None = Field(default=None, alias="from")
    to: str | None = None
    important: bool | None = None
    page: int | None = None
    limit: int | None = None

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(
            self.model_dump(exclude_none=True, by_alias=True)
        )


class DocumentIdsQueryParams(BaseModel):
    """GET /v1/announcements/attachments and GET /v1/earnings/attachments."""

    ids: list[str]

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(self.model_dump(exclude_none=True))


class SymbolMetadataQueryParams(BaseModel):
    """GET /v1/symbols/metadata."""

    symbols: list[str]

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(self.model_dump(exclude_none=True))


class SymbolQuarterDetailQueryParams(BaseModel):
    """Optional query flags for GET /v1/earnings/detail and GET /v1/concalls/detail."""

    detailed: bool | None = None

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(self.model_dump(exclude_none=True))


class SymbolQuarterQueryParams(SymbolQuarterDetailQueryParams):
    """GET /v1/earnings/detail, GET /v1/concalls/detail, GET /v1/concalls/transcript."""

    symbol: str
    quarter: str


class BatchJobIdParams(BaseModel):
    """Path param for batch job routes."""

    job_id: str | int


class AccountLedgerQueryParams(BaseModel):
    """GET /v1/account/ledger."""

    limit: int | None = None

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(self.model_dump(exclude_none=True))


class BatchJobsListQueryParams(BaseModel):
    """GET /v1/batch/jobs."""

    limit: int | None = None

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(self.model_dump(exclude_none=True))


FeedQueryParams = AnnouncementsListQueryParams
AnnouncementsQueryParams = AnnouncementsListQueryParams | AnnouncementsByIdsQueryParams


class DailySummaryPortfolioItem(BaseModel):
    """One holding in a POST /v1/daily-summary portfolio."""

    symbol: str
    exposure: float = 0.0


class DailySummaryRequest(BaseModel):
    """JSON body for POST /v1/daily-summary."""

    portfolio: list[DailySummaryPortfolioItem]

    def to_request_body(self) -> dict[str, Any]:
        return {
            "portfolio": [
                {"symbol": item.symbol, "exposure": item.exposure}
                for item in self.portfolio
            ]
        }


def coerce_query_params(
    params: BaseModel | dict[str, Any] | None,
) -> dict[str, Any] | None:
    """Accept a params model or a plain dict for client helpers."""
    if params is None:
        return None
    if isinstance(params, BaseModel):
        to_query = getattr(params, "to_query_params", None)
        if callable(to_query):
            return to_query()
        return _format_query_params(params.model_dump(exclude_none=True, by_alias=True))
    return dict(params)
