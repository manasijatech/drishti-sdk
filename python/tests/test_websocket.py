import asyncio
import logging

from drishti_sdk.exceptions import DrishtiWebSocketError
from drishti_sdk.websocket import (
    DrishtiWebSocketSession,
    SubscribeOptions,
    parse_websocket_message,
)


def test_parse_subscribed_ack() -> None:
    event = parse_websocket_message(
        '{"status":"subscribed","product":"announcements","tier":"pro_500","full_feed":false,'
        '"symbols":["RELIANCE"],"detailed":true}'
    )
    assert event.kind == "subscribed"
    assert event.product == "announcements"
    assert event.tier == "pro_500"
    assert event.full_feed is False
    assert event.symbols == ["RELIANCE"]


def test_parse_data_event() -> None:
    event = parse_websocket_message('{"channel":"news","data":{"symbol":"INFY","headline":"Hi"}}')
    assert event.kind == "data"
    assert event.channel == "news"
    assert event.data["symbol"] == "INFY"


def test_parse_heartbeat_event() -> None:
    event = parse_websocket_message(
        '{"type":"heartbeat","sent_at":"2026-06-24T11:22:03.342193+00:00"}'
    )
    assert event.kind == "heartbeat"
    assert event.sent_at == "2026-06-24T11:22:03.342193+00:00"


def test_parse_subscribe_error() -> None:
    event = parse_websocket_message(
        '{"error":"WebSocket addon \'news\' is not enabled for this key","code":"forbidden"}'
    )
    assert event.kind == "error"
    assert event.code == "forbidden"


def test_subscribe_message_normalizes_symbols() -> None:
    message = SubscribeOptions(
        product="alerts",
        symbols=["reliance", "RELIANCE", " tcs "],
    ).to_message()
    assert message["symbols"] == ["RELIANCE", "TCS"]
    assert "categories" not in message


def test_subscribe_message_includes_announcement_categories() -> None:
    message = SubscribeOptions(
        product="announcements",
        categories=[" Dividend ", "Board Meeting", "Dividend"],
    ).to_message()
    assert message["categories"] == ["Dividend", "Board Meeting"]


def test_parse_subscribed_ack_includes_categories() -> None:
    event = parse_websocket_message(
        '{"status":"subscribed","product":"announcements","tier":"scale","full_feed":true,'
        '"symbols":[],"categories":["Dividend"],"detailed":true}'
    )
    assert event.kind == "subscribed"
    assert event.categories == ["Dividend"]


def test_block_deals_uses_hyphenated_wire_product() -> None:
    message = SubscribeOptions(product="block_deals").to_message()
    assert message["product"] == "block-deals"


def test_block_deals_wire_events_use_sdk_channel_name() -> None:
    subscribed = parse_websocket_message(
        '{"status":"subscribed","product":"block-deals","tier":"scale","full_feed":true}'
    )
    data = parse_websocket_message(
        '{"channel":"block-deals","data":{"id":"deal-1","symbol":"RELIANCE"}}'
    )

    assert subscribed.kind == "subscribed"
    assert subscribed.product == "block_deals"
    assert data.kind == "data"
    assert data.channel == "block_deals"


def test_lifecycle_logging_supports_format_arguments(caplog: object) -> None:
    session = DrishtiWebSocketSession(api_key="test-key")

    with caplog.at_level(logging.INFO, logger="drishti_sdk.websocket"):
        session._log_lifecycle("connected")
        session._log_lifecycle("subscribed %s attempt=%s", "announcements", 1)

    assert "[drishti-sdk] connected" in caplog.messages
    assert "[drishti-sdk] subscribed announcements attempt=1" in caplog.messages


def test_successful_subscription_ack_logs_without_retry() -> None:
    async def run() -> None:
        session = DrishtiWebSocketSession(api_key="test-key")
        session._ws = object()
        calls = 0

        async def acknowledge(options: SubscribeOptions):
            nonlocal calls
            calls += 1
            return parse_websocket_message(
                '{"status":"subscribed","product":"announcements","tier":"pro",'
                '"full_feed":false}'
            )

        session._send_subscribe_and_wait_for_ack = acknowledge  # type: ignore[method-assign]
        await session._send_subscribe_with_retry(SubscribeOptions(product="announcements"), "subscribe")

        assert calls == 1

    asyncio.run(run())


def test_subscription_failure_logging_preserves_original_error() -> None:
    async def run() -> None:
        session = DrishtiWebSocketSession(api_key="test-key", subscribe_max_attempts=1)
        session._ws = object()

        async def fail(options: SubscribeOptions):
            raise DrishtiWebSocketError("subscription denied", "forbidden")

        session._send_subscribe_and_wait_for_ack = fail  # type: ignore[method-assign]
        try:
            await session._send_subscribe_with_retry(SubscribeOptions(product="announcements"), "subscribe")
        except DrishtiWebSocketError as exc:
            assert str(exc) == "subscription denied"
            assert exc.code == "forbidden"
        else:
            raise AssertionError("subscription failure should be raised")

    asyncio.run(run())


def test_disconnect_rejects_pending_subscription_without_cancelling_it() -> None:
    async def run() -> None:
        session = DrishtiWebSocketSession(api_key="test-key")
        future = asyncio.get_running_loop().create_future()
        session._pending_subscribe = ("news", future)

        session._fail_pending_subscribe("WebSocket connection closed")

        assert session._pending_subscribe is None
        assert not future.cancelled()
        try:
            await future
        except DrishtiWebSocketError as exc:
            assert str(exc) == "WebSocket connection closed"
        else:
            raise AssertionError("pending subscription should fail when the socket closes")

    asyncio.run(run())


