from __future__ import annotations

import asyncio
import contextlib
import inspect
import json
import logging
import random
from asyncio import sleep
from collections.abc import AsyncIterator, Awaitable, Callable, Sequence
from dataclasses import dataclass
from typing import Any, Literal, TypedDict, TypeAlias, cast

import websockets

from drishti_sdk.exceptions import DrishtiWebSocketError

logger = logging.getLogger(__name__)

_DEFAULT_BASE_URL = "https://developers.manasija.in"

DrishtiWebSocketProduct: TypeAlias = Literal[
    "news",
    "announcements",
    "earnings",
    "concalls",
    "alerts",
]

DRISHTI_WS_PRODUCTS: tuple[DrishtiWebSocketProduct, ...] = (
    "news",
    "announcements",
    "earnings",
    "concalls",
    "alerts",
)

WebSocketHandler: TypeAlias = Callable[["WebSocketEvent"], None | Awaitable[None]]
ChannelDataHandler: TypeAlias = Callable[[dict[str, Any]], None | Awaitable[None]]
ReconnectAttemptHandler: TypeAlias = Callable[[int, float, str], None | Awaitable[None]]
ReconnectWarningHandler: TypeAlias = Callable[[int, str], None | Awaitable[None]]
LifecycleHandler: TypeAlias = Callable[[str], None | Awaitable[None]]


class DrishtiWebSocketClientSessionOptions(TypedDict, total=False):
    """Keyword options for ``DrishtiClient.websocket()`` (API key and base URL come from the HTTP client)."""

    ping_interval: float | None
    ping_timeout: float | None
    open_timeout: float | None
    close_timeout: float | None
    max_queue: int | None
    reconnect_initial_delay: float
    reconnect_max_delay: float
    reconnect_backoff_multiplier: float
    reconnect_jitter_ratio: float
    reconnect_warn_after_attempts: int
    on_subscribed: WebSocketHandler | None
    on_data: WebSocketHandler | None
    on_news: ChannelDataHandler | None
    on_announcements: ChannelDataHandler | None
    on_earnings: ChannelDataHandler | None
    on_concalls: ChannelDataHandler | None
    on_alerts: ChannelDataHandler | None
    on_error: WebSocketHandler | None
    on_message: WebSocketHandler | None
    on_open: LifecycleHandler | None
    on_close: LifecycleHandler | None
    on_reconnect_attempt: ReconnectAttemptHandler | None
    on_reconnect_warning: ReconnectWarningHandler | None


def build_websocket_url(base_url: str, *, include_api_key_query: bool = False, api_key: str = "") -> str:
    base = base_url.rstrip("/")
    ws_base = base.replace("https://", "wss://").replace("http://", "ws://")
    url = f"{ws_base}/v1/ws"
    if include_api_key_query and api_key:
        return f"{url}?api_key={api_key}"
    return url


def _normalize_symbols(symbols: Sequence[str]) -> list[str]:
    seen: set[str] = set()
    normalized: list[str] = []
    for symbol in symbols:
        token = str(symbol).strip().upper()
        if not token or token in seen:
            continue
        seen.add(token)
        normalized.append(token)
    return normalized


@dataclass(frozen=True)
class SubscribeOptions:
    product: DrishtiWebSocketProduct
    symbols: Sequence[str] = ()
    detailed: bool = True

    def to_message(self) -> dict[str, Any]:
        return {
            "op": "subscribe",
            "product": self.product,
            "symbols": _normalize_symbols(self.symbols),
            "detailed": self.detailed,
        }


@dataclass(frozen=True)
class SubscribedEvent:
    kind: Literal["subscribed"] = "subscribed"
    product: str = ""
    tier: str = ""
    full_feed: bool = False
    symbols: list[str] | None = None
    detailed: bool = True


@dataclass(frozen=True)
class DataEvent:
    channel: DrishtiWebSocketProduct | str
    data: dict[str, Any]
    kind: Literal["data"] = "data"


@dataclass(frozen=True)
class ErrorEvent:
    message: str
    code: str | None = None
    kind: Literal["error"] = "error"


@dataclass(frozen=True)
class RawEvent:
    payload: dict[str, Any]
    kind: Literal["raw"] = "raw"


WebSocketEvent = SubscribedEvent | DataEvent | ErrorEvent | RawEvent


