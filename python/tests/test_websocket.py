import asyncio

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
