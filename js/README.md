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
  // Optional override. Defaults to https://developers.manasija.in
  // baseUrl: "https://developers.manasija.in",
});

// Dedicated helpers are available for every route:
console.log(await client.getNews());
console.log(await client.getAnnouncements());
console.log(
  await client.getEarningsDetail({ symbol: "MEDIASSIST", quarter: "q4_26", detailed: true }),
);
```

All calls automatically send `X-API-Key` using the `apiKey` provided in the constructor.

Use `get`, `post`, `put`, `patch`, `delete`, or `request` to call any public `/v1` endpoint programmatically.
