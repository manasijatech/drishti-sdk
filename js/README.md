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

console.log(await client.get("/v1/news"));
console.log(
  await client.get("/v1/earnings/{earnings_id}", {
    pathParams: { earnings_id: "67c2f8a1b2c3d4e5f6a7b8d0" },
  }),
);
console.log(
  await client.post("/v1/daily-summary", {
    body: { portfolio: [{ symbol: "RELIANCE", exposure: 10 }] },
  }),
);

// Dedicated helpers are available for every public route:
console.log(await client.getNews());
console.log(await client.getAnnouncements());
console.log(await client.getEarningsEarningsId({ earnings_id: "67c2f8a1b2c3d4e5f6a7b8d0" }));
```

All calls automatically send `X-API-Key` using the `apiKey` provided in the constructor.

Use `get`, `post`, `put`, `patch`, `delete`, or `request` to call any public `/v1` endpoint programmatically.
