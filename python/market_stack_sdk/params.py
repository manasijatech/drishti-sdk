"""Request parameter models for Alpha API /v1 endpoints."""

from __future__ import annotations

from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator

MAX_SYMBOLS_PER_REQUEST = 20
MAX_CATEGORIES_PER_REQUEST = 20
MAX_NEWS_LIMIT = 100
MAX_FEED_LIMIT = 500

NewsSentiment = Literal["positive", "negative", "neutral"]


def _normalize_unique_strings(
    values: list[str] | None,
    *,
    field_name: str,
    max_items: int,
    uppercase: bool = False,
) -> list[str] | None:
    if values is None:
        return None
    normalized: list[str] = []
    seen: set[str] = set()
    for raw in values:
        for part in raw.split(","):
            item = part.strip()
            if uppercase:
                item = item.upper()
            if not item:
                continue
            key = item.casefold()
            if key in seen:
                continue
            seen.add(key)
            normalized.append(item)
    if len(normalized) > max_items:
        raise ValueError(f"{field_name} accepts at most {max_items} unique values")
    return normalized or None


def _format_query_params(raw: dict[str, Any]) -> dict[str, Any] | None:
    out: dict[str, Any] = {}
    for key, value in raw.items():
        if value is None:
            continue
        if key in {"symbols", "categories"} and isinstance(value, list):
            out[key] = ",".join(value)
        else:
            out[key] = value
    return out or None


class NewsQueryParams(BaseModel):
    """Query parameters for GET /v1/news."""

    model_config = ConfigDict(extra="forbid", populate_by_name=True)

    symbols: list[str] | None = Field(
        default=None,
        description=(
            "Filter by NSE/BSE tickers (e.g. RELIANCE, TCS). "
            f"At most {MAX_SYMBOLS_PER_REQUEST} unique symbols per request."
        ),
        json_schema_extra={"examples": [["RELIANCE", "TCS"]]},
    )
    sentiment: NewsSentiment | None = Field(
        default=None,
        description="Filter by sentiment: positive, negative, or neutral.",
    )
    from_: str | None = Field(
        default=None,
        alias="from",
        description="ISO date/datetime lower bound (inclusive), e.g. 2026-04-01.",
    )
    to: str | None = Field(
        default=None,
        description="ISO date/datetime upper bound (inclusive), e.g. 2026-04-09T23:59:59Z.",
    )
    page: int = Field(default=1, ge=1, description="Page number (1-based).")
    limit: int = Field(
        default=20,
        ge=1,
        le=MAX_NEWS_LIMIT,
        description=f"Page size (max {MAX_NEWS_LIMIT}).",
    )

    @field_validator("symbols")
    @classmethod
    def validate_symbols(cls, values: list[str] | None) -> list[str] | None:
        return _normalize_unique_strings(
            values,
            field_name="symbols",
            max_items=MAX_SYMBOLS_PER_REQUEST,
            uppercase=True,
        )

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(
            self.model_dump(exclude_none=True, exclude_defaults=True, by_alias=True)
        )


class FeedQueryParams(BaseModel):
    """Query parameters for GET /v1/announcements and GET /v1/earnings."""

    model_config = ConfigDict(extra="forbid", populate_by_name=True)

    symbols: list[str] | None = Field(
        default=None,
        description=(
            "Filter by NSE/BSE tickers (e.g. RELIANCE). Use symbols, not symbol. "
            f"At most {MAX_SYMBOLS_PER_REQUEST} unique symbols per request."
        ),
        json_schema_extra={"examples": [["RELIANCE"]]},
    )
    categories: list[str] | None = Field(
        default=None,
        description=(
            "Filter by announcement category (e.g. Dividend, Acquisition). "
            f"At most {MAX_CATEGORIES_PER_REQUEST} unique categories."
        ),
    )
    from_: str | None = Field(
        default=None,
        alias="from",
        description="ISO date/datetime lower bound (inclusive).",
    )
    to: str | None = Field(
        default=None,
        description="ISO date/datetime upper bound (inclusive).",
    )
    detailed: bool = Field(
        default=False,
        description="If true, return richer fields per row (heavier payload).",
    )
    page: int = Field(default=1, ge=1, description="Page number (1-based).")
    limit: int = Field(
        default=50,
        ge=1,
        le=MAX_FEED_LIMIT,
        description=f"Page size (max {MAX_FEED_LIMIT}).",
    )

    @field_validator("symbols")
    @classmethod
    def validate_symbols(cls, values: list[str] | None) -> list[str] | None:
        return _normalize_unique_strings(
            values,
            field_name="symbols",
            max_items=MAX_SYMBOLS_PER_REQUEST,
            uppercase=True,
        )

    @field_validator("categories")
    @classmethod
    def validate_categories(cls, values: list[str] | None) -> list[str] | None:
        return _normalize_unique_strings(
            values,
            field_name="categories",
            max_items=MAX_CATEGORIES_PER_REQUEST,
        )

    def to_query_params(self) -> dict[str, Any] | None:
        return _format_query_params(
            self.model_dump(exclude_none=True, exclude_defaults=True, by_alias=True)
        )


AnnouncementsQueryParams = FeedQueryParams
EarningsQueryParams = FeedQueryParams


class DailySummaryPortfolioItem(BaseModel):
    """One holding in a POST /v1/daily-summary portfolio."""

    model_config = ConfigDict(extra="forbid")

    symbol: str = Field(
        ...,
        description="NSE/BSE ticker symbol (e.g. RELIANCE, TCS).",
        json_schema_extra={"examples": ["RELIANCE"]},
    )
    exposure: float = Field(
        0.0,
        ge=0.0,
        le=100.0,
        description="Portfolio weight as a percentage from 0 to 100.",
    )

    @field_validator("symbol")
    @classmethod
    def validate_symbol(cls, value: str) -> str:
        normalized = value.strip().upper()
        if not normalized:
            raise ValueError("symbol must not be empty")
        return normalized


class DailySummaryRequest(BaseModel):
    """JSON body for POST /v1/daily-summary."""

    model_config = ConfigDict(extra="forbid")

    portfolio: list[DailySummaryPortfolioItem] = Field(
        ...,
        min_length=1,
        description="Holdings to include in the generated market summary.",
    )

    def to_request_body(self) -> dict[str, Any]:
        return {
            "portfolio": [
                {"symbol": item.symbol, "exposure": item.exposure}
                for item in self.portfolio
            ]
        }


def coerce_query_params(
    params: FeedQueryParams | NewsQueryParams | dict[str, Any] | None,
) -> dict[str, Any] | None:
    """Accept a params model or a plain dict for client helpers."""
    if params is None:
        return None
    if isinstance(params, (FeedQueryParams, NewsQueryParams)):
        return params.to_query_params()
    return dict(params)