def parse_websocket_message(raw: str) -> WebSocketEvent:
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError:
        return ErrorEvent(message="Invalid JSON")
    if not isinstance(payload, dict):
        return ErrorEvent(message="Expected a JSON object")
    if payload.get("status") == "subscribed":
        symbols = payload.get("symbols")
        symbol_list = (
            [str(item) for item in symbols]
            if isinstance(symbols, list)
            else []
        )
        return SubscribedEvent(
            product=str(payload.get("product") or ""),
            tier=str(payload.get("tier") or ""),
            full_feed=bool(payload.get("full_feed")),
            symbols=symbol_list,
            detailed=bool(payload.get("detailed", True)),
        )
    if "error" in payload:
        code = payload.get("code")
        return ErrorEvent(
            message=str(payload.get("error") or "Unknown error"),
            code=str(code) if code is not None else None,
        )
    channel = payload.get("channel")
    if channel is not None:
        data = payload.get("data")
        if isinstance(data, dict):
            return DataEvent(channel=str(channel), data=data)
        return DataEvent(channel=str(channel), data={"raw": data})
    return RawEvent(payload=payload)


class DrishtiWebSocketSession:
    """Async WebSocket client for Drishti API ``/v1/ws``.

    The session connects automatically on the first async operation and keeps
    retrying in the background when the connection drops. Call
    :meth:`subscribe` and iterate :meth:`events` without a manual connect step.
    """

    def __init__(
        self,
        *,
        api_key: str,
        base_url: str | None = None,
        headers: dict[str, str] | None = None,
        ping_interval: float | None = 20,
        ping_timeout: float | None = 20,
        open_timeout: float | None = 20,
        close_timeout: float | None = 10,
        max_queue: int | None = 1000,
        reconnect_initial_delay: float = 1.0,
        reconnect_max_delay: float = 30.0,
        reconnect_backoff_multiplier: float = 2.0,
        reconnect_jitter_ratio: float = 0.2,
        reconnect_warn_after_attempts: int = 10,
        on_subscribed: WebSocketHandler | None = None,
        on_data: WebSocketHandler | None = None,
        on_news: ChannelDataHandler | None = None,
        on_announcements: ChannelDataHandler | None = None,
        on_earnings: ChannelDataHandler | None = None,
        on_concalls: ChannelDataHandler | None = None,
        on_alerts: ChannelDataHandler | None = None,
        on_error: WebSocketHandler | None = None,
        on_message: WebSocketHandler | None = None,
        on_open: LifecycleHandler | None = None,
        on_close: LifecycleHandler | None = None,
        on_reconnect_attempt: ReconnectAttemptHandler | None = None,
        on_reconnect_warning: ReconnectWarningHandler | None = None,
    ) -> None:
        if not api_key.strip():
            raise ValueError("DrishtiWebSocketSession requires a non-empty api_key")
        self._api_key = api_key
        self._base_url = (base_url or _DEFAULT_BASE_URL).rstrip("/")
        self._extra_headers = dict(headers) if headers else {}
        self._ping_interval = ping_interval
        self._ping_timeout = ping_timeout
        self._open_timeout = open_timeout
        self._close_timeout = close_timeout
        self._max_queue = max_queue
        self._reconnect_initial_delay = max(0.1, float(reconnect_initial_delay))
        self._reconnect_max_delay = max(self._reconnect_initial_delay, float(reconnect_max_delay))
        self._reconnect_backoff_multiplier = max(1.0, float(reconnect_backoff_multiplier))
        self._reconnect_jitter_ratio = min(1.0, max(0.0, float(reconnect_jitter_ratio)))
        self._reconnect_warn_after_attempts = max(1, int(reconnect_warn_after_attempts))
        self._ws: Any = None
        self._manually_closed = False
        self._handlers: dict[str, list[WebSocketHandler]] = {}
        self._channel_handlers: dict[DrishtiWebSocketProduct, list[ChannelDataHandler]] = {}
        self._subscriptions: dict[DrishtiWebSocketProduct, SubscribeOptions] = {}
        self._on_open = on_open
        self._on_close = on_close
        self._on_reconnect_attempt = on_reconnect_attempt
        self._on_reconnect_warning = on_reconnect_warning
        self._connection_task: asyncio.Task[None] | None = None
        self._event_queue: asyncio.Queue[WebSocketEvent] = asyncio.Queue()
        if on_subscribed is not None:
            self.on("subscribed", on_subscribed)
        if on_data is not None:
            self.on("data", on_data)
        if on_news is not None:
            self.on("news", on_news)
        if on_announcements is not None:
            self.on("announcements", on_announcements)
        if on_earnings is not None:
            self.on("earnings", on_earnings)
        if on_concalls is not None:
            self.on("concalls", on_concalls)
        if on_alerts is not None:
            self.on("alerts", on_alerts)
        if on_error is not None:
            self.on("error", on_error)
        if on_message is not None:
            self.on("message", on_message)

    @property
    def connected(self) -> bool:
        return self._ws is not None

    def on(
        self,
        event_name: str,
        handler: WebSocketHandler | ChannelDataHandler,
    ) -> None:
        if event_name in DRISHTI_WS_PRODUCTS:
            channel = cast(DrishtiWebSocketProduct, event_name)
            self._add_channel_listener(channel, cast(ChannelDataHandler, handler))
            return
        self._add_event_listener(event_name, cast(WebSocketHandler, handler))

    def off(
        self,
        event_name: str,
        handler: WebSocketHandler | ChannelDataHandler,
    ) -> None:
        if event_name in DRISHTI_WS_PRODUCTS:
            channel = cast(DrishtiWebSocketProduct, event_name)
            self._remove_channel_listener(channel, cast(ChannelDataHandler, handler))
            return
        self._remove_event_listener(event_name, cast(WebSocketHandler, handler))

    def on_news(self, handler: ChannelDataHandler) -> None:
        self._add_channel_listener("news", handler)

    def on_announcements(self, handler: ChannelDataHandler) -> None:
        self._add_channel_listener("announcements", handler)

    def on_earnings(self, handler: ChannelDataHandler) -> None:
        self._add_channel_listener("earnings", handler)

    def on_concalls(self, handler: ChannelDataHandler) -> None:
        self._add_channel_listener("concalls", handler)

    def on_alerts(self, handler: ChannelDataHandler) -> None:
        self._add_channel_listener("alerts", handler)

    def _add_event_listener(self, event_name: str, handler: WebSocketHandler) -> None:
        self._handlers.setdefault(event_name, []).append(handler)

    def _remove_event_listener(self, event_name: str, handler: WebSocketHandler) -> None:
        handlers = self._handlers.get(event_name, [])
        next_handlers = [item for item in handlers if item != handler]
        if next_handlers:
            self._handlers[event_name] = next_handlers
            return
        self._handlers.pop(event_name, None)

    def _add_channel_listener(
        self,
        channel: DrishtiWebSocketProduct,
        handler: ChannelDataHandler,
    ) -> None:
        self._channel_handlers.setdefault(channel, []).append(handler)

    def _remove_channel_listener(
        self,
        channel: DrishtiWebSocketProduct,
        handler: ChannelDataHandler,
    ) -> None:
        handlers = self._channel_handlers.get(channel, [])
        next_handlers = [item for item in handlers if item != handler]
        if next_handlers:
            self._channel_handlers[channel] = next_handlers
            return
        self._channel_handlers.pop(channel, None)

    def _merge_headers(self) -> dict[str, str]:
        headers = dict(self._extra_headers)
        headers["X-API-Key"] = self._api_key
        return headers

    def _start_connection_maintenance(self, reason: str) -> None:
        if self._manually_closed:
            return
        if self._connection_task is not None and not self._connection_task.done():
            return
        self._connection_task = asyncio.create_task(
            self._maintain_connection(reason),
            name="drishti-ws-connection",
        )

    async def _maintain_connection(self, reason: str) -> None:
        attempt = 0
        delay = self._reconnect_initial_delay
        while not self._manually_closed:
            if self.connected:
                await self._receive_until_closed()
                reason = "connection closed"
                continue
            attempt += 1
            if attempt > 1:
                await self._emit_reconnect_attempt(attempt - 1, delay, reason)
                await sleep(self._jitter(delay))
            try:
                await self._open_connection()
                attempt = 0
                delay = self._reconnect_initial_delay
            except Exception as exc:
                if delay >= self._reconnect_max_delay and attempt >= self._reconnect_warn_after_attempts:
                    await self._emit_reconnect_warning(attempt, reason, exc)
                    attempt = 0
                delay = min(delay * self._reconnect_backoff_multiplier, self._reconnect_max_delay)

    async def _open_connection(self) -> None:
        if self._ws is not None:
            return
        url = build_websocket_url(self._base_url)
        self._ws = await websockets.connect(
            url,
            additional_headers=self._merge_headers(),
            open_timeout=self._open_timeout,
            ping_interval=self._ping_interval,
            ping_timeout=self._ping_timeout,
            close_timeout=self._close_timeout,
            max_queue=self._max_queue,
        )
        await self._resubscribe_all()
        await self._emit_lifecycle(self._on_open, "connected")

    async def _receive_until_closed(self) -> None:
        if self._ws is None:
            return
        try:
            while not self._manually_closed:
                raw = await self._ws.recv()
                if not isinstance(raw, str):
                    raw = raw.decode() if isinstance(raw, bytes) else str(raw)
                event = parse_websocket_message(raw)
                await self._dispatch(event)
                await self._event_queue.put(event)
        except websockets.exceptions.ConnectionClosed as exc:
            await self._emit_lifecycle(self._on_close, f"closed: {exc}")
        finally:
            self._ws = None

    async def close(self) -> None:
        self._manually_closed = True
        if self._ws is not None:
            try:
                await self._ws.close()
                await self._emit_lifecycle(self._on_close, "closed")
            finally:
                self._ws = None
        if self._connection_task is not None and not self._connection_task.done():
            self._connection_task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await self._connection_task
            self._connection_task = None

    async def __aenter__(self) -> DrishtiWebSocketSession:
        self._start_connection_maintenance("context")
        return self

    async def __aexit__(self, *args: object) -> None:
        await self.close()

    async def subscribe(self, options: SubscribeOptions | DrishtiWebSocketProduct, **kwargs: Any) -> None:
        if isinstance(options, str):
            options = SubscribeOptions(
                product=cast(DrishtiWebSocketProduct, options),
                symbols=kwargs.get("symbols") or (),
                detailed=bool(kwargs.get("detailed", True)),
            )
        normalized = SubscribeOptions(
            product=options.product,
            symbols=_normalize_symbols(options.symbols),
            detailed=bool(options.detailed),
        )
        self._subscriptions[normalized.product] = normalized
        self._start_connection_maintenance("subscribe")
        if self._ws is None:
            return
        await self._ws.send(json.dumps(normalized.to_message()))

    async def _dispatch(self, event: WebSocketEvent) -> None:
        names = [event.kind, "message"]
        for name in names:
            for handler in self._handlers.get(name, []):
                result = handler(event)
                if inspect.isawaitable(result):
                    await result
        if not isinstance(event, DataEvent):
            return
        channel = event.channel
        if channel not in DRISHTI_WS_PRODUCTS:
            return
        for handler in self._channel_handlers.get(cast(DrishtiWebSocketProduct, channel), []):
            result = handler(event.data)
            if inspect.isawaitable(result):
                await result

    async def events(self) -> AsyncIterator[WebSocketEvent]:
        self._start_connection_maintenance("events")
        while not self._manually_closed:
            try:
                event = await asyncio.wait_for(self._event_queue.get(), timeout=0.5)
            except asyncio.TimeoutError:
                continue
            yield event

    async def _resubscribe_all(self) -> None:
        if self._ws is None:
            return
        for options in self._subscriptions.values():
            await self._ws.send(json.dumps(options.to_message()))

    async def _emit_reconnect_attempt(self, attempt: int, delay: float, reason: str) -> None:
        handler = self._on_reconnect_attempt
        if handler is None:
            return
        result = handler(attempt, delay, reason)
        if inspect.isawaitable(result):
            await result

    async def _emit_reconnect_warning(self, attempt: int, reason: str, exc: Exception) -> None:
        handler = self._on_reconnect_warning
        if handler is not None:
            result = handler(attempt, reason)
            if inspect.isawaitable(result):
                await result
            return
        logger.warning(
            "Drishti WebSocket still unable to connect after %s attempts (%s): %s",
            attempt,
            reason,
            exc,
        )

    async def _emit_lifecycle(self, handler: LifecycleHandler | None, reason: str) -> None:
        if handler is None:
            return
        result = handler(reason)
        if inspect.isawaitable(result):
            await result

    def _jitter(self, delay: float) -> float:
        if self._reconnect_jitter_ratio <= 0:
            return delay
        spread = delay * self._reconnect_jitter_ratio
        return max(0.0, delay + random.uniform(-spread, spread))


async def stream_product(
    *,
    api_key: str,
    product: DrishtiWebSocketProduct,
    symbols: Sequence[str] | None = None,
    detailed: bool = True,
    base_url: str | None = None,
    headers: dict[str, str] | None = None,
) -> AsyncIterator[dict[str, Any]]:
    session = DrishtiWebSocketSession(api_key=api_key, base_url=base_url, headers=headers)
    try:
        await session.subscribe(SubscribeOptions(product=product, symbols=symbols or (), detailed=detailed))
        async for event in session.events():
            if isinstance(event, DataEvent):
                yield event.data
    finally:
        await session.close()
