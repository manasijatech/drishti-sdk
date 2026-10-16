"""Runs the shared cross-SDK conformance fixtures in ../../conformance against the Python SDK.

Fixture format is documented in conformance/README.md. The JS SDK runs the same fixtures from
js/tests/conformance.test.mjs, so a behaviour change must be made in every SDK to keep both green.
"""

from __future__ import annotations

import dataclasses
import json
from email.parser import BytesParser
from email.policy import default as default_email_policy
from pathlib import Path
from typing import Any
from urllib.parse import parse_qsl

import httpx
import pytest

from drishti_sdk import (
    ALERT_TYPES,
    BATCH_JOB_STATUSES,
    BATCH_JOB_TERMINAL_STATUSES,
    DEFAULT_BASE_URL,
    DrishtiApiError,
    DrishtiClient,
    SubscribeOptions,
    build_batch_input_jsonl,
    build_websocket_url,
    parse_batch_result_jsonl,
    parse_websocket_message,
)
from drishti_sdk.params import BatchSummaryInputLine
from drishti_sdk.websocket import DRISHTI_WS_PRODUCTS, PERMANENT_SUBSCRIBE_ERROR_CODES

CONFORMANCE_DIR = Path(__file__).resolve().parents[2] / "conformance"
TEST_API_KEY = "test-key"
TEST_BASE_URL = "https://api.test"


def _cases(subdir: str) -> list[Any]:
    params = []
    for path in sorted((CONFORMANCE_DIR / subdir).glob("*.json")):
        for case in json.loads(path.read_text(encoding="utf-8"))["cases"]:
            params.append(pytest.param(case, id=case["id"]))
    return params


# --- REST -----------------------------------------------------------------------------------


def _python_kwargs(method: str, args: dict[str, Any]) -> dict[str, Any]:
    """Translate canonical fixture argument names to this SDK's keyword arguments."""
    kwargs: dict[str, Any] = {}
    for key, value in args.items():
        if key == "from":
            kwargs["from_"] = value
        elif key == "file_content":
            kwargs["file_bytes"] = value.encode("utf-8")
        elif key == "poll_interval_ms":
            kwargs["poll_interval"] = value / 1000
        elif key == "body" and method == "post_daily_summary":
            kwargs["request"] = value
        else:
            kwargs[key] = value
    return kwargs


def _build_response(spec: dict[str, Any], request: httpx.Request) -> httpx.Response:
    if spec.get("network_error"):
        raise httpx.ConnectError("simulated network error", request=request)
    status = spec.get("status", 200)
    if "json" in spec:
        content = json.dumps(spec["json"]).encode("utf-8")
        content_type = spec.get("content_type", "application/json")
    else:
        content = spec.get("text", "").encode("utf-8")
        content_type = spec.get("content_type", "application/json")
    headers = {"content-type": content_type} if status != 204 else {}
    return httpx.Response(status, headers=headers, content=content if status != 204 else b"")


def _run_rest_case(
    case: dict[str, Any], monkeypatch: pytest.MonkeyPatch
) -> tuple[list[httpx.Request], Any]:
    responses = case.get("responses") or [case.get("response", {"status": 200, "json": {}})]
    seen: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        request.read()
        seen.append(request)
        if len(seen) > len(responses):
            pytest.fail(f"unexpected extra request #{len(seen)}: {request.method} {request.url}")
        return _build_response(responses[len(seen) - 1], request)

    real_client = httpx.Client
    monkeypatch.setattr(
        "drishti_sdk.client.httpx.Client",
        lambda **kwargs: real_client(transport=httpx.MockTransport(handler), **kwargs),
    )
    client_cfg = case.get("client", {})
    result: Any = None
    error: BaseException | None = None
    try:
        client = DrishtiClient(
            api_key=client_cfg.get("api_key", TEST_API_KEY),
            base_url=client_cfg.get("base_url", TEST_BASE_URL),
            headers=client_cfg.get("headers"),
            retry_max_retries=client_cfg.get("max_retries", 2),
            retry_initial_delay=0,
            retry_max_delay=0,
        )
        call = case["call"]
        method = getattr(client, call["method"])
        kwargs = _python_kwargs(call["method"], call.get("args", {}))
        result = method(*call.get("positional", []), **kwargs)
    except Exception as exc:  # noqa: BLE001 - asserted against the fixture below
        error = exc
    return seen, (result, error)


def _query_multimap(request: httpx.Request) -> dict[str, list[str]]:
    out: dict[str, list[str]] = {}
    for key, value in parse_qsl(request.url.query.decode("utf-8"), keep_blank_values=True):
        out.setdefault(key, []).append(value)
    return out


