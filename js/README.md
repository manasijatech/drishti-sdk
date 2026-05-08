# market-stack-sdk (JavaScript / TypeScript)

HTTP client for Alpha API (`/v1`). Requires Node 18+ (global `fetch`).

## Install

From this directory:

```bash
npm install
npm run build
```

To use as a local package: `npm pack` or `npm link`.

## Usage

```typescript
import { MarketStackClient } from "market-stack-sdk";

const client = new MarketStackClient({
  apiKey: process.env.ALPHA_API_KEY!,
  // Optional override. Defaults to process.env.ALPHA_API_BASE_URL
  // baseUrl: "http://127.0.0.1:8000",
});

console.log(await client.get("/v1/announcements"));
console.log(
  await client.get("/v1/announcements/{announcement_id}", {
    pathParams: { announcement_id: "67c2f8a1b2c3d4e5f6a7b8c9" },
  }),
);
console.log(
  await client.post("/v1/daily-summary/generate", {
    body: { portfolio: [{ symbol: "RELIANCE", qty: 10 }] },
    query: { page: 1, limit: 20 },
  }),
);

// Dedicated helpers are available for every public route:
console.log(await client.getAnnouncements());
console.log(await client.getAnnouncementsAnnouncementId({ announcement_id: "67c2f8a1b2c3d4e5f6a7b8c9" }));
```

All calls automatically send `X-API-Key` using the `apiKey` provided in the constructor.

Use `get`, `post`, `put`, `patch`, `delete`, or `request` to call any public `/v1` endpoint programmatically.
