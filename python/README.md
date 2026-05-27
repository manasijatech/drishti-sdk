# Drishti SDK (Python)

Official Python SDK for the Manasija Drishti API (`/v1`).

This SDK provides:
- A synchronous HTTP client with named-argument endpoint helpers
- A low-level request interface for advanced/custom usage
- A WebSocket client for real-time streams (`/v1/ws`)

## Requirements

- Python `3.10+`
- A valid Drishti API key

## Installation

```bash
pip install drishti-sdk
```


## Quick Start

```python
from drishti_sdk import DrishtiClient

with DrishtiClient(api_key="YOUR_API_KEY") as client:
    news = client.get_news(
        symbols=["RELIANCE", "TCS"],
        limit=10,
    )
    print(len(news["data"]))
```

All requests automatically include `X-API-Key` using the provided `api_key`.

## HTTP Usage

```python
from drishti_sdk import DrishtiClient

with DrishtiClient(api_key="YOUR_API_KEY") as client:
    announcements = client.get_announcements(
        symbols=["RELIANCE"],
        categories=["Corporate Action"],
        detailed=True,
        limit=20,
    )

    earnings = client.get_earnings_detail(
        symbol="MEDIASSIST",
        quarter="q4_26",
        detailed=True,
    )

    transcript = client.get_concalls_transcript(
        symbol="TCS",
        quarter="q4_26",
    )

    alerts = client.get_alerts(
        symbols=["INFY"],
        important=True,
        limit=25,
    )
```

## Error Handling

```python
from drishti_sdk import DrishtiApiError, DrishtiClient

try:
    with DrishtiClient(api_key="YOUR_API_KEY") as client:
        client.get_account()
except DrishtiApiError as exc:
    print(exc.status_code)
    print(exc.body)
    raise
```

## WebSocket Usage (`/v1/ws`)

```python
import asyncio
from drishti_sdk import DrishtiClient, SubscribeOptions


async def main() -> None:
    client = DrishtiClient(api_key="YOUR_API_KEY")
    async with client.websocket() as ws:
        await ws.subscribe(
            SubscribeOptions(
                product="announcements",
                symbols=["RELIANCE"],
                detailed=False,
            )
        )
        async for event in ws.events():
            if event.kind == "subscribed":
                print("ready", event.product, event.tier)
            elif event.kind == "data":
                print(event.channel, event.data)


asyncio.run(main())
```

Reconnect/callback style:

```python
async with client.websocket(
    auto_reconnect=True,
    reconnect_initial_delay=1.0,
    reconnect_max_delay=30.0,
    on_reconnect_attempt=lambda attempt, delay, reason: print(attempt, delay, reason),
    on_data=lambda event: print(event.data) if event.kind == "data" else None,
) as ws:
    await ws.subscribe("alerts", symbols=["RELIANCE"])
    await ws.run()
```

## Batch Jobs

```python
from drishti_sdk import DrishtiClient

with DrishtiClient(api_key="YOUR_API_KEY") as client:
    with open("batch.jsonl", "rb") as f:
        file_bytes = f.read()

    job = client.post_batch_jobs_file(
        file_name="batch.jsonl",
        file_bytes=file_bytes,
        display_name="Quarterly run",
    )

    status = client.get_batch_jobs_job_id(job_id=job["id"])
```

## API Surface

### REST helper methods

- `get_news`
- `get_symbols_metadata`
- `get_announcements_categories`
- `get_announcements`
- `get_announcements_attachments`
- `post_daily_summary`
- `get_earnings`
- `get_earnings_detail`
- `get_earnings_attachments`
- `get_concalls`
- `get_concalls_detail`
- `get_concalls_transcript`
- `post_concalls_transcripts`
- `get_alerts`
- `get_account`
- `get_account_limits`
- `get_account_usage`
- `get_account_ledger`
- `post_batch_jobs`
- `post_batch_jobs_file`
- `get_batch_jobs`
- `get_batch_jobs_job_id`
- `delete_batch_jobs_job_id`
- `get_batch_jobs_job_id_results`
- `websocket`

### Low-level HTTP methods

- `request`
- `get`
- `post`
- `put`
- `patch`
- `delete`
- `request_v1`

## Development

```bash
pip install -e .[dev]
```
