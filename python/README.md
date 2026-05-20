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
    print(client.get_earnings_detail(symbol="MEDIASSIST", quarter="q4_26", params={"detailed": True}))
    print(
        client.post_daily_summary(
            DailySummaryRequest(portfolio=[{"symbol": "RELIANCE", "exposure": 10}])
        )
    )
```

All calls automatically send `X-API-Key` from the provided `api_key`.

Use `get`, `post`, `put`, `patch`, `delete`, or `request` for any public `/v1` endpoint.
