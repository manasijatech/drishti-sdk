from __future__ import annotations

import time
from typing import TYPE_CHECKING, Any, Mapping, TypeVar, cast

if TYPE_CHECKING:
    from drishti_sdk.websocket import (
        DrishtiWebSocketClientSessionOptions,
        DrishtiWebSocketSession,
    )

import httpx

from drishti_sdk.exceptions import DrishtiApiError
from drishti_sdk.params import (
    AccountLedgerQueryParams,
    AlertsQueryParams,
    AnnouncementsListQueryParams,
    BatchJobsListQueryParams,
    ConcallsIndexQueryParams,
    ConcallsQueryParams,
    ContentRetentionHeader,
    DailySummaryPortfolioItem,
    DailySummaryRequest,
    DocumentIdsQueryParams,
    EarningsIndexQueryParams,
    EarningsQueryParams,
    NewsQueryParams,
    NewsSentiment,
    SymbolMetadataQueryParams,
    SymbolQuarterQueryParams,
    SymbolQuarterTranscriptQueryParams,
    UpcomingConcallsQueryParams,
    UpcomingEarningsQueryParams,
    coerce_query_params,
    parse_batch_result_jsonl,
)
from drishti_sdk.types import (
    AccountDetailResponse,
    AccountLimitsResponse,
    AccountUsageEnvelope,
    Alert,
    AnnouncementDetail,
    BATCH_JOB_TERMINAL_STATUSES,
    BatchAttachmentLookupResponse,
    BatchJobCancelResponse,
    BatchJobListResponse,
    BatchJobResponse,
    BatchResultLine,
    Concall,
    ConcallArtifactUrlsResponse,
    ConcallTranscriptBatchResponse,
    JsonValue,
    LedgerListResponse,
    PaginatedAlertResponse,
    PaginatedAnnouncementResponse,
    PaginatedConcallResponse,
    PaginatedEarningsResponse,
    PaginatedLightweightIndexResponse,
    PaginatedUpcomingConcallResponse,
    PaginatedUpcomingEarningsResponse,
    EarningsDetail,
    EarningsListItem,
    PaginatedNewsResponse,
    AnnouncementCategoriesResponse,
    SummaryResponse,
    SymbolMetadataResponse,
    UpcomingConcall,
    UpcomingEarningsListItem,
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
        retry_max_retries: int = 2,
        retry_initial_delay: float = 0.3,
        retry_max_delay: float = 5.0,
        retry_multiplier: float = 2.0,
        retry_on_statuses: tuple[int, ...] = (408, 409, 425, 429, 500, 502, 503, 504),
    ) -> None:
        if not api_key.strip():
            raise ValueError("DrishtiClient requires a non-empty api_key")
        self._base_url = (base_url or DEFAULT_BASE_URL).rstrip("/")
        self._api_key = api_key
        self._extra_headers = dict(headers) if headers else {}
        self._client = httpx.Client(timeout=timeout)
        self._retry_max_retries = max(0, int(retry_max_retries))
        self._retry_initial_delay = max(0.0, float(retry_initial_delay))
        self._retry_max_delay = max(0.0, float(retry_max_delay))
        self._retry_multiplier = max(1.0, float(retry_multiplier))
        self._retry_on_statuses = tuple(int(status) for status in retry_on_statuses)

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

    @staticmethod
    def _content_retention_headers(
        content_retention: ContentRetentionHeader | None,
    ) -> dict[str, str] | None:
        if content_retention is None:
            return None
        return {"X-Alpha-Content-Retention": content_retention}

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
        retry_max_retries: int | None = None,
        retry_initial_delay: float | None = None,
        retry_max_delay: float | None = None,
        retry_multiplier: float | None = None,
        retry_on_statuses: tuple[int, ...] | None = None,
    ) -> TResponse:
        sub = self._build_path(path, path_params).removeprefix("/")
        url = f"{self._base_url}/{sub}"
        request_headers = self._merge_headers()
        if headers:
            request_headers.update(headers)
        max_retries = self._retry_max_retries if retry_max_retries is None else max(0, int(retry_max_retries))
        initial_delay = self._retry_initial_delay if retry_initial_delay is None else max(0.0, float(retry_initial_delay))
        max_delay = self._retry_max_delay if retry_max_delay is None else max(0.0, float(retry_max_delay))
        multiplier = self._retry_multiplier if retry_multiplier is None else max(1.0, float(retry_multiplier))
        retry_statuses = self._retry_on_statuses if retry_on_statuses is None else tuple(int(status) for status in retry_on_statuses)

        attempts = max_retries + 1
        last_error: Exception | None = None
        for attempt in range(1, attempts + 1):
            try:
                response = self._client.request(
                    method.upper(),
                    url,
                    json=body,
                    data=data,
                    files=files,
                    params=params,
                    headers=request_headers,
                )
                if response.status_code in retry_statuses and attempt < attempts:
                    delay = min(max_delay, initial_delay * (multiplier ** (attempt - 1)))
                    time.sleep(delay)
                    continue
                return cast(TResponse, self._parse_response(response))
            except httpx.RequestError as exc:
                last_error = exc
                if attempt >= attempts:
                    raise
                delay = min(max_delay, initial_delay * (multiplier ** (attempt - 1)))
                time.sleep(delay)
        if last_error is not None:
            raise last_error
        raise RuntimeError("Request failed after retries")

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

    def get_announcements_categories(self) -> AnnouncementCategoriesResponse:
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
        important: bool | None = None,
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
            important=important,
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
        request: DailySummaryRequest | Mapping[str, Any] | None = None,
        portfolio: list[Mapping[str, Any]] | None = None,
        content_retention: ContentRetentionHeader | None = None,
    ) -> SummaryResponse:
        resolved_request = self._resolve_daily_summary_request(request=request, portfolio=portfolio)
        headers = self._content_retention_headers(content_retention)
        return self.request(
            "POST",
            "/v1/daily-summary/",
            body=resolved_request.to_request_body(),
            headers=headers,
        )

    @staticmethod
    def _resolve_daily_summary_request(
        *,
        request: DailySummaryRequest | Mapping[str, Any] | None,
        portfolio: list[Mapping[str, Any]] | None,
    ) -> DailySummaryRequest:
        if request is not None:
            if isinstance(request, DailySummaryRequest):
                return request
            return DailySummaryRequest.model_validate(dict(request))
        if portfolio is not None:
            return DailySummaryRequest(
                portfolio=[
                    DailySummaryPortfolioItem.model_validate(dict(item))
                    for item in portfolio
                ]
            )
        raise ValueError("Either request or portfolio must be provided")

    def get_earnings_index(
        self,
        *,
        symbols: list[str] | None = None,
        scrip_codes: list[str] | None = None,
        from_: str | None = None,
        to: str | None = None,
        quarter: str | None = None,
        page: int | None = None,
        limit: int | None = None,
    ) -> PaginatedLightweightIndexResponse:
        params = EarningsIndexQueryParams(
            symbols=symbols,
            scrip_codes=scrip_codes,
            from_=from_,
            to=to,
            quarter=quarter,
            page=page,
            limit=limit,
        )
        return self.get("/v1/earnings/index", params=coerce_query_params(params), path_params=None)

    def get_earnings(
        self,
        *,
        ids: list[str] | None = None,
        symbols: list[str] | None = None,
        scrip_codes: list[str] | None = None,
        from_: str | None = None,
        to: str | None = None,
        quarter: str | None = None,
        detailed: bool | None = None,
        page: int | None = None,
        limit: int | None = None,
    ) -> PaginatedEarningsResponse:
        params = EarningsQueryParams(
            ids=ids,
            symbols=symbols,
            scrip_codes=scrip_codes,
            from_=from_,
            to=to,
            quarter=quarter,
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

    def get_upcoming_earnings(
        self,
        *,
        symbols: list[str] | None = None,
        scrip_codes: list[str] | None = None,
        page: int | None = None,
        limit: int | None = None,
    ) -> PaginatedUpcomingEarningsResponse:
        params = UpcomingEarningsQueryParams(
            symbols=symbols,
            scrip_codes=scrip_codes,
            page=page,
            limit=limit,
        )
        return self.get("/v1/earnings/upcoming", params=coerce_query_params(params), path_params=None)

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

    def get_concalls_index(
        self,
        *,
        symbols: list[str] | None = None,
        scrip_codes: list[str] | None = None,
        from_: str | None = None,
        to: str | None = None,
        quarter: str | None = None,
        page: int | None = None,
        limit: int | None = None,
    ) -> PaginatedLightweightIndexResponse:
        params = ConcallsIndexQueryParams(
            symbols=symbols,
            scrip_codes=scrip_codes,
            from_=from_,
            to=to,
            quarter=quarter,
            page=page,
            limit=limit,
        )
        return self.get("/v1/concalls/index", params=coerce_query_params(params), path_params=None)

    def get_upcoming_concalls(
        self,
        *,
        symbols: list[str] | None = None,
        scrip_codes: list[str] | None = None,
        detailed: bool | None = None,
        page: int | None = None,
        limit: int | None = None,
    ) -> PaginatedUpcomingConcallResponse:
        params = UpcomingConcallsQueryParams(
            symbols=symbols,
            scrip_codes=scrip_codes,
            detailed=detailed,
            page=page,
            limit=limit,
        )
        return self.get("/v1/concalls/upcoming", params=coerce_query_params(params), path_params=None)

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
    ) -> ConcallArtifactUrlsResponse:
        params = SymbolQuarterTranscriptQueryParams(
            symbol=symbol,
            scrip_code=scrip_code,
            quarter=quarter,
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
        content_retention: ContentRetentionHeader | None = None,
    ) -> BatchJobResponse:
        return self.post_batch_jobs_file(
            file_name=file_name,
            file_bytes=file_bytes,
            display_name=display_name,
            metadata=metadata,
            content_retention=content_retention,
        )

    def post_batch_jobs_file(
        self,
        *,
        file_name: str,
        file_bytes: bytes,
        display_name: str | None = None,
        metadata: str | None = None,
        content_retention: ContentRetentionHeader | None = None,
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
            headers=self._content_retention_headers(content_retention),
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

    def get_batch_jobs_job_id_results_parsed(self, *, job_id: str | int) -> list[BatchResultLine]:
        content = self.get_batch_jobs_job_id_results(job_id=job_id)
        if not isinstance(content, str):
            raise TypeError("Expected batch job results to be JSONL text")
        return parse_batch_result_jsonl(content)

    def wait_for_batch_job_completion(
        self,
        *,
        job_id: str | int,
        poll_interval: float = 2.0,
        timeout: float = 300.0,
        terminal_statuses: tuple[str, ...] = BATCH_JOB_TERMINAL_STATUSES,
    ) -> BatchJobResponse:
        started = time.monotonic()
        terminal = {status.lower() for status in terminal_statuses}
        while True:
            job = self.get_batch_jobs_job_id(job_id=job_id)
            status = str(job.get("status", "")).lower()
            if status in terminal:
                return job
            if time.monotonic() - started >= timeout:
                raise TimeoutError(f"Timed out waiting for batch job {job_id} to complete")
            time.sleep(max(0.0, poll_interval))

    def submit_batch_job_and_wait(
        self,
        *,
        file_name: str,
        file_bytes: bytes,
        display_name: str | None = None,
        metadata: str | None = None,
        content_retention: ContentRetentionHeader | None = None,
        poll_interval: float = 2.0,
        timeout: float = 300.0,
        terminal_statuses: tuple[str, ...] = BATCH_JOB_TERMINAL_STATUSES,
    ) -> BatchJobResponse:
        job = self.post_batch_jobs_file(
            file_name=file_name,
            file_bytes=file_bytes,
            display_name=display_name,
            metadata=metadata,
            content_retention=content_retention,
        )
        return self.wait_for_batch_job_completion(
            job_id=job["id"],
            poll_interval=poll_interval,
            timeout=timeout,
            terminal_statuses=terminal_statuses,
        )

    def request_v1(
        self,
        method: str,
        path: str,
        *,
        json: JsonValue | None = None,
        params: Mapping[str, Any] | None = None,
    ) -> JsonValue | str | None:
        return self.request(method=method, path=f"/v1/{path.removeprefix('/')}", body=json, params=params)

    def websocket(self, **kwargs: DrishtiWebSocketClientSessionOptions) -> DrishtiWebSocketSession:
        from drishti_sdk.websocket import DrishtiWebSocketSession

        return DrishtiWebSocketSession(
            api_key=self._api_key,
            base_url=self._base_url,
            headers=self._extra_headers,
            **kwargs,
        )
