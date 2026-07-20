"""Request parameter models for Drishti API /v1 endpoints."""

from __future__ import annotations

import json
from typing import Any, Literal, cast

from pydantic import BaseModel, ConfigDict, Field

from drishti_sdk.types import AlertType, BatchResultLine, SummaryMode

NewsSentiment = Literal["positive", "negative", "neutral"]

def _format_query_params(raw: dict[str, Any]) -> dict[str, Any] | None:
    out: dict[str, Any] = {}
    for key, value in raw.items():
        if value is None:
            continue
        if key in {"symbols", "scrip_codes", "categories", "ids", "type"} and isinstance(value, list):
            out[key] = ",".join(str(item) for item in value)
        else:
            out[key] = value
    return out or None


class NewsQueryParams(BaseModel):
    """Query parameters for GET /v1/news."""

    model_config = ConfigDict(populate_by_name=True)

    symbols: list[str] | None = None
    scrip_codes: list[str] | None = None
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
    scrip_codes: list[str] | None = None
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
    important: bool | None = None


class IndexQueryParams(BaseModel):
    """GET /v1/earnings/index and GET /v1/concalls/index."""

    model_config = ConfigDict(populate_by_name=True)

    symbols: list[str] | None = None
    scrip_codes: list[str] | None = None
    from_: str | None = Field(default=None, alias="from")
    to: str | None = None
    page: int | None = None
    limit: int | None = None

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(
            self.model_dump(exclude_none=True, by_alias=True)
        )


class EarningsIndexQueryParams(IndexQueryParams):
    """GET /v1/earnings/index."""

    quarter: str | None = None


class ConcallsIndexQueryParams(IndexQueryParams):
    """GET /v1/concalls/index."""

    quarter: str | None = None


class EarningsQueryParams(PaginatedFeedQueryParams):
    """GET /v1/earnings."""

    ids: list[str] | None = None
    quarter: str | None = None


class UpcomingEarningsQueryParams(BaseModel):
    """GET /v1/earnings/upcoming."""

    model_config = ConfigDict(populate_by_name=True)

    symbols: list[str] | None = None
    scrip_codes: list[str] | None = None
    page: int | None = None
    limit: int | None = None

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(
            self.model_dump(exclude_none=True, by_alias=True)
        )


class ConcallsQueryParams(PaginatedFeedQueryParams):
    """GET /v1/concalls."""


class UpcomingConcallsQueryParams(BaseModel):
    """GET /v1/concalls/upcoming."""

    model_config = ConfigDict(populate_by_name=True)

    symbols: list[str] | None = None
    scrip_codes: list[str] | None = None
    detailed: bool | None = None
    page: int | None = None
    limit: int | None = None

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(
            self.model_dump(exclude_none=True, by_alias=True)
        )


class AlertsQueryParams(BaseModel):
    """GET /v1/alerts."""

    model_config = ConfigDict(populate_by_name=True)

    symbols: list[str] | None = None
    scrip_codes: list[str] | None = None
    type: list[AlertType] | None = None
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

    symbols: list[str] | None = None
    scrip_codes: list[str] | None = None

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(self.model_dump(exclude_none=True))


class SymbolQuarterDetailQueryParams(BaseModel):
    """Optional query flags for GET /v1/earnings/detail and GET /v1/concalls/detail."""

    detailed: bool | None = None

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(self.model_dump(exclude_none=True))


class SymbolQuarterQueryParams(SymbolQuarterDetailQueryParams):
    """GET /v1/earnings/detail and GET /v1/concalls/detail."""

    symbol: str | None = None
    scrip_code: str | None = None
    quarter: str


class SymbolQuarterTranscriptQueryParams(BaseModel):
    """GET /v1/concalls/transcript."""

    symbol: str | None = None
    scrip_code: str | None = None
    quarter: str

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(self.model_dump(exclude_none=True))


ContentRetentionHeader = Literal["none"]


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
AnnouncementsQueryParams = AnnouncementsListQueryParams


class DailySummaryPortfolioItem(BaseModel):
    """One holding in a POST /v1/daily-summary portfolio."""

    symbol: str
    exposure: float = 0.0
    label: str | None = None


class DailySummaryItem(BaseModel):
    """Canonical symbol input with optional exposure and label."""

    symbol: str
    exposure: float | None = None
    label: str | None = None



class DailySummaryRequest(BaseModel):
    """JSON body for POST /v1/daily-summary."""

    mode: SummaryMode | None = None
    portfolio: list[DailySummaryPortfolioItem] | None = None
    symbols: list[str] | None = None
    items: list[DailySummaryItem] | None = None

    def to_request_body(self) -> dict[str, Any]:
        body: dict[str, Any] = {}
        if self.mode is not None:
            body["mode"] = self.mode
        if self.portfolio is not None:
            body["portfolio"] = [
                item.model_dump(exclude_none=True)
                for item in self.portfolio
            ]
        if self.symbols is not None:
            body["symbols"] = self.symbols
        if self.items is not None:
            body["items"] = [
                item.model_dump(exclude_none=True)
                for item in self.items
            ]
        return body


class BatchSummaryInputLine(DailySummaryRequest):
    """One JSONL line for POST /v1/batch/jobs."""

    custom_id: str

    def to_jsonl_line(self) -> str:
        return self.model_dump_json(exclude_none=True)


def build_batch_input_jsonl(lines: list[BatchSummaryInputLine]) -> bytes:
    """Serialize batch summary input lines to JSONL bytes."""
    if not lines:
        raise ValueError("At least one batch input line is required")
    payload = "\n".join(line.to_jsonl_line() for line in lines)
    return f"{payload}\n".encode("utf-8")


def parse_batch_result_jsonl(content: str) -> list[BatchResultLine]:
    """Parse JSONL text from GET /v1/batch/jobs/{job_id}/results."""
    lines: list[BatchResultLine] = []
    for raw_line in content.splitlines():
        if not raw_line.strip():
            continue
        parsed = json.loads(raw_line)
        if isinstance(parsed, dict):
            lines.append(cast(BatchResultLine, parsed))
    return lines


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
