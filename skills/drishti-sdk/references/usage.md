# Drishti SDK Usage Reference

## Repository Shape

- Root README calls this the Market-Stack SDK for Alpha API (`/v1`).
- JavaScript/TypeScript SDK lives in `js/`.
- Python SDK lives in `python/`.
- Default API base URL: `https://developers.manasija.in`.
- Authentication: `X-API-Key`, supplied through the client constructor.

## Endpoint Helper Map

| API area | JavaScript/TypeScript | Python |
| --- | --- | --- |
| News list | `getNews(params?)` | `get_news(params=None)` |
| Symbol metadata | `getSymbolsMetadata(params)` | `get_symbols_metadata(params)` |
| Announcement categories | `getAnnouncementsCategories()` | `get_announcements_categories()` |
| Announcements list/detail rows | `getAnnouncements(params?)` | `get_announcements(params=None)` |
| Announcement attachments | `getAnnouncementsAttachments({ ids })` | `get_announcements_attachments(params)` |
| Daily summary | `postDailySummary({ body })` | `post_daily_summary({ "body": ... })` |
| Earnings list/detail rows | `getEarnings(params?)` | `get_earnings(params=None)` |
| Earnings detail | `getEarningsDetail({ symbol?, scrip_code?, quarter, detailed? })` | `get_earnings_detail(params)` |
| Earnings attachments | `getEarningsAttachments({ ids })` | `get_earnings_attachments(params)` |
| Concalls list | `getConcalls(params?)` | `get_concalls(params=None)` |
| Concalls detail | `getConcallsDetail({ symbol?, scrip_code?, quarter, detailed? })` | `get_concalls_detail(params)` |
| Concall transcript/audio URLs | `getConcallsTranscript({ symbol?, scrip_code?, quarter })` | `get_concalls_transcript(params)` |
| Batch concall transcripts | `postConcallsTranscripts({ items })` | `post_concalls_transcripts({ "items": ... })` |
| Alerts list | `getAlerts(params?)` | `get_alerts(params=None)` |
| Account detail | `getAccount()` | `get_account()` |
| Account limits | `getAccountLimits()` | `get_account_limits()` |
| Account usage | `getAccountUsage()` | `get_account_usage()` |
| Account ledger | `getAccountLedger(params?)` | `get_account_ledger(params=None)` |
| Create batch job | `postBatchJobsFile(params)` or `postBatchJobs(params)` | `post_batch_jobs_file(params)` or `post_batch_jobs(params)` |
| List batch jobs | `getBatchJobs(params?)` | `get_batch_jobs(params=None)` |
| Get batch job | `getBatchJobsJobId({ job_id })` | `get_batch_jobs_job_id(params)` |
| Cancel batch job | `deleteBatchJobsJobId({ job_id })` | `delete_batch_jobs_job_id(params)` |
| Batch job results | `getBatchJobsJobIdResults({ job_id })` | `get_batch_jobs_job_id_results(params)` |

## Common Params

### List feeds

Use these on news, announcements, earnings, and concalls where applicable:

- `symbols`: list of ticker symbols.
- `scrip_codes`: list of scrip codes.
- `from`/`from_`: ISO date or datetime lower bound.
- `to`: ISO date or datetime upper bound.
- `detailed`: boolean for feed rows that support detail expansion.
- `page`: page number.
- `limit`: page size.

Announcements also support `categories`. News supports `sentiment` as `positive`, `negative`, or `neutral`. Alerts support `type`, `important`, `from`/`from_`, `to`, `page`, and `limit`.

### Detail by symbol and quarter

Use for earnings detail, concall detail, and concall transcript/audio URLs:

```ts
await client.getEarningsDetail({ symbol: "MEDIASSIST", quarter: "q4_26", detailed: true });
```

```python
client.get_earnings_detail({"symbol": "MEDIASSIST", "quarter": "q4_26", "detailed": True})
```

Provide either `symbol` or `scrip_code` plus required `quarter`.

## JavaScript Examples

```ts
import { MarketStackClient, MarketStackApiError } from "@manasija/market-stack-sdk";

const client = new MarketStackClient({
  apiKey: process.env.ALPHA_API_KEY!,
  // baseUrl: "https://developers.manasija.in",
});

try {
  const announcements = await client.getAnnouncements({
    symbols: ["RELIANCE"],
    categories: ["board meeting"],
    detailed: true,
    limit: 10,
  });

  const summary = await client.postDailySummary({
    body: { portfolio: [{ symbol: "RELIANCE", exposure: 10 }] },
  });

  console.log(announcements.data, summary.status);
} catch (error) {
  if (error instanceof MarketStackApiError) {
    console.error(error.statusCode, error.body);
  }
  throw error;
}
```

### JavaScript batch upload

```ts
const file = new Blob([jsonlText], { type: "application/jsonl" });

const job = await client.postBatchJobsFile({
  file,
  filename: "batch.jsonl",
  display_name: "May earnings lookup",
  metadata: JSON.stringify({ source: "agent" }),
});
```

## Python Examples

```python
from market_stack_sdk import (
    AnnouncementsQueryParams,
    DailySummaryRequest,
    MarketStackApiError,
    MarketStackClient,
    NewsQueryParams,
)

try:
    with MarketStackClient(api_key="YOUR_API_KEY") as client:
        news = client.get_news(NewsQueryParams(symbols=["RELIANCE"], limit=10))
        announcements = client.get_announcements(
            AnnouncementsQueryParams(symbols=["RELIANCE"], detailed=True, limit=10)
        )
        summary = client.post_daily_summary(
            {
                "body": DailySummaryRequest(
                    portfolio=[{"symbol": "RELIANCE", "exposure": 10}]
                )
            }
        )
except MarketStackApiError as error:
    print(error.status_code, error.body)
    raise
```

### Python batch upload

```python
from pathlib import Path

with MarketStackClient(api_key="YOUR_API_KEY") as client:
    job = client.post_batch_jobs_file(
        {
            "file_name": "batch.jsonl",
            "file_bytes": Path("batch.jsonl").read_bytes(),
            "display_name": "May earnings lookup",
            "metadata": '{"source":"agent"}',
        }
    )
```

## Low-Level Calls

Use low-level calls for public `/v1` endpoints not yet wrapped by a helper.

JavaScript:

```ts
await client.request("GET", "/v1/news", { query: { symbols: "RELIANCE" } });
await client.get("/v1/account/usage");
await client.post("/v1/daily-summary", {
  body: { portfolio: [{ symbol: "RELIANCE", exposure: 10 }] },
});
```

Python:

```python
client.request("GET", "/v1/news", params={"symbols": "RELIANCE"})
client.request_v1("GET", "news", params={"symbols": "RELIANCE"})
client.post("/v1/daily-summary", body={"portfolio": [{"symbol": "RELIANCE", "exposure": 10}]})
```

## Websocket Notes

The current SDK has no websocket connection class, subscribe method, or streaming helper in `js/src` or `python/market_stack_sdk`. It exposes websocket-related data only as account response fields:

- account detail includes `websocket_addons` and `live_entitlement`.
- account usage includes `live_usage`, `rate_limits`, and `live_entitlement`.
- account limits includes `websocket`.

When implementing websocket functionality, first confirm the actual API contract from current API docs or source. Do not infer a `wss://` URL, message schema, or subscribe protocol from these SDK files alone.
