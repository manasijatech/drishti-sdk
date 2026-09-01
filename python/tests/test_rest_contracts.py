from typing import Any

from drishti_sdk.client import DrishtiClient
from drishti_sdk.params import BatchSummaryInputLine, DailySummaryRequest


class RecordingClient(DrishtiClient):
    def __init__(self) -> None:
        self.calls: list[tuple[str, dict[str, Any] | None]] = []

    def get(self, path: str, *, params=None, path_params=None):
        self.calls.append((path, params))
        return "symbol,company_name\nTCS,Tata Consultancy Services\n"


def test_symbol_universe_helper_uses_format_query_parameter() -> None:
    client = RecordingClient()

    client.get_symbols(format="json")

    assert client.calls == [("/v1/symbols", {"format": "json"})]


def test_daily_summary_preserves_context_window_days() -> None:
    request = DailySummaryRequest.model_validate(
        {"symbols": ["TCS"], "mode": "news_context", "context_window_days": 7}
    )

    assert request.to_request_body()["context_window_days"] == 7


def test_batch_summary_serializes_context_window_days() -> None:
    line = BatchSummaryInputLine(
        custom_id="window-7",
        symbols=["TCS"],
        mode="news_context",
        context_window_days=7,
    )

    assert '"context_window_days":7' in line.to_jsonl_line()
