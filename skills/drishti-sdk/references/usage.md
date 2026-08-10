# Drishti SDK Usage Reference

## Repository Shape

- Root README describes this as the Drishti SDK for Drishti Alpha API (`/v1`).
- JavaScript/TypeScript SDK lives in `js/`.
- Python SDK lives in `python/`.
- Default API base URL: `https://developers.manasija.in`.
- Authentication: `X-API-Key` from client constructor.

## Installation

JavaScript / TypeScript:

```bash
npm install drishti-sdk
```

Browser bundle via CDN (global `DrishtiSDK`) — demos and prototypes only:

```html
<script src="https://cdn.jsdelivr.net/npm/drishti-sdk"></script>
<script>
  const { DrishtiClient } = DrishtiSDK;
</script>
```

> **Warning:** API keys in the browser are visible to users. Prefer `npm install drishti-sdk` on the server for production, or proxy Drishti through your backend.

Unpkg: `https://unpkg.com/drishti-sdk`. Pin a package version in the URL for prototypes, for example `@1.0.1`.

Python:

```bash
pip install drishti-sdk
```

## Endpoint Helper Map

| API area | JavaScript/TypeScript | Python |
| --- | --- | --- |
| News list | `getNews(params?)` | `get_news(...)` |
| Symbol metadata | `getSymbolsMetadata(params)` | `get_symbols_metadata(...)` |
| Announcement categories | `getAnnouncementsCategories()` | `get_announcements_categories()` |
| Announcements list/detail rows | `getAnnouncements(params?)` | `get_announcements(...)` |
| Announcement attachments | `getAnnouncementsAttachments({ ids })` | `get_announcements_attachments(...)` |
| Announcement PDF citation | `getAnnouncementCitationPdf(id)` | `get_announcement_citation_pdf(id)` |
| Daily summary | `postDailySummary({ body, contentRetention? })` | `post_daily_summary(..., content_retention=...)` |
| Earnings index | `getEarningsIndex(params?)` | `get_earnings_index(...)` |
| Earnings list/detail rows | `getEarnings(params?)` | `get_earnings(...)` |
| Earnings detail | `getEarningsDetail({ symbol?, scrip_code?, quarter, detailed? })` | `get_earnings_detail(...)` |
| Earnings attachments | `getEarningsAttachments({ ids })` | `get_earnings_attachments(...)` |
| Earnings PDF citation | `getEarningsCitationPdf(id, page?)` | `get_earnings_citation_pdf(id, page=...)` |
| Earnings PDF page citation | `getEarningsCitationPage(id, page)` | `get_earnings_citation_page(id, page)` |
| Upcoming earnings | `getUpcomingEarnings(params?)` | `get_upcoming_earnings(...)` |
| Concalls index | `getConcallsIndex(params?)` | `get_concalls_index(...)` |
| Concalls list | `getConcalls(params?)` | `get_concalls(...)` |
| Concall PDF citation | `getConcallCitationPdf(id)` | `get_concall_citation_pdf(id)` |
| Upcoming concalls | `getUpcomingConcalls(params?)` | `get_upcoming_concalls(...)` |
| Concalls detail | `getConcallsDetail({ symbol?, scrip_code?, quarter, detailed? })` | `get_concalls_detail(...)` |
| Concall transcript/audio URLs | `getConcallsTranscript({ symbol?, scrip_code?, quarter })` | `get_concalls_transcript(...)` |
| Batch concall transcripts | `postConcallsTranscripts({ items })` | `post_concalls_transcripts(...)` |
| Alerts list | `getAlerts(params?)` | `get_alerts(...)` |
| Account detail | `getAccount()` | `get_account()` |
| Account limits | `getAccountLimits()` | `get_account_limits()` |
| Account usage | `getAccountUsage()` | `get_account_usage()` |
| Account ledger | `getAccountLedger(params?)` | `get_account_ledger(...)` |
| Create batch job | `postBatchJobsFile(params)` or `postBatchJobs(params)` (`contentRetention?`) | `post_batch_jobs_file(...)` or `post_batch_jobs(...)` (`content_retention=...`) |
| List batch jobs | `getBatchJobs(params?)` | `get_batch_jobs(...)` |
| Get batch job | `getBatchJobsJobId({ job_id })` | `get_batch_jobs_job_id(...)` |
| Cancel batch job | `deleteBatchJobsJobId({ job_id })` | `delete_batch_jobs_job_id(...)` |
| Batch job results | `getBatchJobsJobIdResults({ job_id })` | `get_batch_jobs_job_id_results(...)` |

