import asyncio

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
