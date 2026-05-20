from __future__ import annotations

from typing import Any, Mapping, TypeVar, cast

import httpx

from market_stack_sdk.exceptions import MarketStackApiError
from market_stack_sdk.params import (
    DailySummaryRequest,
    FeedQueryParams,
    NewsQueryParams,
    coerce_query_params,
)
from market_stack_sdk.types import (
    AccountDetailResponse,
    AccountLimitsResponse,
    AccountUsageEnvelope,
    Alert,
    AnnouncementBatchResponse,
    AnnouncementDetail,
    BatchAttachmentLookupResponse,
    BatchJobCancelResponse,
    BatchJobListResponse,
    BatchJobResponse,
    Concall,
    ConcallArtifactUrlsResponse,
    ConcallTranscriptBatchResponse,
    JsonValue,
    LedgerListResponse,
    PaginatedAlertResponse,
    PaginatedAnnouncementResponse,
    PaginatedConcallResponse,
    PaginatedEarningsResponse,
    EarningsDetail,
    EarningsListItem,
    PaginatedNewsResponse,
    PresignedUrlResponse,
    StringListResponse,
    SummaryResponse,
    SymbolMetadataResponse,
)

DEFAULT_BASE_URL = "https://developers.manasija.in"
DEFAULT_TIMEOUT_SEC = 60.0

TResponse = TypeVar("TResponse")


