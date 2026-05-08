from __future__ import annotations

import os
from typing import Any, Mapping, TypeVar, cast

import httpx

from market_stack_sdk.exceptions import MarketStackApiError
from market_stack_sdk.types import (
    AccountDetailResponse,
    AccountLimitsResponse,
    AccountListResponse,
    AccountUsageEnvelope,
    AdminAccountDashboardResponse,
    AdminApiKeyDetailResponse,
    Alert,
    AnnouncementDetail,
    ApiKeyAdminListResponse,
    ApiKeyAdminPayload,
    ApiKeyCreateResponse,
    ApiKeyDeleteResponse,
    ApiKeyGetResponse,
    ApiKeyListResponse,
    BatchJobCancelResponse,
    BatchJobListResponse,
    BatchJobResponse,
    CacheClearResponse,
    Concall,
    JsonValue,
    LedgerEntry,
    LedgerListResponse,
    MarketReport,
    MigrateResponse,
    PaginatedAlertResponse,
    PaginatedAnnouncementResponse,
    PaginatedConcallResponse,
    PaginatedMarketReportResponse,
    PresignedUrlResponse,
    SummaryResponse,
    UsageHistoryEnvelope,
    UsageResponse,
)

DEFAULT_BASE_URL = os.getenv("ALPHA_API_BASE_URL", "http://127.0.0.1:8000")
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

    def get_announcements(self, params: Mapping[str, Any] | None = None) -> PaginatedAnnouncementResponse:
        return self.get("/v1/announcements", params=params, path_params=None)

    def get_announcements_announcement_id(self, announcement_id: str | int, params: Mapping[str, Any] | None = None) -> AnnouncementDetail:
        return self.get("/v1/announcements/{announcement_id}", params=params, path_params={"announcement_id": announcement_id})

    def get_announcements_announcement_id_attachment(self, announcement_id: str | int, params: Mapping[str, Any] | None = None) -> PresignedUrlResponse:
        return self.get("/v1/announcements/{announcement_id}/attachment", params=params, path_params={"announcement_id": announcement_id})

    def post_daily_summary(self, body: JsonValue | None = None, params: Mapping[str, Any] | None = None) -> SummaryResponse:
        return self.post("/v1/daily-summary", body=body, params=params, path_params=None)

    def get_earnings(self, params: Mapping[str, Any] | None = None) -> PaginatedAnnouncementResponse:
        return self.get("/v1/earnings", params=params, path_params=None)

    def get_earnings_earnings_id(self, earnings_id: str | int, params: Mapping[str, Any] | None = None) -> AnnouncementDetail:
        return self.get("/v1/earnings/{earnings_id}", params=params, path_params={"earnings_id": earnings_id})

    def get_earnings_earnings_id_attachment(self, earnings_id: str | int, params: Mapping[str, Any] | None = None) -> PresignedUrlResponse:
        return self.get("/v1/earnings/{earnings_id}/attachment", params=params, path_params={"earnings_id": earnings_id})

    def get_concalls(self, params: Mapping[str, Any] | None = None) -> PaginatedConcallResponse:
        return self.get("/v1/concalls", params=params, path_params=None)

    def get_concalls_concall_id(self, concall_id: str | int, params: Mapping[str, Any] | None = None) -> Concall:
        return self.get("/v1/concalls/{concall_id}", params=params, path_params={"concall_id": concall_id})

    def get_concalls_concall_id_transcript(self, concall_id: str | int, params: Mapping[str, Any] | None = None) -> PresignedUrlResponse:
        return self.get("/v1/concalls/{concall_id}/transcript", params=params, path_params={"concall_id": concall_id})

    def get_alerts(self, params: Mapping[str, Any] | None = None) -> PaginatedAlertResponse:
        return self.get("/v1/alerts", params=params, path_params=None)

    def get_alerts_alert_id(self, alert_id: str | int, params: Mapping[str, Any] | None = None) -> Alert:
        return self.get("/v1/alerts/{alert_id}", params=params, path_params={"alert_id": alert_id})

    def get_reports(self, params: Mapping[str, Any] | None = None) -> PaginatedMarketReportResponse:
        return self.get("/v1/reports", params=params, path_params=None)

    def get_reports_report_id(self, report_id: str | int, params: Mapping[str, Any] | None = None) -> MarketReport:
        return self.get("/v1/reports/{report_id}", params=params, path_params={"report_id": report_id})

    def get_account(self, params: Mapping[str, Any] | None = None) -> AccountDetailResponse:
        return self.get("/v1/account", params=params, path_params=None)

    def get_account_limits(self, params: Mapping[str, Any] | None = None) -> AccountLimitsResponse:
        return self.get("/v1/account/limits", params=params, path_params=None)

    def get_account_usage(self, params: Mapping[str, Any] | None = None) -> AccountUsageEnvelope:
        return self.get("/v1/account/usage", params=params, path_params=None)

    def get_account_ledger(self, params: Mapping[str, Any] | None = None) -> LedgerListResponse:
        return self.get("/v1/account/ledger", params=params, path_params=None)

    def post_admin_accounts(self, body: JsonValue | None = None, params: Mapping[str, Any] | None = None) -> AccountDetailResponse:
        return self.post("/v1/admin/accounts", body=body, params=params, path_params=None)

    def get_admin_accounts(self, params: Mapping[str, Any] | None = None) -> AccountListResponse:
        return self.get("/v1/admin/accounts", params=params, path_params=None)

    def get_admin_accounts_account_id(self, account_id: str | int, params: Mapping[str, Any] | None = None) -> AccountDetailResponse:
        return self.get("/v1/admin/accounts/{account_id}", params=params, path_params={"account_id": account_id})

    def patch_admin_accounts_account_id(self, account_id: str | int, body: JsonValue | None = None, params: Mapping[str, Any] | None = None) -> AccountDetailResponse:
        return self.patch("/v1/admin/accounts/{account_id}", body=body, params=params, path_params={"account_id": account_id})

    def post_admin_accounts_account_id_credits(self, account_id: str | int, body: JsonValue | None = None, params: Mapping[str, Any] | None = None) -> dict[str, LedgerEntry]:
        return self.post("/v1/admin/accounts/{account_id}/credits", body=body, params=params, path_params={"account_id": account_id})

    def get_admin_accounts_account_id_ledger(self, account_id: str | int, params: Mapping[str, Any] | None = None) -> LedgerListResponse:
        return self.get("/v1/admin/accounts/{account_id}/ledger", params=params, path_params={"account_id": account_id})

    def post_admin_accounts_account_id_api_keys(self, account_id: str | int, body: JsonValue | None = None, params: Mapping[str, Any] | None = None) -> dict[str, ApiKeyAdminPayload]:
        return self.post("/v1/admin/accounts/{account_id}/api-keys", body=body, params=params, path_params={"account_id": account_id})

    def patch_admin_accounts_account_id_api_keys_api_key(self, account_id: str | int, api_key: str | int, body: JsonValue | None = None, params: Mapping[str, Any] | None = None) -> dict[str, ApiKeyAdminPayload]:
        return self.patch("/v1/admin/accounts/{account_id}/api-keys/{api_key}", body=body, params=params, path_params={"account_id": account_id, "api_key": api_key})

    def get_admin_accounts_account_id_api_keys(self, account_id: str | int, params: Mapping[str, Any] | None = None) -> ApiKeyAdminListResponse:
        return self.get("/v1/admin/accounts/{account_id}/api-keys", params=params, path_params={"account_id": account_id})

    def get_admin_accounts_account_id_dashboard(self, account_id: str | int, params: Mapping[str, Any] | None = None) -> AdminAccountDashboardResponse:
        return self.get("/v1/admin/accounts/{account_id}/dashboard", params=params, path_params={"account_id": account_id})

    def get_admin_accounts_account_id_usage(self, account_id: str | int, params: Mapping[str, Any] | None = None) -> UsageHistoryEnvelope:
        return self.get("/v1/admin/accounts/{account_id}/usage", params=params, path_params={"account_id": account_id})

    def get_admin_accounts_account_id_api_keys_api_key(self, account_id: str | int, api_key: str | int, params: Mapping[str, Any] | None = None) -> AdminApiKeyDetailResponse:
        return self.get("/v1/admin/accounts/{account_id}/api-keys/{api_key}", params=params, path_params={"account_id": account_id, "api_key": api_key})

    def get_admin_accounts_account_id_api_keys_api_key_usage(self, account_id: str | int, api_key: str | int, params: Mapping[str, Any] | None = None) -> UsageHistoryEnvelope:
        return self.get("/v1/admin/accounts/{account_id}/api-keys/{api_key}/usage", params=params, path_params={"account_id": account_id, "api_key": api_key})

    def post_api_keys(self, body: JsonValue | None = None, params: Mapping[str, Any] | None = None) -> ApiKeyCreateResponse:
        return self.post("/v1/api-keys", body=body, params=params, path_params=None)

    def get_api_keys_api_key(self, api_key: str | int, params: Mapping[str, Any] | None = None) -> ApiKeyGetResponse:
        return self.get("/v1/api-keys/{api_key}", params=params, path_params={"api_key": api_key})

    def patch_api_keys_api_key(self, api_key: str | int, body: JsonValue | None = None, params: Mapping[str, Any] | None = None) -> ApiKeyGetResponse:
        return self.patch("/v1/api-keys/{api_key}", body=body, params=params, path_params={"api_key": api_key})

    def delete_api_keys_api_key(self, api_key: str | int, params: Mapping[str, Any] | None = None) -> ApiKeyDeleteResponse:
        return self.delete("/v1/api-keys/{api_key}", params=params, path_params={"api_key": api_key})

    def get_api_keys(self, params: Mapping[str, Any] | None = None) -> ApiKeyListResponse:
        return self.get("/v1/api-keys", params=params, path_params=None)

    def post_api_keys_migrate(self, body: JsonValue | None = None, params: Mapping[str, Any] | None = None) -> MigrateResponse:
        return self.post("/v1/api-keys/migrate", body=body, params=params, path_params=None)

    def delete_api_keys_cache(self, params: Mapping[str, Any] | None = None) -> CacheClearResponse:
        return self.delete("/v1/api-keys/cache", params=params, path_params=None)

    def get_api_keys_api_key_usage(self, api_key: str | int, params: Mapping[str, Any] | None = None) -> UsageResponse:
        return self.get("/v1/api-keys/{api_key}/usage", params=params, path_params={"api_key": api_key})

    def post_batch_jobs(self, body: JsonValue | None = None, params: Mapping[str, Any] | None = None) -> BatchJobResponse:
        return self.post("/v1/batch/jobs", body=body, params=params, path_params=None)

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
