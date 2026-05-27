from __future__ import annotations

from typing import TYPE_CHECKING, Any, Mapping, TypeVar, cast

if TYPE_CHECKING:
    from drishti_sdk.websocket import (
        AlphaWebSocketClientSessionOptions,
        AlphaWebSocketSession,
    )

import httpx

from drishti_sdk.exceptions import DrishtiApiError
from drishti_sdk.params import (
    AccountLedgerQueryParams,
    AlertsQueryParams,
    AnnouncementsListQueryParams,
    BatchJobsListQueryParams,
    ConcallsQueryParams,
    DailySummaryRequest,
    DocumentIdsQueryParams,
    NewsSentiment,
    EarningsQueryParams,
    NewsQueryParams,
    SymbolMetadataQueryParams,
    SymbolQuarterQueryParams,
    coerce_query_params,
)
from drishti_sdk.types import (
    AccountDetailResponse,
    AccountLimitsResponse,
    AccountUsageEnvelope,
    Alert,
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
    StringListResponse,
    SummaryResponse,
    SymbolMetadataResponse,
)

DEFAULT_BASE_URL = "https://developers.manasija.in"
DEFAULT_TIMEOUT_SEC = 60.0

TResponse = TypeVar("TResponse")


def _normalize_earnings_item_payload(item: Any) -> Any:
    if not isinstance(item, dict):
        return item
    if "earnings_table" not in item and "earnings_table_extraction" in item:
        item["earnings_table"] = item.get("earnings_table_extraction")
    return item


