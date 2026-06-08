#!/usr/bin/env node
/**
 * Subscribe to every Drishti WebSocket channel and print events.
 *
 * Usage:
 *   DRISHTI_API_KEY=... node js/scripts/ws-listen-all.mjs
 *   DRISHTI_API_KEY=... node js/scripts/ws-listen-all.mjs --symbols RELIANCE,TCS,INFY
 */

import WebSocket from "ws";
import { DrishtiClient, DRISHTI_WS_PRODUCTS } from "../dist/index.js";

function parseArgs(argv) {
  const options = {
    apiKey: process.env.DRISHTI_API_KEY?.trim() ?? "",
    baseUrl: process.env.DRISHTI_BASE_URL ?? "https://developers.manasija.in",
    symbols: process.env.DRISHTI_WS_SYMBOLS || "",
    detailed: true,
  };
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === "--api-key") {
      options.apiKey = argv[++index] ?? "";
    } else if (arg === "--base-url") {
      options.baseUrl = argv[++index] ?? options.baseUrl;
    } else if (arg === "--symbols") {
      options.symbols = argv[++index] ?? options.symbols;
    } else if (arg === "--detailed") {
      options.detailed = true;
    } else if (arg === "--no-detailed") {
      options.detailed = false;
    } else if (arg === "--help" || arg === "-h") {
      printHelp();
      process.exit(0);
    }
  }
  return options;
}

function printHelp() {
  console.log(`Usage: node js/scripts/ws-listen-all.mjs [options]

Options:
  --api-key <key>       API key (default: DRISHTI_API_KEY)
  --base-url <url>      Base URL (default: DRISHTI_BASE_URL or developers.manasija.in)
  --symbols <list>      Comma-separated symbols (default: RELIANCE,TCS)
  --detailed            Request detailed payloads (default)
  --no-detailed         Request summary payloads
`);
}

function parseSymbols(raw) {
  const seen = new Set();
  const symbols = [];
  for (const part of raw.split(",")) {
    const token = part.trim().toUpperCase();
    if (!token || seen.has(token)) {
      continue;
    }
    seen.add(token);
    symbols.push(token);
  }
  return symbols;
}

function preview(data) {
  const symbol = data.symbol;
  const headline = data.headline ?? data.title ?? data.reason;
  const parts = [];
  if (symbol) {
    parts.push(String(symbol));
  }
  if (headline) {
    parts.push(String(headline).slice(0, 120));
  }
  return parts.length > 0 ? parts.join(" | ") : JSON.stringify(data).slice(0, 200);
}

function channelLogger(channel) {
  return (data) => {
    console.log(`[${channel}] ${preview(data)}`);
  };
}

const options = parseArgs(process.argv.slice(2));
if (!options.apiKey) {
  console.error("Missing API key. Pass --api-key or set DRISHTI_API_KEY.");
  process.exit(1);
}

const symbols = parseSymbols(options.symbols);
const client = new DrishtiClient({ apiKey: options.apiKey, baseUrl: options.baseUrl });

const ws = client.websocket({
  webSocketImpl: WebSocket,
  onOpen: () => console.log("[lifecycle] connected"),
  onClose: (reason) => console.log(`[lifecycle] closed: ${reason}`),
  onReconnectAttempt: (attempt, delayMs, reason) => {
    console.log(`[lifecycle] reconnect attempt=${attempt} delay=${delayMs}ms reason=${reason}`);
  },
  onReconnectWarning: (attempt, reason) => {
    console.log(`[lifecycle] reconnect warning after ${attempt} attempts (${reason})`);
  },
  onNews: channelLogger("news"),
  onAnnouncements: channelLogger("announcements"),
  onEarnings: channelLogger("earnings"),
  onConcalls: channelLogger("concalls"),
  onAlerts: channelLogger("alerts"),
  onError: (event) => {
    if (event.kind === "error") {
      console.log(`[error] ${event.message}${event.code ? ` (${event.code})` : ""}`);
    }
  },
});

for (const product of DRISHTI_WS_PRODUCTS) {
  await ws.subscribe({ product, symbols, detailed: options.detailed });
  console.log(`[subscribe] queued ${product} symbols=${symbols.length ? symbols.join(",") : "[]"}`);
}

console.log("Listening on all channels. Press Ctrl+C to stop.");

process.on("SIGINT", async () => {
  console.log("\nStopping...");
  await ws.close();
  process.exit(0);
});

for await (const event of ws.events()) {
  if (event.kind === "subscribed") {
    console.log(
      `[subscribed] product=${event.product} tier=${event.tier} full_feed=${event.fullFeed} symbols=${event.symbols.join(",")}`,
    );
  } else if (event.kind === "raw") {
    console.log("[raw]", event.payload);
  }
}