## Common Params

- `symbols`, `scrip_codes` for instruments.
- `from`/`from_`, `to`, `page`, `limit` for feeds.
- `detailed` when detail expansion is supported.
- `contentRetention` / `content_retention: "none"` sends `X-Alpha-Content-Retention` on daily summary and batch upload routes.
- Announcements support `categories`.
- Alerts support `type`, `important`.
- Earnings and concalls list/index routes support `symbols`, `scrip_codes`, `quarter`, `from`, `to`, `page`, `limit`. `GET /v1/earnings` also supports `ids` for earnings record IDs. `GET /v1/earnings` and `GET /v1/concalls` also support `detailed`.

Use either `symbol` or `scrip_code` with required `quarter` for earnings/concall detail APIs.

## JavaScript Example

```ts
import { DrishtiClient, DrishtiApiError } from "drishti-sdk";

const client = new DrishtiClient({ apiKey: process.env.DRISHTI_API_KEY! });

try {
  const announcements = await client.getAnnouncements({
    symbols: ["RELIANCE"],
    categories: ["board meeting"],
    important: true,
    detailed: true,
    limit: 10,
  });

  const summary = await client.postDailySummary({
    body: { portfolio: [{ symbol: "RELIANCE", exposure: 10 }] },
  });

  console.log(announcements.data, summary.status);
} catch (error) {
  if (error instanceof DrishtiApiError) {
    console.error(error.statusCode, error.body);
  }
  throw error;
}
```

## Python Example

```python
from drishti_sdk import DrishtiApiError, DrishtiClient

try:
    with DrishtiClient(api_key="YOUR_API_KEY") as client:
        news = client.get_news(symbols=["RELIANCE"], limit=10)
        announcements = client.get_announcements(symbols=["RELIANCE"], important=True, detailed=True, limit=10)
        summary = client.post_daily_summary(
            body={"portfolio": [{"symbol": "RELIANCE", "exposure": 10}]}
        )
except DrishtiApiError as error:
    print(error.status_code, error.body)
    raise
```

## WebSocket

Websocket is supported in both SDKs at `/v1/ws`. Sessions connect automatically, replay subscriptions after reconnects, and retry with backoff until `close()`.

### JavaScript / TypeScript

Event listeners:

```ts
const ws = client.websocket({
  onData: (event) => {
    if (event.kind === "data") console.log(event.channel, event.data);
  },
  onAnnouncements: (announcement) => console.log("announcement", announcement),
});
await ws.subscribe({ product: "alerts", symbols: ["RELIANCE"] });
```

Channel listeners: `onNews`, `onAnnouncements`, `onEarnings`, `onConcalls`, `onAlerts`, or `ws.on("announcements", handler)`.

Async iterator:

```ts
const ws = client.websocket();
await ws.subscribe({ product: "announcements", symbols: ["RELIANCE"] });
for await (const event of ws.events()) {
  if (event.kind === "data") console.log(event.channel, event.data);
}
```

### Python

Event listeners:

```python
async with client.websocket(
    on_announcements=lambda announcement: print("announcement", announcement),
    on_data=lambda event: print(event.data) if event.kind == "data" else None,
) as ws:
    await ws.subscribe("alerts", symbols=["RELIANCE"])
    await asyncio.Event().wait()
```

Async iterator:

```python
async with client.websocket() as ws:
    await ws.subscribe("announcements", symbols=["RELIANCE"])
    async for event in ws.events():
        if event.kind == "data":
            print(event.channel, event.data)
```

`subscribe` accepts either `SubscribeOptions(...)` or the product name with keyword args.

## Low-Level Calls

Use low-level calls for `/v1` endpoints not wrapped by helper methods.

- JS: `client.request("GET", "/v1/news", { query: { symbols: "RELIANCE" } })`
- Python: `client.request_v1("GET", "news", params={"symbols": "RELIANCE"})`
