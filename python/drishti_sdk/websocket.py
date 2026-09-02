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
    "block_deals",
    "announcements",
    "earnings",
    "concalls",
    "alerts",
]

DRISHTI_WS_PRODUCTS: tuple[DrishtiWebSocketProduct, ...] = (
    "news",
    "block_deals",
    "announcements",
    "earnings",
    "concalls",
    "alerts",
)


def _normalize_websocket_product(value: str) -> str:
    return "block_deals" if value == "block-deals" else value


def _wire_websocket_product(value: DrishtiWebSocketProduct) -> str:
    return "block-deals" if value == "block_deals" else value


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
    subscribe_ack_timeout: float
    subscribe_max_attempts: int
    subscribe_retry_initial_delay: float
    subscribe_retry_max_delay: float
    subscribe_retry_backoff_multiplier: float
    enable_lifecycle_logging: bool
    on_subscribed: WebSocketHandler | None
    on_data: WebSocketHandler | None
    on_news: ChannelDataHandler | None
    on_block_deals: ChannelDataHandler | None
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
            "product": _wire_websocket_product(self.product),
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
class HeartbeatEvent:
    sent_at: str
    kind: Literal["heartbeat"] = "heartbeat"


@dataclass(frozen=True)
class RawEvent:
    payload: dict[str, Any]
    kind: Literal["raw"] = "raw"


WebSocketEvent = SubscribedEvent | DataEvent | ErrorEvent | HeartbeatEvent | RawEvent


