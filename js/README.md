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

## WebSocket (`/v1/ws`)

```typescript
import { MarketStackClient } from "market-stack-sdk";

const client = new MarketStackClient({ apiKey: process.env.ALPHA_API_KEY! });
const ws = client.websocket();

await ws.connect();
await ws.subscribe({ product: "announcements", symbols: ["RELIANCE"], detailed: false });

for await (const event of ws.events()) {
  if (event.kind === "subscribed") {
    console.log("ready", event.product, event.tier);
  } else if (event.kind === "data") {
    console.log(event.channel, event.data.symbol);
  }
}

await ws.close();
```

Direct session import (without creating `MarketStackClient`):

```typescript
import { AlphaWebSocketSession } from "market-stack-sdk";

const ws = new AlphaWebSocketSession({
  apiKey: process.env.ALPHA_API_KEY!,
  autoReconnect: true,
});

await ws.connect();
await ws.subscribe({ product: "news", symbols: ["RELIANCE"], detailed: true });
await ws.run();
```

Callback style:

```typescript
const ws = client.websocket({
  onData: (event) => console.log(event.data),
});

await ws.connect();
await ws.subscribe({ product: "alerts", symbols: ["RELIANCE"] });
await ws.run();

// Optional resilience hooks:
// autoReconnect: true,
// reconnectInitialDelayMs: 1000,
// reconnectMaxDelayMs: 30000,
// onReconnectAttempt: (attempt, delayMs, reason) => console.log({ attempt, delayMs, reason }),
// onOpen: () => console.log("ws connected"),
// onClose: (reason) => console.log("ws closed", reason),
```
