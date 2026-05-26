---
name: drishti-sdk
description: Use this skill when Codex needs to integrate with or explain the Drishti/Manasija Market-Stack SDK for Alpha API /v1 endpoints in JavaScript, TypeScript, or Python. Trigger for tasks involving market-stack-sdk, @manasija/market-stack-sdk, MarketStackClient, Alpha API news, announcements, earnings, concalls, alerts, account, batch jobs, API key auth, request params, response typing, or checking whether websocket support exists in this SDK.
---

# Drishti SDK

Use this skill to write or review code that calls Drishti Alpha API (`/v1`) through the local Market-Stack SDKs.

## First Steps

1. Prefer the SDK helper methods over hand-built HTTP calls.
2. Use `X-API-Key` authentication through the client constructor; do not manually add it unless using a raw HTTP client.
3. Default base URL is `https://developers.manasija.in`; override `baseUrl` only for staging, local API, or tests.
4. Read `references/usage.md` when you need endpoint-helper names, params, examples, batch upload details, or websocket status.

## Package Names

- TypeScript/JavaScript package: `@manasija/market-stack-sdk`.
- Python package: `market-stack-sdk`, import module `market_stack_sdk`.
- Both SDKs expose `MarketStackClient` and `MarketStackApiError`.

## JavaScript And TypeScript

Use Node 18+ because the SDK relies on global `fetch`, `Blob`, and `FormData`.

```ts
import { MarketStackClient } from "@manasija/market-stack-sdk";

const client = new MarketStackClient({
  apiKey: process.env.ALPHA_API_KEY!,
});

const news = await client.getNews({ symbols: ["RELIANCE"], limit: 10 });
```

Use camelCase helper methods: `getNews`, `getAnnouncements`, `getEarningsDetail`, `postDailySummary`, `getAccountUsage`, etc.

## Python

Use a context manager so the underlying `httpx.Client` is closed.

```python
from market_stack_sdk import MarketStackClient, NewsQueryParams

with MarketStackClient(api_key="YOUR_API_KEY") as client:
    news = client.get_news(NewsQueryParams(symbols=["RELIANCE"], limit=10))
```

Use snake_case helper methods: `get_news`, `get_announcements`, `get_earnings_detail`, `post_daily_summary`, `get_account_usage`, etc.

## Params And Query Encoding

- Prefer typed params models in Python (`NewsQueryParams`, `SymbolQuarterQueryParams`, `DailySummaryRequest`) when available.
- Plain dictionaries also work in Python helper methods.
- Use JS object params for TypeScript.
- List filters such as `symbols`, `scrip_codes`, `categories`, `ids`, and alert `type` are serialized as comma-separated query values by the SDK helpers.
- In Python, the query key `from` is represented as `from_` in Pydantic models.

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

## Websocket Status

This SDK currently does not include a websocket client or websocket subscribe/connect helpers. Websocket-related types only appear in account limits, usage, and entitlement responses. If asked to use websocket APIs, first inspect the current SDK/API docs or repository for newly added websocket files before inventing an interface.

## Validation

For SDK changes, run the relevant local checks:

```bash
cd js && npm run build
cd python && python -m compileall market_stack_sdk
```

Add focused tests if behavior changes, especially around query serialization, path params, error handling, or file upload.