def test_channel_handlers_receive_typed_payloads() -> None:
    async def run() -> None:
        session = DrishtiWebSocketSession(api_key="test-key")
        announcements: list[dict[str, object]] = []
        alerts: list[dict[str, object]] = []
        all_data: list[str] = []

        session.on("announcements", announcements.append)
        session.on("alerts", alerts.append)
        session.on("data", lambda event: all_data.append(event.channel) if event.kind == "data" else None)

        await session._dispatch(
            parse_websocket_message(
                '{"channel":"announcements","data":{"symbol":"RELIANCE","title":"Results"}}'
            )
        )
        await session._dispatch(
            parse_websocket_message('{"channel":"alerts","data":{"symbol":"TCS","message":"Price move"}}')
        )

        assert announcements == [{"symbol": "RELIANCE", "title": "Results"}]
        assert alerts == [{"symbol": "TCS", "message": "Price move"}]
        assert all_data == ["announcements", "alerts"]

    asyncio.run(run())


def test_channel_listener_helpers_and_off() -> None:
    async def run() -> None:
        session = DrishtiWebSocketSession(api_key="test-key")
        announcements: list[dict[str, object]] = []

        session.on_announcements(announcements.append)
        await session._dispatch(
            parse_websocket_message(
                '{"channel":"announcements","data":{"symbol":"RELIANCE","title":"Results"}}'
            )
        )
        assert announcements == [{"symbol": "RELIANCE", "title": "Results"}]

        session.off("announcements", announcements.append)
        await session._dispatch(
            parse_websocket_message(
                '{"channel":"announcements","data":{"symbol":"TCS","title":"Update"}}'
            )
        )
        assert announcements == [{"symbol": "RELIANCE", "title": "Results"}]

    asyncio.run(run())


def test_permanent_subscribe_rejection_is_not_retried() -> None:
    async def run() -> None:
        session = DrishtiWebSocketSession(api_key="test-key", subscribe_max_attempts=10)
        session._ws = object()
        calls = 0

        async def reject(options: SubscribeOptions):
            nonlocal calls
            calls += 1
            raise DrishtiWebSocketError("WebSocket addon 'announcements' is not enabled for this key", "forbidden")

        session._send_subscribe_and_wait_for_ack = reject  # type: ignore[method-assign]
        try:
            await session._send_subscribe_with_retry(SubscribeOptions(product="announcements"), "subscribe")
        except DrishtiWebSocketError as exc:
            assert exc.code == "forbidden"
        else:
            raise AssertionError("permanent rejection should be raised")
        assert calls == 1

    asyncio.run(run())


class _FakeSocket:
    def __init__(self) -> None:
        self.closed = False
        self._closed_event = asyncio.Event()

    async def recv(self) -> str:
        await self._closed_event.wait()
        raise asyncio.CancelledError

    async def send(self, _message: str) -> None:
        return None

    async def close(self) -> None:
        self.closed = True
        self._closed_event.set()


def _patch_connect(monkeypatch: object, sockets: list[_FakeSocket]) -> None:
    import drishti_sdk.websocket as module

    async def connect(*_args: object, **_kwargs: object) -> _FakeSocket:
        socket = _FakeSocket()
        sockets.append(socket)
        return socket

    async def no_sleep(_delay: float) -> None:
        return None

    monkeypatch.setattr(module.websockets, "connect", connect)  # type: ignore[attr-defined]
    monkeypatch.setattr(module, "sleep", no_sleep)  # type: ignore[attr-defined]


def test_failed_connection_setup_closes_the_socket(monkeypatch: object) -> None:
    async def run() -> None:
        sockets: list[_FakeSocket] = []
        _patch_connect(monkeypatch, sockets)
        session = DrishtiWebSocketSession(api_key="test-key")
        session._subscriptions = {"news": SubscribeOptions(product="news", symbols=["INFY"])}
        attempts = 0

        async def resubscribe() -> bool:
            nonlocal attempts
            attempts += 1
            # Let the receive task start, as it does in production, before failing.
            await asyncio.sleep(0)
            if attempts >= 3:
                session._manually_closed = True
            raise DrishtiWebSocketError("Subscribe acknowledgement timed out")

        session._resubscribe_all = resubscribe  # type: ignore[method-assign]
        await session._maintain_connection("initial")

        assert len(sockets) == 3
        assert all(socket.closed for socket in sockets)

    asyncio.run(run())


def test_rejected_subscriptions_keep_reconnecting_with_backoff(monkeypatch: object) -> None:
    import drishti_sdk.websocket as module

    async def run() -> None:
        sockets: list[_FakeSocket] = []
        _patch_connect(monkeypatch, sockets)
        delays: list[float] = []

        async def record_sleep(delay: float) -> None:
            delays.append(delay)

        monkeypatch.setattr(module, "sleep", record_sleep)  # type: ignore[attr-defined]
        session = DrishtiWebSocketSession(
            api_key="test-key",
            reconnect_initial_delay=1.0,
            reconnect_backoff_multiplier=2.0,
            reconnect_max_delay=30.0,
            reconnect_jitter_ratio=0,
        )
        announcements = SubscribeOptions(product="announcements")
        session._subscriptions = {"announcements": announcements}
        attempts = 0

        async def reject(options: SubscribeOptions):
            nonlocal attempts
            attempts += 1
            if attempts >= 4:
                session._manually_closed = True
            raise DrishtiWebSocketError("not enabled", "forbidden")

        session._send_subscribe_and_wait_for_ack = reject  # type: ignore[method-assign]
        await asyncio.wait_for(session._maintain_connection("initial"), timeout=2)

        assert attempts == 4
        assert len(sockets) == 4
        assert all(socket.closed for socket in sockets)
        assert session._subscriptions == {"announcements": announcements}
        assert delays == [2.0, 4.0, 8.0]

    asyncio.run(run())
