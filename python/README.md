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
    print(client.get("/v1/news"))
    print(client.get("/v1/earnings/{earnings_id}", path_params={"earnings_id": "67c2f8a1b2c3d4e5f6a7b8d0"}))
    print(
        client.post(
            "/v1/daily-summary",
            body={"portfolio": [{"symbol": "RELIANCE", "exposure": 10}]},
        )
    )
    # Dedicated helpers are available for every public route:
    print(client.get_news())
    print(client.get_announcements())
    print(client.get_earnings_earnings_id(earnings_id="67c2f8a1b2c3d4e5f6a7b8d0"))
```

All calls automatically send `X-API-Key` from the provided `api_key`.

Use `get`, `post`, `put`, `patch`, `delete`, or `request` for any public `/v1` endpoint.