def _multipart(request: httpx.Request) -> dict[str, Any]:
    header = b"Content-Type: " + request.headers["content-type"].encode() + b"\r\n\r\n"
    raw = header + request.content
    message = BytesParser(policy=default_email_policy).parsebytes(raw)
    fields: dict[str, str] = {}
    files: dict[str, dict[str, str]] = {}
    for part in message.iter_parts():
        name = part.get_param("name", header="content-disposition")
        content = part.get_payload(decode=True).decode("utf-8")
        filename = part.get_filename()
        if filename is not None:
            files[name] = {"filename": filename, "content": content}
        else:
            fields[name] = content
    return {"fields": fields, "files": files}


def _assert_request(request: httpx.Request, expected: dict[str, Any]) -> None:
    assert request.method == expected["method"]
    assert request.url.raw_path.split(b"?", 1)[0].decode("ascii") == expected["path"]
    assert _query_multimap(request) == expected.get("query", {})
    for name, value in expected.get("headers", {}).items():
        assert request.headers.get(name) == value, f"header {name}"
    if expected.get("json_body") is not None:
        assert json.loads(request.content) == expected["json_body"]
    if "multipart" in expected:
        assert _multipart(request) == expected["multipart"]


@pytest.mark.parametrize("case", _cases("rest"))
def test_rest_conformance(case: dict[str, Any], monkeypatch: pytest.MonkeyPatch) -> None:
    seen, (result, error) = _run_rest_case(case, monkeypatch)

    expected_requests = case.get("expect_requests")
    if expected_requests is None and "expect_request" in case:
        expected_requests = [case["expect_request"]]
    if expected_requests is not None:
        assert len(seen) == len(expected_requests)
        for request, expected in zip(seen, expected_requests):
            _assert_request(request, expected)
    if "expect_request_count" in case:
        assert len(seen) == case["expect_request_count"]

    if "expect_error" in case:
        expected_error = case["expect_error"]
        assert error is not None, f"expected {expected_error['type']}, got result {result!r}"
        if expected_error["type"] == "api_error":
            assert isinstance(error, DrishtiApiError)
            assert error.status_code == expected_error["status"]
            assert error.body == expected_error["body"]
        else:
            assert isinstance(error, ValueError)
        return
    if error is not None:
        raise error
    if "expect_result" in case:
        assert result == case["expect_result"]


# --- Helpers --------------------------------------------------------------------------------


@pytest.mark.parametrize("case", _cases("helpers"))
def test_helper_conformance(case: dict[str, Any]) -> None:
    if case["function"] == "build_batch_input_jsonl":
        lines = [BatchSummaryInputLine.model_validate(line) for line in case["input"]]
        if "expect_error" in case:
            with pytest.raises(ValueError):
                build_batch_input_jsonl(lines)
            return
        text = build_batch_input_jsonl(lines).decode("utf-8")
        assert text.endswith("\n")
        assert [json.loads(line) for line in text.splitlines()] == case["expect_lines"]
    elif case["function"] == "parse_batch_result_jsonl":
        assert parse_batch_result_jsonl(case["input"]) == case["expect_result"]
    else:
        pytest.fail(f"unknown helper {case['function']}")


# --- WebSocket ------------------------------------------------------------------------------


def _ws_cases(name: str) -> list[Any]:
    data = json.loads((CONFORMANCE_DIR / "websocket" / f"{name}.json").read_text(encoding="utf-8"))
    return [pytest.param(case, id=case["id"]) for case in data["cases"]]


@pytest.mark.parametrize("case", _ws_cases("parse"))
def test_websocket_parse_conformance(case: dict[str, Any]) -> None:
    assert dataclasses.asdict(parse_websocket_message(case["raw"])) == case["expect"]


@pytest.mark.parametrize("case", _ws_cases("subscribe"))
def test_websocket_subscribe_conformance(case: dict[str, Any]) -> None:
    assert SubscribeOptions(**case["options"]).to_message() == case["expect"]


@pytest.mark.parametrize("case", _ws_cases("url"))
def test_websocket_url_conformance(case: dict[str, Any]) -> None:
    api_key = case.get("api_key", "")
    url = build_websocket_url(
        case["base_url"], include_api_key_query=bool(api_key), api_key=api_key
    )
    assert url == case["expect"]


# --- Constants ------------------------------------------------------------------------------


def test_constants_conformance() -> None:
    expected = json.loads((CONFORMANCE_DIR / "constants.json").read_text(encoding="utf-8"))
    assert DEFAULT_BASE_URL == expected["default_base_url"]
    assert list(DRISHTI_WS_PRODUCTS) == expected["ws_products"]
    assert sorted(PERMANENT_SUBSCRIBE_ERROR_CODES) == expected["permanent_subscribe_error_codes"]
    assert list(ALERT_TYPES) == expected["alert_types"]
    assert list(BATCH_JOB_STATUSES) == expected["batch_job_statuses"]
    assert list(BATCH_JOB_TERMINAL_STATUSES) == expected["batch_job_terminal_statuses"]