class DrishtiClient:
    """Sync HTTP client for Drishti API v1 with endpoint-specific response typing."""

    def __init__(
        self,
        *,
        api_key: str,
        base_url: str | None = None,
        timeout: float = DEFAULT_TIMEOUT_SEC,
        headers: Mapping[str, str] | None = None,
    ) -> None:
        if not api_key.strip():
            raise ValueError("DrishtiClient requires a non-empty api_key")
        self._base_url = (base_url or DEFAULT_BASE_URL).rstrip("/")
        self._api_key = api_key
        self._extra_headers = dict(headers) if headers else {}
        self._client = httpx.Client(timeout=timeout)

    def close(self) -> None:
        self._client.close()

    def __enter__(self) -> "DrishtiClient":
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
            raise DrishtiApiError(response.status_code, data)
        return data

    def get_news(
        self,
        *,
        symbols: list[str] | None = None,
        scrip_codes: list[str] | None = None,
        sentiment: NewsSentiment | None = None,
        from_: str | None = None,
        to: str | None = None,
        page: int | None = None,
        limit: int | None = None,
    ) -> PaginatedNewsResponse:
        params = NewsQueryParams(
            symbols=symbols,
            scrip_codes=scrip_codes,
            sentiment=sentiment,
            from_=from_,
            to=to,
            page=page,
            limit=limit,
        )
        return self.get("/v1/news", params=coerce_query_params(params), path_params=None)

    def get_symbols_metadata(
        self,
        *,
        symbols: list[str] | None = None,
        scrip_codes: list[str] | None = None,
    ) -> SymbolMetadataResponse:
        params = SymbolMetadataQueryParams(symbols=symbols, scrip_codes=scrip_codes)
        return self.get("/v1/symbols/metadata", params=coerce_query_params(params), path_params=None)

    def get_announcements_categories(self) -> StringListResponse:
        return self.get("/v1/announcements/categories", path_params=None)

    def get_announcements(
        self,
        *,
        symbols: list[str] | None = None,
        scrip_codes: list[str] | None = None,
        categories: list[str] | None = None,
        from_: str | None = None,
        to: str | None = None,
        detailed: bool | None = None,
        page: int | None = None,
        limit: int | None = None,
    ) -> PaginatedAnnouncementResponse:
        params = AnnouncementsListQueryParams(
            symbols=symbols,
            scrip_codes=scrip_codes,
            categories=categories,
            from_=from_,
            to=to,
            detailed=detailed,
            page=page,
            limit=limit,
        )
        return self.get("/v1/announcements", params=coerce_query_params(params), path_params=None)

    def get_announcements_attachments(
        self,
        *,
        ids: list[str],
    ) -> BatchAttachmentLookupResponse:
        params = DocumentIdsQueryParams(ids=ids)
        return self.get("/v1/announcements/attachments", params=coerce_query_params(params), path_params=None)

    def post_daily_summary(
        self,
        *,
        portfolio: list[Mapping[str, Any]],
    ) -> SummaryResponse:
        request_body = DailySummaryRequest(portfolio=portfolio).to_request_body()
        return self.post("/v1/daily-summary", body=request_body, path_params=None)

    def get_earnings(
        self,
        *,
        symbols: list[str] | None = None,
        scrip_codes: list[str] | None = None,
        from_: str | None = None,
        to: str | None = None,
        detailed: bool | None = None,
        page: int | None = None,
        limit: int | None = None,
    ) -> PaginatedEarningsResponse:
        params = EarningsQueryParams(
            symbols=symbols,
            scrip_codes=scrip_codes,
            from_=from_,
            to=to,
            detailed=detailed,
            page=page,
            limit=limit,
        )
        response = self.get("/v1/earnings", params=coerce_query_params(params), path_params=None)
        if isinstance(response, dict):
            data = response.get("data")
            if isinstance(data, list):
                response["data"] = [_normalize_earnings_item_payload(item) for item in data]
        return response

    def get_earnings_detail(
        self,
        *,
        quarter: str,
        symbol: str | None = None,
        scrip_code: str | None = None,
        detailed: bool | None = None,
    ) -> EarningsListItem | EarningsDetail:
        params = SymbolQuarterQueryParams(
            symbol=symbol,
            scrip_code=scrip_code,
            quarter=quarter,
            detailed=detailed,
        )
        response = self.get("/v1/earnings/detail", params=coerce_query_params(params), path_params=None)
        return _normalize_earnings_item_payload(response)

    def get_earnings_attachments(
        self,
        *,
        ids: list[str],
    ) -> BatchAttachmentLookupResponse:
        params = DocumentIdsQueryParams(ids=ids)
        return self.get("/v1/earnings/attachments", params=coerce_query_params(params), path_params=None)

    def get_concalls(
        self,
        *,
        symbols: list[str] | None = None,
        scrip_codes: list[str] | None = None,
        from_: str | None = None,
        to: str | None = None,
        detailed: bool | None = None,
        page: int | None = None,
        limit: int | None = None,
    ) -> PaginatedConcallResponse:
        params = ConcallsQueryParams(
            symbols=symbols,
            scrip_codes=scrip_codes,
            from_=from_,
            to=to,
            detailed=detailed,
            page=page,
            limit=limit,
        )
        return self.get("/v1/concalls", params=coerce_query_params(params), path_params=None)

    def get_concalls_detail(
        self,
        *,
        quarter: str,
        symbol: str | None = None,
        scrip_code: str | None = None,
        detailed: bool | None = None,
    ) -> Concall:
        params = SymbolQuarterQueryParams(
            symbol=symbol,
            scrip_code=scrip_code,
            quarter=quarter,
            detailed=detailed,
        )
        return self.get("/v1/concalls/detail", params=coerce_query_params(params), path_params=None)

    def get_concalls_transcript(
        self,
        *,
        quarter: str,
        symbol: str | None = None,
        scrip_code: str | None = None,
        detailed: bool | None = None,
    ) -> ConcallArtifactUrlsResponse:
        params = SymbolQuarterQueryParams(
            symbol=symbol,
            scrip_code=scrip_code,
            quarter=quarter,
            detailed=detailed,
        )
        return self.get("/v1/concalls/transcript", params=coerce_query_params(params), path_params=None)

    def post_concalls_transcripts(
        self,
        *,
        items: list[Mapping[str, str]],
    ) -> ConcallTranscriptBatchResponse:
        return self.post(
            "/v1/concalls/transcripts",
            body={"items": items},
            path_params=None,
        )

    def get_alerts(
        self,
        *,
        symbols: list[str] | None = None,
        scrip_codes: list[str] | None = None,
        type: list[str] | None = None,
        from_: str | None = None,
        to: str | None = None,
        important: bool | None = None,
        page: int | None = None,
        limit: int | None = None,
    ) -> PaginatedAlertResponse:
        params = AlertsQueryParams(
            symbols=symbols,
            scrip_codes=scrip_codes,
            type=type,
            from_=from_,
            to=to,
            important=important,
            page=page,
            limit=limit,
        )
        return self.get("/v1/alerts", params=coerce_query_params(params), path_params=None)

    def get_account(self) -> AccountDetailResponse:
        return self.get("/v1/account", path_params=None)

    def get_account_limits(self) -> AccountLimitsResponse:
        return self.get("/v1/account/limits", path_params=None)

    def get_account_usage(self) -> AccountUsageEnvelope:
        return self.get("/v1/account/usage", path_params=None)

    def get_account_ledger(
        self,
        *,
        limit: int | None = None,
    ) -> LedgerListResponse:
        params = AccountLedgerQueryParams(limit=limit)
        return self.get("/v1/account/ledger", params=coerce_query_params(params), path_params=None)

    def post_batch_jobs(
        self,
        *,
        file_name: str,
        file_bytes: bytes,
        display_name: str | None = None,
        metadata: str | None = None,
    ) -> BatchJobResponse:
        return self.post_batch_jobs_file(
            file_name=file_name,
            file_bytes=file_bytes,
            display_name=display_name,
            metadata=metadata,
        )

    def post_batch_jobs_file(
        self,
        *,
        file_name: str,
        file_bytes: bytes,
        display_name: str | None = None,
        metadata: str | None = None,
    ) -> BatchJobResponse:
        form_data: dict[str, str] = {}
        if display_name is not None:
            form_data["display_name"] = str(display_name)
        if metadata is not None:
            form_data["metadata"] = str(metadata)
        return self.request(
            "POST",
            "/v1/batch/jobs",
            data=form_data,
            files={
                "file": (
                    file_name,
                    file_bytes,
                    "application/jsonl",
                )
            },
        )

    def get_batch_jobs(
        self,
        *,
        limit: int | None = None,
    ) -> BatchJobListResponse:
        params = BatchJobsListQueryParams(limit=limit)
        return self.get("/v1/batch/jobs", params=coerce_query_params(params), path_params=None)

    def get_batch_jobs_job_id(self, *, job_id: str | int) -> BatchJobResponse:
        return self.get("/v1/batch/jobs/{job_id}", path_params={"job_id": job_id})

    def delete_batch_jobs_job_id(self, *, job_id: str | int) -> BatchJobCancelResponse:
        return self.delete("/v1/batch/jobs/{job_id}", path_params={"job_id": job_id})

    def get_batch_jobs_job_id_results(self, *, job_id: str | int) -> str:
        return self.get("/v1/batch/jobs/{job_id}/results", path_params={"job_id": job_id})

    def request_v1(
        self,
        method: str,
        path: str,
        *,
        json: JsonValue | None = None,
        params: Mapping[str, Any] | None = None,
    ) -> JsonValue | str | None:
        return self.request(method=method, path=f"/v1/{path.removeprefix('/')}", body=json, params=params)

    def websocket(self, **kwargs: AlphaWebSocketClientSessionOptions) -> AlphaWebSocketSession:
        from drishti_sdk.websocket import AlphaWebSocketSession

        return AlphaWebSocketSession(
            api_key=self._api_key,
            base_url=self._base_url,
            headers=self._extra_headers,
            **kwargs,
        )
