from __future__ import annotations

from typing import Any


class MarketStackApiError(Exception):
    """Raised when the API returns a non-success HTTP status."""

    def __init__(self, status_code: int, body: Any) -> None:
        self.status_code = status_code
        self.body = body
        super().__init__(f"Market-Stack API error {status_code}: {body}")


class MarketStackWebSocketError(Exception):
    """Raised when the WebSocket connection fails or closes unexpectedly."""