def parse_websocket_message(raw: str) -> WebSocketEvent:
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError:
        return ErrorEvent(message="Invalid JSON")
    if not isinstance(payload, dict):
        return ErrorEvent(message="Expected a JSON object")
    if payload.get("type") == "heartbeat":
        return HeartbeatEvent(sent_at=str(payload.get("sent_at") or ""))
    if payload.get("status") == "subscribed":
        symbols = payload.get("symbols")
        symbol_list = (
            [str(item) for item in symbols]
            if isinstance(symbols, list)
            else []
        )
        return SubscribedEvent(
            product=_normalize_websocket_product(str(payload.get("product") or "")),
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
            return DataEvent(channel=_normalize_websocket_product(str(channel)), data=data)
        return DataEvent(channel=_normalize_websocket_product(str(channel)), data={"raw": data})
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
        subscribe_ack_timeout: float = 10.0,
        subscribe_max_attempts: int = 10,
        subscribe_retry_initial_delay: float = 1.0,
        subscribe_retry_max_delay: float = 30.0,
        subscribe_retry_backoff_multiplier: float = 2.0,
        enable_lifecycle_logging: bool = True,
        on_subscribed: WebSocketHandler | None = None,
        on_data: WebSocketHandler | None = None,
        on_news: ChannelDataHandler | None = None,
        on_block_deals: ChannelDataHandler | None = None,
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
        self._subscribe_ack_timeout = max(1.0, float(subscribe_ack_timeout))
        self._subscribe_max_attempts = max(1, int(subscribe_max_attempts))
        self._subscribe_retry_initial_delay = max(0.1, float(subscribe_retry_initial_delay))
        self._subscribe_retry_max_delay = max(
            self._subscribe_retry_initial_delay,
            float(subscribe_retry_max_delay),
        )
        self._subscribe_retry_backoff_multiplier = max(1.0, float(subscribe_retry_backoff_multiplier))
        self._enable_lifecycle_logging = bool(enable_lifecycle_logging)
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
        self._receive_task: asyncio.Task[None] | None = None
        self._event_queue: asyncio.Queue[WebSocketEvent] = asyncio.Queue()
        self._subscribe_lock = asyncio.Lock()
        self._pending_subscribe: tuple[str, asyncio.Future[SubscribedEvent]] | None = None
        self._last_heartbeat_at: float | None = None
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
        if on_block_deals is not None:
            self.on("block_deals", on_block_deals)
        if on_error is not None:
            self.on("error", on_error)
        if on_message is not None:
            self.on("message", on_message)

    @property
    def connected(self) -> bool:
        return self._ws is not None

    @property
    def last_heartbeat_received_at(self) -> float | None:
        return self._last_heartbeat_at

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

    def on_block_deals(self, handler: ChannelDataHandler) -> None:
        self._add_channel_listener("block_deals", handler)

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
            if self._receive_task is not None and not self._receive_task.done():
                await self._receive_task
                self._receive_task = None
                self._ws = None
                self._clear_pending_subscribe()
                reason = "connection closed"
                continue
            attempt += 1
            if attempt > 1:
                await self._emit_reconnect_attempt(attempt - 1, delay, reason)
                await sleep(self._jitter(delay))
            elif reason not in {"context", "subscribe", "events", "initial"}:
                self._log_lifecycle(f"reconnecting ({reason})")
            try:
                if self._ws is None:
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
                self._receive_task = asyncio.create_task(
                    self._receive_until_closed(),
                    name="drishti-ws-receive",
                )
                self._log_lifecycle(
                    f"connected; resubscribing {len(self._subscriptions)} product(s)",
                )
                await self._resubscribe_all()
                await self._emit_lifecycle(self._on_open, "connected")
                attempt = 0
                delay = self._reconnect_initial_delay
                await self._receive_task
                self._receive_task = None
            except Exception as exc:
                self._fail_pending_subscribe(f"WebSocket connection failed: {exc}")
                if self._receive_task is not None and not self._receive_task.done():
                    self._receive_task.cancel()
                    with contextlib.suppress(asyncio.CancelledError):
                        await self._receive_task
                self._receive_task = None
                if self._ws is not None:
                    with contextlib.suppress(Exception):
                        await self._ws.close()
                self._ws = None
                self._log_lifecycle(f"connection setup failed: {exc}")
                if delay >= self._reconnect_max_delay and attempt >= self._reconnect_warn_after_attempts:
                    await self._emit_reconnect_warning(attempt, reason, exc)
                    attempt = 0
                delay = min(delay * self._reconnect_backoff_multiplier, self._reconnect_max_delay)

    async def _receive_until_closed(self) -> None:
        if self._ws is None:
            return
        try:
            while not self._manually_closed:
                raw = await self._ws.recv()
                if not isinstance(raw, str):
                    raw = raw.decode() if isinstance(raw, bytes) else str(raw)
                event = parse_websocket_message(raw)
                if isinstance(event, HeartbeatEvent):
                    self._last_heartbeat_at = asyncio.get_running_loop().time()
                    await self._dispatch(event)
                    continue
                resolved_pending = self._resolve_pending_subscribe(event)
                if isinstance(event, (SubscribedEvent, ErrorEvent)) and resolved_pending:
                    await self._dispatch(event)
                    await self._event_queue.put(event)
                    continue
                if resolved_pending:
                    continue
                await self._dispatch(event)
                await self._event_queue.put(event)
        except websockets.exceptions.ConnectionClosed as exc:
            await self._emit_lifecycle(self._on_close, f"closed: {exc}")
        finally:
            self._ws = None
            self._fail_pending_subscribe("WebSocket connection closed")

    async def close(self) -> None:
        self._manually_closed = True
        self._clear_pending_subscribe()
        if self._receive_task is not None and not self._receive_task.done():
            self._receive_task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await self._receive_task
            self._receive_task = None
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
        await self._send_subscribe_with_retry(normalized, "subscribe")

    def _log_lifecycle(self, message: str) -> None:
        if not self._enable_lifecycle_logging:
            return
        logger.info("[drishti-sdk] %s", message)

    def _clear_pending_subscribe(self) -> None:
        if self._pending_subscribe is None:
            return
        _, future = self._pending_subscribe
        if not future.done():
            future.cancel()
        self._pending_subscribe = None

    def _fail_pending_subscribe(self, message: str) -> None:
        if self._pending_subscribe is None:
            return
        _, future = self._pending_subscribe
        self._pending_subscribe = None
        if not future.done():
            future.set_exception(DrishtiWebSocketError(message))

    def _resolve_pending_subscribe(self, event: WebSocketEvent) -> bool:
        if self._pending_subscribe is None:
            return False
        product, future = self._pending_subscribe
        if isinstance(event, SubscribedEvent) and event.product == product:
            if not future.done():
                future.set_result(event)
            return True
        if isinstance(event, ErrorEvent):
            if not future.done():
                future.set_exception(DrishtiWebSocketError(event.message, event.code))
            return True
        return False

    async def _send_subscribe_and_wait_for_ack(self, options: SubscribeOptions) -> SubscribedEvent:
        if self._ws is None:
            raise DrishtiWebSocketError("WebSocket is not connected")
        loop = asyncio.get_running_loop()
        future: asyncio.Future[SubscribedEvent] = loop.create_future()
        self._pending_subscribe = (options.product, future)
        try:
            await self._ws.send(json.dumps(options.to_message()))
            return await asyncio.wait_for(future, timeout=self._subscribe_ack_timeout)
        finally:
            if self._pending_subscribe is not None and self._pending_subscribe[1] is future:
                self._pending_subscribe = None

    async def _send_subscribe_with_retry(self, options: SubscribeOptions, reason: str) -> None:
        attempt = 0
        delay = self._subscribe_retry_initial_delay
        async with self._subscribe_lock:
            while attempt < self._subscribe_max_attempts:
                if self._ws is None:
                    raise DrishtiWebSocketError("WebSocket is not connected")
                attempt += 1
                try:
                    ack = await self._send_subscribe_and_wait_for_ack(options)
                    self._log_lifecycle(
                        "subscribed %s tier=%s full_feed=%s (%s, attempt %s)",
                        options.product,
                        ack.tier,
                        ack.full_feed,
                        reason,
                        attempt,
                    )
                    return
                except Exception as exc:
                    code = getattr(exc, "code", None)
                    suffix = f" [{code}]" if code else ""
                    self._log_lifecycle(
                        "subscribe failed product=%s attempt=%s/%s (%s): %s%s",
                        options.product,
                        attempt,
                        self._subscribe_max_attempts,
                        reason,
                        exc,
                        suffix,
                    )
                    if self._ws is None:
                        raise DrishtiWebSocketError(str(exc), code) from exc
                    if attempt >= self._subscribe_max_attempts:
                        if isinstance(exc, DrishtiWebSocketError):
                            raise exc
                        raise DrishtiWebSocketError(str(exc), code) from exc
                    await sleep(self._jitter(delay))
                    delay = min(
                        delay * self._subscribe_retry_backoff_multiplier,
                        self._subscribe_retry_max_delay,
                    )

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
        if self._ws is None or not self._subscriptions:
            return
        for options in self._subscriptions.values():
            await self._send_subscribe_with_retry(options, "reconnect")

    async def _emit_reconnect_attempt(self, attempt: int, delay: float, reason: str) -> None:
        handler = self._on_reconnect_attempt
        if handler is None:
            self._log_lifecycle(
                f"reconnect attempt={attempt} delay={delay:.1f}s reason={reason}",
            )
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
