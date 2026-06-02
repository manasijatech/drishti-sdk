# Drishti SDK (JavaScript / TypeScript)

Official JavaScript/TypeScript SDK for the Manasija Drishti API (`/v1`).

This SDK provides:

- A typed HTTP client for all endpoints of drishti api
- A low-level request layer for custom endpoint access
- A WebSocket session client for real-time streams
- Configurable retry/backoff for transient HTTP failures

## Requirements

- Node.js `18+`
- A valid Drishti API key

## Installation

Install from npm:

```bash
npm install drishti-sdk
```

If you are developing this package locally:

```bash
npm install
npm run build
```

## Quick Start

```ts
import { DrishtiClient } from "drishti-sdk";

const client = new DrishtiClient({
  apiKey: process.env.DRISHTI_API_KEY!,
});

const news = await client.getNews({
  symbols: ["RELIANCE", "TCS"],
  limit: 10,
});

console.log(news.data.length);
```

All SDK calls automatically send `X-API-Key` using the key passed to `DrishtiClient`.

## Retry Configuration

Retries are configurable globally on the client and per request.

```ts
import { DrishtiClient } from "drishti-sdk";

const client = new DrishtiClient({
  apiKey: process.env.DRISHTI_API_KEY!,
  retry: {
    maxRetries: 3,
    initialDelayMs: 250,
    maxDelayMs: 4000,
    multiplier: 2,
    retryOnStatuses: [408, 429, 500, 502, 503, 504],
  },
});

const news = await client.get("/v1/news", {
  query: { limit: 10 },
  retry: { maxRetries: 1 },
});
```

## HTTP Usage

### Common endpoint examples

```ts
import { DrishtiClient } from "drishti-sdk";

const client = new DrishtiClient({ apiKey: process.env.DRISHTI_API_KEY! });

// Announcements
const announcements = await client.getAnnouncements({
  symbols: ["RELIANCE"],
  detailed: true,
  limit: 20,
});

// Earnings detail
const earnings = await client.getEarningsDetail({
  symbol: "MEDIASSIST",
  quarter: "q4_26",
  detailed: true,
});

// Concall transcript URLs
const transcript = await client.getConcallsTranscript({
  symbol: "TCS",
  quarter: "q4_26",
});

// Alerts feed
const alerts = await client.getAlerts({
  symbols: ["INFY"],
  important: true,
  limit: 25,
});

// Account usage
const usage = await client.getAccountUsage();
```

### Low-level request access

Use this when you need an endpoint not yet wrapped by a helper method.

```ts
import { DrishtiClient } from "drishti-sdk";

const client = new DrishtiClient({ apiKey: process.env.DRISHTI_API_KEY! });

const response = await client.request("GET", "/v1/news", {
  query: { symbols: "RELIANCE", limit: 5 },
});
```

You can also use shortcut methods: `get`, `post`, `put`, `patch`, `delete`.

## Error Handling

HTTP failures throw `DrishtiApiError`.

```ts
import { DrishtiApiError, DrishtiClient } from "drishti-sdk";

const client = new DrishtiClient({ apiKey: process.env.DRISHTI_API_KEY! });

try {
  await client.getAccount();
} catch (error) {
  if (error instanceof DrishtiApiError) {
    console.error(error.statusCode);
    console.error(error.body);
  }
  throw error;
}
```

## WebSocket Usage (`/v1/ws`)

### Async iterator style

```ts
import { DrishtiClient } from "drishti-sdk";

const client = new DrishtiClient({ apiKey: process.env.DRISHTI_API_KEY! });
const ws = client.websocket();

await ws.connect();
await ws.subscribe({ product: "announcements", symbols: ["RELIANCE"], detailed: false });

for await (const event of ws.events()) {
  if (event.kind === "subscribed") {
    console.log("subscribed", event.product, event.tier);
  } else if (event.kind === "data") {
    console.log(event.channel, event.data);
  } else if (event.kind === "error") {
    console.error(event.message);
  }
}
```

### Callback style with reconnect

```ts
import { DrishtiClient } from "drishti-sdk";

const client = new DrishtiClient({ apiKey: process.env.DRISHTI_API_KEY! });

const ws = client.websocket({
  autoReconnect: true,
  reconnectInitialDelayMs: 1000,
  reconnectMaxDelayMs: 30000,
  onReconnectAttempt: (attempt, delayMs, reason) => {
    console.log("reconnect", { attempt, delayMs, reason });
  },
  onData: (event) => {
    if (event.kind === "data") console.log(event.channel, event.data);
  },
});

await ws.connect();
await ws.subscribe({ product: "alerts", symbols: ["RELIANCE"] });
await ws.run();
```

Direct import is also supported:

```ts
import { DrishtiWebSocketSession } from "drishti-sdk";
```

## Batch Jobs

Upload JSONL files for batch processing:

```ts
import { readFile } from "node:fs/promises";
import { Blob } from "node:buffer";
import { DrishtiClient } from "drishti-sdk";

const client = new DrishtiClient({ apiKey: process.env.DRISHTI_API_KEY! });

const fileBuffer = await readFile("./batch.jsonl");
const fileBlob = new Blob([fileBuffer], { type: "application/jsonl" });

const job = await client.postBatchJobsFile({
  file: fileBlob,
  filename: "batch.jsonl",
  display_name: "Quarterly run",
});

const status = await client.getBatchJobsJobId({ job_id: job.id });
```

Wait until completion:

```ts
const finalJob = await client.waitForBatchJobCompletion({
  job_id: job.id,
  pollIntervalMs: 2000,
  timeoutMs: 300000,
});
```

Submit and wait in one call:

```ts
const finalJob = await client.submitBatchJobAndWait({
  file: fileBlob,
  filename: "batch.jsonl",
  display_name: "Quarterly run",
  pollIntervalMs: 2000,
  timeoutMs: 300000,
});
```

## API Surface

### REST helper methods

- `getNews`
- `getSymbolsMetadata`
- `getAnnouncementsCategories`
- `getAnnouncements`
- `getAnnouncementsAttachments`
- `postDailySummary`
- `getEarnings`
- `getEarningsDetail`
- `getEarningsAttachments`
- `getConcalls`
- `getConcallsDetail`
- `getConcallsTranscript`
- `postConcallsTranscripts`
- `getAlerts`
- `getAccount`
- `getAccountLimits`
- `getAccountUsage`
- `getAccountLedger`
- `postBatchJobs`
- `postBatchJobsFile`
- `getBatchJobs`
- `getBatchJobsJobId`
- `deleteBatchJobsJobId`
- `getBatchJobsJobIdResults`
- `waitForBatchJobCompletion`
- `submitBatchJobAndWait`
- `websocket`

### Low-level HTTP methods

- `request`
- `get`
- `post`
- `put`
- `patch`
- `delete`

## TypeScript Support

The package ships with `.d.ts` files and typed request/response models from:

- `types.ts` (response payloads)
- `params.ts` (query/body input models)

This enables strong autocomplete and type checking for both endpoint helpers and WebSocket events.
