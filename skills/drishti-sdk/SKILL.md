---
name: drishti-sdk
description: Use this skill when Codex needs to integrate with or explain the Drishti Market-Stack SDK for Alpha API /v1 endpoints in JavaScript, TypeScript, or Python. Trigger for tasks involving Drishti SDK, drishti-sdk, DrishtiClient, Alpha API news, announcements, earnings, concalls, alerts, account, batch jobs, websocket streams, API key auth, request params, and response typing.
---

# Drishti SDK

Use this skill to write or review code that calls Drishti Alpha API (`/v1`) through the local SDKs.

## First Steps

1. Prefer SDK helper methods over hand-built HTTP calls.
2. Use `X-API-Key` authentication through the client constructor.
3. Default base URL is `https://developers.manasija.in`; override only for staging/local/tests.
4. Read `references/usage.md` when you need endpoint helper names, params, examples, batch upload details, or websocket usage.

## Package Names

- TypeScript/JavaScript package: `drishti-sdk`.
- Python package: `drishti-sdk`, import module `drishti_sdk`.
- Both SDKs expose `DrishtiClient` and `DrishtiApiError`.

## JavaScript And TypeScript

Use Node 18+ because the SDK relies on `fetch`, `Blob`, and `FormData`.

```ts
import { DrishtiClient } from "drishti-sdk";

const client = new DrishtiClient({
  apiKey: process.env.DRISHTI_API_KEY!,
});

const news = await client.getNews({ symbols: ["RELIANCE"], limit: 10 });
```

Use camelCase helper methods like `getNews`, `getAnnouncements`, `getEarningsDetail`, `postDailySummary`, `getAccountUsage`.

## Python

Use a context manager so the underlying `httpx.Client` is closed.

```python
from drishti_sdk import DrishtiClient

with DrishtiClient(api_key="YOUR_API_KEY") as client:
    news = client.get_news(symbols=["RELIANCE"], limit=10)
```

Use snake_case helper methods like `get_news`, `get_announcements`, `get_earnings_detail`, `post_daily_summary`, `get_account_usage`.

## Params And Query Encoding

- Prefer named helper arguments in Python and typed params in TypeScript.
- List filters such as `symbols`, `scrip_codes`, `categories`, `ids`, and alert `type` are serialized as comma-separated query values by helper serializers.
- In Python, the API query key `from` is represented as `from_`.

## Raw Endpoint Calls

Use low-level methods only when a helper does not exist:

```ts
await client.get("/v1/news", { query: { symbols: "RELIANCE", limit: 5 } });
await client.post("/v1/daily-summary", { body: { portfolio: [{ symbol: "RELIANCE", exposure: 10 }] } });
```

```python
client.get("/v1/news", params={"symbols": "RELIANCE", "limit": 5})
client.request_v1("GET", "news", params={"symbols": "RELIANCE"})
```

## WebSocket Support

This SDK includes websocket support for `/v1/ws`.

- JavaScript: use `client.websocket()` or `AlphaWebSocketSession`.
- Python: use `async with client.websocket() as ws`, then `subscribe(...)` and `events()` or `run()`.

## Validation

For SDK changes, run relevant checks:

```bash
cd js && npm run build
cd python && python -m compileall drishti_sdk
```

Add focused tests when behavior changes, especially query serialization, websocket parsing, path params, errors, and file upload.
