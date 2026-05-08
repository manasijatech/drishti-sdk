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
    # Defaults to ALPHA_API_BASE_URL (or http://127.0.0.1:8000)
    print(client.get("/v1/announcements"))
    print(client.get("/v1/announcements/{announcement_id}", path_params={"announcement_id": "67c2f8a1b2c3d4e5f6a7b8c9"}))
    print(
        client.post(
            "/v1/daily-summary/generate",
            body={"portfolio": [{"symbol": "RELIANCE", "qty": 10}]},
            params={"page": 1, "limit": 20},
        )
    )
    # Dedicated helpers are available for every public route:
    print(client.get_announcements())
    print(client.get_announcements_announcement_id(announcement_id="67c2f8a1b2c3d4e5f6a7b8c9"))
```

All calls automatically send `X-API-Key` from the provided `api_key`.

Use `get`, `post`, `put`, `patch`, `delete`, or `request` for any public `/v1` endpoint.
