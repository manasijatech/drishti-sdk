"""Regression tests: batch results are JSONL (``application/jsonl``) and must not be JSON-decoded."""

from __future__ import annotations

import httpx
import pytest

from drishti_sdk import DrishtiClient

JSONL = '{"custom_id":"a","status":"succeeded"}\n{"custom_id":"b","status":"failed"}\n'


@pytest.fixture
def client(monkeypatch: pytest.MonkeyPatch) -> DrishtiClient:
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.url.path == "/v1/batch/jobs/job_1/results"
        return httpx.Response(200, headers={"content-type": "application/jsonl"}, content=JSONL.encode())

    real_client = httpx.Client
    monkeypatch.setattr(
        "drishti_sdk.client.httpx.Client",
        lambda **kwargs: real_client(transport=httpx.MockTransport(handler), **kwargs),
    )
    return DrishtiClient(api_key="test-key", base_url="https://api.test")


def test_results_returns_raw_jsonl_text(client: DrishtiClient) -> None:
    assert client.get_batch_jobs_job_id_results(job_id="job_1") == JSONL


def test_results_parsed_splits_jsonl_lines(client: DrishtiClient) -> None:
    assert client.get_batch_jobs_job_id_results_parsed(job_id="job_1") == [
        {"custom_id": "a", "status": "succeeded"},
        {"custom_id": "b", "status": "failed"},
    ]