class MarketStackClient:
    """Sync HTTP client for Alpha API v1 with endpoint-specific response typing."""

    def __init__(
        self,
        *,
        api_key: str,
        base_url: str | None = None,
        timeout: float = DEFAULT_TIMEOUT_SEC,
        headers: Mapping[str, str] | None = None,
    ) -> None:
        if not api_key.strip():
            raise ValueError("MarketStackClient requires a non-empty api_key")
        self._base_url = (base_url or DEFAULT_BASE_URL).rstrip("/")
        self._api_key = api_key
        self._extra_headers = dict(headers) if headers else {}
        self._client = httpx.Client(timeout=timeout)

    def close(self) -> None:
        self._client.close()

    def __enter__(self) -> "MarketStackClient":
        return self

    def __exit__(self, *args: object) -> None:
        self.close()

    def _merge_headers(self) -> dict[str, str]:
        out = dict(self._extra_headers)
        out["X-API-Key"] = self._api_key
        return out

    def _build_path(self, path: str, path_params: Mapping[str, Any] | None = None) -> str:
        if not path_params:
            return path
        resolved_path = path
        for key, value in path_params.items():
            resolved_path = resolved_path.replace(f":{key}", str(value)).replace(f"{{{key}}}", str(value))
        return resolved_path

    def request(
        self,
        method: str,
        path: str,
        *,
        body: JsonValue | None = None,
        data: Mapping[str, Any] | None = None,
        files: Any = None,
        params: Mapping[str, Any] | None = None,
        path_params: Mapping[str, Any] | None = None,
        headers: Mapping[str, str] | None = None,
    ) -> TResponse:
        sub = self._build_path(path, path_params).removeprefix("/")
        url = f"{self._base_url}/{sub}"
        request_headers = self._merge_headers()
        if headers:
            request_headers.update(headers)
        response = self._client.request(
            method.upper(),
            url,
            json=body,
            data=data,
            files=files,
            params=params,
            headers=request_headers,
        )
        return cast(TResponse, self._parse_response(response))

    def get(self, path: str, *, params: Mapping[str, Any] | None = None, path_params: Mapping[str, Any] | None = None) -> TResponse:
        return self.request("GET", path, params=params, path_params=path_params)

    def post(self, path: str, *, body: JsonValue | None = None, params: Mapping[str, Any] | None = None, path_params: Mapping[str, Any] | None = None) -> TResponse:
        return self.request("POST", path, body=body, params=params, path_params=path_params)

    def put(self, path: str, *, body: JsonValue | None = None, params: Mapping[str, Any] | None = None, path_params: Mapping[str, Any] | None = None) -> TResponse:
        return self.request("PUT", path, body=body, params=params, path_params=path_params)

    def patch(self, path: str, *, body: JsonValue | None = None, params: Mapping[str, Any] | None = None, path_params: Mapping[str, Any] | None = None) -> TResponse:
        return self.request("PATCH", path, body=body, params=params, path_params=path_params)

    def delete(self, path: str, *, params: Mapping[str, Any] | None = None, path_params: Mapping[str, Any] | None = None) -> TResponse:
        return self.request("DELETE", path, params=params, path_params=path_params)

    def _parse_response(self, response: httpx.Response) -> JsonValue | str | None:
        if response.status_code == 204:
            return None
        content_type = response.headers.get("content-type", "")
        if "application/json" in content_type and response.content:
            data: JsonValue = response.json()
        elif response.content:
            data = response.text
        else:
            data = None
        if response.is_error:
            raise MarketStackApiError(response.status_code, data)
        return data

    def get_news(
        self,
        params: NewsQueryParams | Mapping[str, Any] | None = None,
    ) -> PaginatedNewsResponse:
        return self.get("/v1/news", params=coerce_query_params(params), path_params=None)

    def get_symbols_metadata(self, params: Mapping[str, Any] | None = None) -> SymbolMetadataResponse:
        return self.get("/v1/symbols/metadata", params=params, path_params=None)

    def get_announcements_categories(self, params: Mapping[str, Any] | None = None) -> StringListResponse:
        return self.get("/v1/announcements/categories", params=params, path_params=None)

    def get_announcements(
        self,
        params: FeedQueryParams | Mapping[str, Any] | None = None,
    ) -> PaginatedAnnouncementResponse:
        return self.get("/v1/announcements", params=coerce_query_params(params), path_params=None)

    def get_announcements_items(self, params: Mapping[str, Any] | None = None) -> AnnouncementBatchResponse:
        result = self.get("/v1/announcements", params=params, path_params=None)
        return {
            "data": result.get("data", []),
            "missing_ids": result.get("missing_ids", []),
        }

    def get_announcements_attachments(self, params: Mapping[str, Any] | None = None) -> BatchAttachmentLookupResponse:
        return self.get("/v1/announcements/attachments", params=params, path_params=None)

    def post_daily_summary(
        self,
        body: DailySummaryRequest | JsonValue | None = None,
        params: Mapping[str, Any] | None = None,
    ) -> SummaryResponse:
        request_body: JsonValue | None
        if isinstance(body, DailySummaryRequest):
            request_body = body.to_request_body()
        else:
            request_body = body
        return self.post("/v1/daily-summary", body=request_body, params=params, path_params=None)

    def get_earnings(
        self,
        params: FeedQueryParams | Mapping[str, Any] | None = None,
    ) -> PaginatedEarningsResponse:
        return self.get("/v1/earnings", params=coerce_query_params(params), path_params=None)

    def get_earnings_detail(
        self,
        *,
        symbol: str,
        quarter: str,
        params: Mapping[str, Any] | None = None,
    ) -> EarningsListItem | EarningsDetail:
        query = dict(params or {})
        query["symbol"] = symbol
        query["quarter"] = quarter
        return self.get("/v1/earnings/detail", params=query, path_params=None)

    def get_earnings_attachments(self, params: Mapping[str, Any] | None = None) -> BatchAttachmentLookupResponse:
        return self.get("/v1/earnings/attachments", params=params, path_params=None)

    def get_concalls(self, params: Mapping[str, Any] | None = None) -> PaginatedConcallResponse:
        return self.get("/v1/concalls", params=params, path_params=None)

    def get_concalls_detail(
        self,
        *,
        symbol: str,
        quarter: str,
        params: Mapping[str, Any] | None = None,
    ) -> Concall:
        query = dict(params or {})
        query["symbol"] = symbol
        query["quarter"] = quarter
        return self.get("/v1/concalls/detail", params=query, path_params=None)

    def get_concalls_transcript(
        self,
        *,
        symbol: str,
        quarter: str,
        params: Mapping[str, Any] | None = None,
    ) -> ConcallArtifactUrlsResponse:
        query = dict(params or {})
        query["symbol"] = symbol
        query["quarter"] = quarter
        return self.get("/v1/concalls/transcript", params=query, path_params=None)

    def post_concalls_transcripts(
        self,
        *,
        items: list[dict[str, str]],
        params: Mapping[str, Any] | None = None,
    ) -> ConcallTranscriptBatchResponse:
        return self.post(
            "/v1/concalls/transcripts",
            body={"items": items},
            params=params,
            path_params=None,
        )

    def get_alerts(self, params: Mapping[str, Any] | None = None) -> PaginatedAlertResponse:
        return self.get("/v1/alerts", params=params, path_params=None)

    def get_account(self, params: Mapping[str, Any] | None = None) -> AccountDetailResponse:
        return self.get("/v1/account", params=params, path_params=None)

    def get_account_limits(self, params: Mapping[str, Any] | None = None) -> AccountLimitsResponse:
        return self.get("/v1/account/limits", params=params, path_params=None)

    def get_account_usage(self, params: Mapping[str, Any] | None = None) -> AccountUsageEnvelope:
        return self.get("/v1/account/usage", params=params, path_params=None)

    def get_account_ledger(self, params: Mapping[str, Any] | None = None) -> LedgerListResponse:
        return self.get("/v1/account/ledger", params=params, path_params=None)

    def post_batch_jobs(self, body: JsonValue | None = None, params: Mapping[str, Any] | None = None) -> BatchJobResponse:
        return self.post("/v1/batch/jobs", body=body, params=params, path_params=None)

    def post_batch_jobs_file(
        self,
        *,
        file_name: str,
        file_bytes: bytes,
        display_name: str | None = None,
        metadata: str | None = None,
        params: Mapping[str, Any] | None = None,
    ) -> BatchJobResponse:
        form_data: dict[str, str] = {}
        if display_name is not None:
            form_data["display_name"] = display_name
        if metadata is not None:
            form_data["metadata"] = metadata
        return self.request(
            "POST",
            "/v1/batch/jobs",
            params=params,
            data=form_data,
            files={"file": (file_name, file_bytes, "application/jsonl")},
        )

    def get_batch_jobs(self, params: Mapping[str, Any] | None = None) -> BatchJobListResponse:
        return self.get("/v1/batch/jobs", params=params, path_params=None)

    def get_batch_jobs_job_id(self, job_id: str | int, params: Mapping[str, Any] | None = None) -> BatchJobResponse:
        return self.get("/v1/batch/jobs/{job_id}", params=params, path_params={"job_id": job_id})

    def delete_batch_jobs_job_id(self, job_id: str | int, params: Mapping[str, Any] | None = None) -> BatchJobCancelResponse:
        return self.delete("/v1/batch/jobs/{job_id}", params=params, path_params={"job_id": job_id})

    def get_batch_jobs_job_id_results(self, job_id: str | int, params: Mapping[str, Any] | None = None) -> str:
        return self.get("/v1/batch/jobs/{job_id}/results", params=params, path_params={"job_id": job_id})

    def request_v1(
        self,
        method: str,
        path: str,
        *,
        json: JsonValue | None = None,
        params: Mapping[str, Any] | None = None,
    ) -> JsonValue | str | None:
        return self.request(method=method, path=f"/v1/{path.removeprefix('/')}" , body=json, params=params)
