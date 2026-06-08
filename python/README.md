# Drishti SDK (Python)

Official Python SDK for the Manasija Drishti API (`/v1`).

This SDK provides:
- A synchronous HTTP client with named-argument endpoint helpers
- A low-level request interface for advanced/custom usage
- A WebSocket client for real-time streams (`/v1/ws`)
- Configurable retry/backoff for transient HTTP failures

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

## Retry Configuration

Retries are configurable globally on the client and per request.

```python
from drishti_sdk import DrishtiClient

with DrishtiClient(
    api_key="YOUR_API_KEY",
    retry_max_retries=3,
    retry_initial_delay=0.25,
    retry_max_delay=4.0,
    retry_multiplier=2.0,
    retry_on_statuses=(408, 429, 500, 502, 503, 504),
) as client:
    # Per-request override
    news = client.request(
        "GET",
        "/v1/news",
        params={"limit": 10},
        retry_max_retries=1,
    )
```

## HTTP Usage

```python
from drishti_sdk import DrishtiClient

with DrishtiClient(api_key="YOUR_API_KEY") as client:
    announcements = client.get_announcements(
        symbols=["RELIANCE"],
        categories=["Corporate Action"],
        important=True,
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

Callback style:

```python
async with client.websocket(
    reconnect_initial_delay=1.0,
    reconnect_max_delay=30.0,
    on_reconnect_attempt=lambda attempt, delay, reason: print(attempt, delay, reason),
    on_data=lambda event: print(event.data) if event.kind == "data" else None,
    on_announcements=lambda announcement: print("announcement", announcement),
    on_alerts=lambda alert: print("alert", alert),
) as ws:
    await ws.subscribe("alerts", symbols=["RELIANCE"])
    await asyncio.Event().wait()  # callbacks run in the background; Ctrl+C to stop
```

Register listeners after connect:

```python
def on_announcement(announcement: dict[str, object]) -> None:
    print("announcement", announcement)

ws.on_announcements(on_announcement)
ws.off("announcements", on_announcement)
```

### WebSocket Reference

The WebSocket session is created from the HTTP client, so it inherits the same
`api_key` and `base_url` settings. The session connects to `/v1/ws` and sends
`X-API-Key` on connect.

Supported subscription products:

- `news`
- `announcements`
- `earnings`
- `concalls`
- `alerts`

Useful session options:

- `reconnect_initial_delay`
- `reconnect_max_delay`
- `reconnect_backoff_multiplier`
- `reconnect_jitter_ratio`
- `reconnect_warn_after_attempts`
- `ping_interval`
- `ping_timeout`
- `open_timeout`
- `close_timeout`
- `max_queue`
- `on_subscribed`
- `on_data`
- `on_news`
- `on_announcements`
- `on_earnings`
- `on_concalls`
- `on_alerts`
- `on_error`
- `on_message`
- `on_open`
- `on_close`
- `on_reconnect_attempt`
- `on_reconnect_warning`

The session connects automatically on the first async call (`subscribe`,
`events`, or entering an `async with` block) and keeps retrying in the
background after disconnects.

Subscription messages accept either `SubscribeOptions(...)` or the product name
as a string. Symbols are normalized to uppercase and de-duplicated before the
message is sent. Subscriptions are replayed after every reconnect.

Event shapes:

- `subscribed`: acknowledgement with `product`, `tier`, `full_feed`,
  `symbols`, and `detailed`
- `data`: payload event with `channel` and `data`
- `error`: error event with `message` and optional `code`
- `raw`: unclassified JSON payload

Direct exports available from `drishti_sdk`:

- `DRISHTI_WS_PRODUCTS`
- `SubscribeOptions`
- `DrishtiWebSocketSession`
- `DrishtiWebSocketClientSessionOptions`
- `DataEvent`
- `ErrorEvent`
- `RawEvent`
- `SubscribedEvent`
- `WebSocketEvent`
- `build_websocket_url`
- `parse_websocket_message`
- `stream_product`

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

Wait until completion:

```python
final_job = client.wait_for_batch_job_completion(
    job_id=job["id"],
    poll_interval=2.0,
    timeout=300.0,
)
```

Submit and wait in one call:

```python
final_job = client.submit_batch_job_and_wait(
    file_name="batch.jsonl",
    file_bytes=file_bytes,
    display_name="Quarterly run",
    poll_interval=2.0,
    timeout=300.0,
)
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
- `get_upcoming_concalls`
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
- `wait_for_batch_job_completion`
- `submit_batch_job_and_wait`
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
