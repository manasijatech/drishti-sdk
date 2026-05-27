# market-stack-sdk (Python)

HTTP client for Alpha API (`/v1`).

## Install

From this directory:

```bash
pip install .
```

## Usage

```python
from market_stack_sdk import MarketStackClient

with MarketStackClient(api_key="YOUR_API_KEY") as client:
    # Defaults to https://developers.manasija.in
    from market_stack_sdk import AnnouncementsQueryParams, DailySummaryRequest, NewsQueryParams

    print(client.get_news(NewsQueryParams(symbols=["RELIANCE"], limit=10)))
    print(client.get_announcements(AnnouncementsQueryParams(symbols=["RELIANCE"])))
    print(
        client.get_earnings_detail(
            {"symbol": "MEDIASSIST", "quarter": "q4_26", "detailed": True}
        )
    )
    print(
        client.post_daily_summary(
            {
                "body": DailySummaryRequest(
                    portfolio=[{"symbol": "RELIANCE", "exposure": 10}]
                )
            }
        )
    )
```

All calls automatically send `X-API-Key` from the provided `api_key`.

Use `get`, `post`, `put`, `patch`, `delete`, or `request` for any public `/v1` endpoint.

## WebSocket (`/v1/ws`)

Requires the `websockets` package (installed with this SDK).

```python
import asyncio
from market_stack_sdk import MarketStackClient, SubscribeOptions

async def main() -> None:
    client = MarketStackClient(api_key="YOUR_API_KEY")
    async with client.websocket() as ws:
        await ws.subscribe(
            SubscribeOptions(product="announcements", symbols=["RELIANCE"], detailed=False)
        )
        async for event in ws.events():
            if event.kind == "subscribed":
                print("ready", event.product, event.tier)
            elif event.kind == "data":
                print(event.channel, event.data.get("symbol"))

asyncio.run(main())
```

Direct session import (without creating `MarketStackClient`):

```python
import asyncio
from market_stack_sdk import AlphaWebSocketSession, SubscribeOptions

async def main() -> None:
    async with AlphaWebSocketSession(api_key="YOUR_API_KEY", auto_reconnect=True) as ws:
        await ws.subscribe(
            SubscribeOptions(product="news", symbols=["RELIANCE"], detailed=True)
        )
        await ws.run()

asyncio.run(main())
```

Callback style:

```python
async with client.websocket(on_data=lambda e: print(e.data)) as ws:
    await ws.subscribe(product="alerts", symbols=["RELIANCE"])
    await ws.run()

# Optional resilience hooks:
# async with client.websocket(
#     auto_reconnect=True,
#     reconnect_initial_delay=1.0,
#     reconnect_max_delay=30.0,
#     on_reconnect_attempt=lambda attempt, delay, reason: print(attempt, delay, reason),
#     on_open=lambda reason: print("connected", reason),
#     on_close=lambda reason: print("closed", reason),
# ) as ws:
#     ...
```
