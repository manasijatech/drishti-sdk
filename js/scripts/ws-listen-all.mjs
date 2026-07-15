#!/usr/bin/env node
/**
 * Subscribe to every Drishti WebSocket channel and print events.
 *
 * Usage:
 *   DRISHTI_API_KEY=... node js/scripts/ws-listen-all.mjs
 *   DRISHTI_API_KEY=... node js/scripts/ws-listen-all.mjs --symbols RELIANCE,TCS,INFY
 */

import WebSocket from "ws";
import Database from "better-sqlite3";
import { mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { DrishtiClient, DRISHTI_WS_PRODUCTS } from "../dist/index.js";

const RETENTION_DAYS = 30;
const RETENTION_SWEEP_INTERVAL_MS = 24 * 60 * 60 * 1000;

function parseArgs(argv) {
  const options = {
    apiKey: process.env.DRISHTI_API_KEY?.trim() ?? "",
    baseUrl: process.env.DRISHTI_BASE_URL ?? "https://developers.manasija.in",
    symbols: process.env.DRISHTI_WS_SYMBOLS || "",
    dbPath: process.env.DRISHTI_WS_DB_PATH ?? "./ws-listen-all.sqlite",
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
    } else if (arg === "--db-path") {
      options.dbPath = argv[++index] ?? options.dbPath;
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
  --db-path <path>      SQLite database path (default: DRISHTI_WS_DB_PATH or ./ws-listen-all.sqlite)
  --detailed            Request detailed payloads (default)
  --no-detailed         Request summary payloads
`);
}

function formatLogTimestamp() {
  return new Date().toLocaleString("en-IN", {
    timeZone: "Asia/Kolkata",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false,
  });
}

function log(...parts) {
  console.log(`${formatLogTimestamp()}`, ...parts);
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

function ensureDirectoryForFile(filePath) {
  mkdirSync(dirname(resolve(filePath)), { recursive: true });
}

function nowIso() {
  return new Date().toISOString();
}

function stringifyValue(value) {
  if (value === undefined || value === null) {
    return null;
  }
  if (typeof value === "string") {
    return value;
  }
  if (typeof value === "number" || typeof value === "boolean" || typeof value === "bigint") {
    return String(value);
  }
  return JSON.stringify(value);
}

function stringifyJson(value) {
  if (value === undefined || value === null) {
    return null;
  }
  return JSON.stringify(value);
}

function toInteger(value) {
  if (value === undefined || value === null) {
    return null;
  }
  if (typeof value === "boolean") {
    return value ? 1 : 0;
  }
  if (typeof value === "number") {
    return Number.isFinite(value) ? value : null;
  }
  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    if (normalized === "true") {
      return 1;
    }
    if (normalized === "false") {
      return 0;
    }
    const parsed = Number.parseInt(value, 10);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}

function prepareDb(dbPath) {
  ensureDirectoryForFile(dbPath);
  const db = new Database(resolve(dbPath));
  db.pragma("journal_mode = WAL");
  db.pragma("synchronous = NORMAL");
  db.pragma("foreign_keys = ON");
  db.exec(`
    CREATE TABLE IF NOT EXISTS ws_messages (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      received_at TEXT NOT NULL,
      kind TEXT NOT NULL,
      channel TEXT,
      payload_json TEXT NOT NULL
    );

    CREATE INDEX IF NOT EXISTS idx_ws_messages_kind_received_at
      ON ws_messages(kind, received_at);

    CREATE INDEX IF NOT EXISTS idx_ws_messages_channel_received_at
      ON ws_messages(channel, received_at);

    CREATE TABLE IF NOT EXISTS news_events (
      id TEXT PRIMARY KEY,
      symbol TEXT,
      title TEXT,
      specific_title TEXT,
      summary TEXT,
      long_summary TEXT,
      company TEXT,
      source TEXT,
      sentiment TEXT,
      article_type TEXT,
      scrip_code TEXT,
      date TEXT,
      link TEXT,
      payload_json TEXT NOT NULL,
      received_at TEXT NOT NULL,
      last_seen_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS announcements_events (
      id TEXT PRIMARY KEY,
      symbol TEXT,
      company_name TEXT,
      image TEXT,
      date TEXT,
      headline TEXT,
      title TEXT,
      summary TEXT,
      category TEXT,
      attachment_url TEXT,
      long_summary TEXT,
      descriptor TEXT,
      important INTEGER,
      related_categories_json TEXT,
      extracted_information_json TEXT,
      payload_json TEXT NOT NULL,
      received_at TEXT NOT NULL,
      last_seen_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS earnings_events (
      id TEXT PRIMARY KEY,
      symbol TEXT,
      scrip_code TEXT,
      company_name TEXT,
      image TEXT,
      quarter TEXT,
      date TEXT,
      summary TEXT,
      attachment_url TEXT,
      earnings_significant INTEGER,
      earnings_table_json TEXT,
      payload_json TEXT NOT NULL,
      received_at TEXT NOT NULL,
      last_seen_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS concalls_events (
      id TEXT PRIMARY KEY,
      symbol TEXT,
      quarter TEXT,
      date TEXT,
      transcript_url TEXT,
      audio_url TEXT,
      short_analysis_json TEXT,
      sentiment_analysis_json TEXT,
      expanded_analysis_json TEXT,
      payload_json TEXT NOT NULL,
      received_at TEXT NOT NULL,
      last_seen_at TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS alerts_events (
      id TEXT PRIMARY KEY,
      symbol TEXT,
      type TEXT,
      reason TEXT,
      timestamp TEXT,
      meta_json TEXT NOT NULL,
      payload_json TEXT NOT NULL,
      received_at TEXT NOT NULL,
      last_seen_at TEXT NOT NULL
    );
  `);
  return db;
}

function getRetentionCutoffIso() {
  return new Date(Date.now() - RETENTION_DAYS * 24 * 60 * 60 * 1000).toISOString();
}

function pruneOldRows(db) {
  const cutoffIso = getRetentionCutoffIso();
  const deleteWsMessages = db.prepare(`DELETE FROM ws_messages WHERE received_at < ?`);
  const deleteNews = db.prepare(`DELETE FROM news_events WHERE last_seen_at < ?`);
  const deleteAnnouncements = db.prepare(`DELETE FROM announcements_events WHERE last_seen_at < ?`);
  const deleteEarnings = db.prepare(`DELETE FROM earnings_events WHERE last_seen_at < ?`);
  const deleteConcalls = db.prepare(`DELETE FROM concalls_events WHERE last_seen_at < ?`);
  const deleteAlerts = db.prepare(`DELETE FROM alerts_events WHERE last_seen_at < ?`);

  const result = db.transaction(() => {
    const wsMessages = deleteWsMessages.run(cutoffIso);
    const news = deleteNews.run(cutoffIso);
    const announcements = deleteAnnouncements.run(cutoffIso);
    const earnings = deleteEarnings.run(cutoffIso);
    const concalls = deleteConcalls.run(cutoffIso);
    const alerts = deleteAlerts.run(cutoffIso);
    return {
      cutoffIso,
      deleted: {
        ws_messages: wsMessages.changes,
        news_events: news.changes,
        announcements_events: announcements.changes,
        earnings_events: earnings.changes,
        concalls_events: concalls.changes,
        alerts_events: alerts.changes,
      },
    };
  })();

  const deletedTotal = Object.values(result.deleted).reduce((sum, count) => sum + count, 0);
  log(
    `[db] retention sweep cutoff=${result.cutoffIso} deleted=${deletedTotal} ` +
      `ws_messages=${result.deleted.ws_messages} news=${result.deleted.news_events} ` +
      `announcements=${result.deleted.announcements_events} earnings=${result.deleted.earnings_events} ` +
      `concalls=${result.deleted.concalls_events} alerts=${result.deleted.alerts_events}`,
  );
}

function persistMessage(db, kind, channel, payload) {
  db.prepare(
    `INSERT INTO ws_messages (received_at, kind, channel, payload_json)
     VALUES (?, ?, ?, ?)`,
  ).run(nowIso(), kind, channel ?? null, stringifyJson(payload) ?? "null");
}

function upsertNews(db, data) {
  const statement = db.prepare(`
    INSERT INTO news_events (
      id, symbol, title, specific_title, summary, long_summary, company, source,
      sentiment, article_type, scrip_code, date, link, payload_json, received_at, last_seen_at
    ) VALUES (
      @id, @symbol, @title, @specific_title, @summary, @long_summary, @company, @source,
      @sentiment, @article_type, @scrip_code, @date, @link, @payload_json, @received_at, @last_seen_at
    )
    ON CONFLICT(id) DO UPDATE SET
      symbol = excluded.symbol,
      title = excluded.title,
      specific_title = excluded.specific_title,
      summary = excluded.summary,
      long_summary = excluded.long_summary,
      company = excluded.company,
      source = excluded.source,
      sentiment = excluded.sentiment,
      article_type = excluded.article_type,
      scrip_code = excluded.scrip_code,
      date = excluded.date,
      link = excluded.link,
      payload_json = excluded.payload_json,
      last_seen_at = excluded.last_seen_at
  `);
  statement.run({
    id: stringifyValue(data.id),
    symbol: stringifyValue(data.symbol),
    title: stringifyValue(data.title),
    specific_title: stringifyValue(data.specific_title),
    summary: stringifyValue(data.summary),
    long_summary: stringifyValue(data.long_summary),
    company: stringifyValue(data.company),
    source: stringifyValue(data.source),
    sentiment: stringifyValue(data.sentiment),
    article_type: stringifyValue(data.article_type),
    scrip_code: stringifyValue(data.scrip_code),
    date: stringifyValue(data.date),
    link: stringifyValue(data.link),
    payload_json: stringifyJson(data) ?? "null",
    received_at: nowIso(),
    last_seen_at: nowIso(),
  });
}

function upsertAnnouncements(db, data) {
  const statement = db.prepare(`
    INSERT INTO announcements_events (
      id, symbol, company_name, image, date, headline, title, summary, category,
      attachment_url, long_summary, descriptor, important, related_categories_json,
      extracted_information_json, payload_json, received_at, last_seen_at
    ) VALUES (
      @id, @symbol, @company_name, @image, @date, @headline, @title, @summary, @category,
      @attachment_url, @long_summary, @descriptor, @important, @related_categories_json,
      @extracted_information_json, @payload_json, @received_at, @last_seen_at
    )
    ON CONFLICT(id) DO UPDATE SET
      symbol = excluded.symbol,
      company_name = excluded.company_name,
      image = excluded.image,
      date = excluded.date,
      headline = excluded.headline,
      title = excluded.title,
      summary = excluded.summary,
      category = excluded.category,
      attachment_url = excluded.attachment_url,
      long_summary = excluded.long_summary,
      descriptor = excluded.descriptor,
      important = excluded.important,
      related_categories_json = excluded.related_categories_json,
      extracted_information_json = excluded.extracted_information_json,
      payload_json = excluded.payload_json,
      last_seen_at = excluded.last_seen_at
  `);
  statement.run({
    id: stringifyValue(data.id),
    symbol: stringifyValue(data.symbol),
    company_name: stringifyValue(data.company_name),
    image: stringifyValue(data.image),
    date: stringifyValue(data.date),
    headline: stringifyValue(data.headline),
    title: stringifyValue(data.title),
    summary: stringifyValue(data.summary),
    category: stringifyValue(data.category),
    attachment_url: stringifyValue(data.attachment_url),
    long_summary: stringifyValue(data.long_summary),
    descriptor: stringifyValue(data.descriptor),
    important: toInteger(data.important),
    related_categories_json: stringifyJson(data.related_categories),
    extracted_information_json: stringifyJson(data.extracted_information),
    payload_json: stringifyJson(data) ?? "null",
    received_at: nowIso(),
    last_seen_at: nowIso(),
  });
}

function upsertEarnings(db, data) {
  const statement = db.prepare(`
    INSERT INTO earnings_events (
      id, symbol, scrip_code, company_name, image, quarter, date, summary,
      attachment_url, earnings_significant, earnings_table_json, payload_json,
      received_at, last_seen_at
    ) VALUES (
      @id, @symbol, @scrip_code, @company_name, @image, @quarter, @date, @summary,
      @attachment_url, @earnings_significant, @earnings_table_json, @payload_json,
      @received_at, @last_seen_at
    )
    ON CONFLICT(id) DO UPDATE SET
      symbol = excluded.symbol,
      scrip_code = excluded.scrip_code,
      company_name = excluded.company_name,
      image = excluded.image,
      quarter = excluded.quarter,
      date = excluded.date,
      summary = excluded.summary,
      attachment_url = excluded.attachment_url,
      earnings_significant = excluded.earnings_significant,
      earnings_table_json = excluded.earnings_table_json,
      payload_json = excluded.payload_json,
      last_seen_at = excluded.last_seen_at
  `);
  statement.run({
    id: stringifyValue(data.id),
    symbol: stringifyValue(data.symbol),
    scrip_code: stringifyValue(data.scrip_code),
    company_name: stringifyValue(data.company_name),
    image: stringifyValue(data.image),
    quarter: stringifyValue(data.quarter),
    date: stringifyValue(data.date),
    summary: stringifyValue(data.summary),
    attachment_url: stringifyValue(data.attachment_url),
    earnings_significant: toInteger(data.earnings_significant),
    earnings_table_json: stringifyJson(data.earnings_table),
    payload_json: stringifyJson(data) ?? "null",
    received_at: nowIso(),
    last_seen_at: nowIso(),
  });
}

function upsertConcalls(db, data) {
  const statement = db.prepare(`
    INSERT INTO concalls_events (
      id, symbol, quarter, date, transcript_url, audio_url, short_analysis_json,
      sentiment_analysis_json, expanded_analysis_json, payload_json, received_at, last_seen_at
    ) VALUES (
      @id, @symbol, @quarter, @date, @transcript_url, @audio_url, @short_analysis_json,
      @sentiment_analysis_json, @expanded_analysis_json, @payload_json, @received_at, @last_seen_at
    )
    ON CONFLICT(id) DO UPDATE SET
      symbol = excluded.symbol,
      quarter = excluded.quarter,
      date = excluded.date,
      transcript_url = excluded.transcript_url,
      audio_url = excluded.audio_url,
      short_analysis_json = excluded.short_analysis_json,
      sentiment_analysis_json = excluded.sentiment_analysis_json,
      expanded_analysis_json = excluded.expanded_analysis_json,
      payload_json = excluded.payload_json,
      last_seen_at = excluded.last_seen_at
  `);
  statement.run({
    id: stringifyValue(data.id),
    symbol: stringifyValue(data.symbol),
    quarter: stringifyValue(data.quarter),
    date: stringifyValue(data.date),
    transcript_url: stringifyValue(data.transcript_url),
    audio_url: stringifyValue(data.audio_url),
    short_analysis_json: stringifyJson(data.short_analysis),
    sentiment_analysis_json: stringifyJson(data.sentiment_analysis),
    expanded_analysis_json: stringifyJson(data.expanded_analysis),
    payload_json: stringifyJson(data) ?? "null",
    received_at: nowIso(),
    last_seen_at: nowIso(),
  });
}

function upsertAlerts(db, data) {
  const statement = db.prepare(`
    INSERT INTO alerts_events (
      id, symbol, type, reason, timestamp, meta_json, payload_json, received_at, last_seen_at
    ) VALUES (
      @id, @symbol, @type, @reason, @timestamp, @meta_json, @payload_json, @received_at, @last_seen_at
    )
    ON CONFLICT(id) DO UPDATE SET
      symbol = excluded.symbol,
      type = excluded.type,
      reason = excluded.reason,
      timestamp = excluded.timestamp,
      meta_json = excluded.meta_json,
      payload_json = excluded.payload_json,
      last_seen_at = excluded.last_seen_at
  `);
  statement.run({
    id: stringifyValue(data.id),
    symbol: stringifyValue(data.symbol),
    type: stringifyValue(data.type),
    reason: stringifyValue(data.reason),
    timestamp: stringifyValue(data.timestamp),
    meta_json: stringifyJson(data.meta) ?? "null",
    payload_json: stringifyJson(data) ?? "null",
    received_at: nowIso(),
    last_seen_at: nowIso(),
  });
}

function persistChannelData(db, channel, data) {
  persistMessage(db, "data", channel, data);
  if (channel === "news") {
    upsertNews(db, data);
  } else if (channel === "announcements") {
    upsertAnnouncements(db, data);
  } else if (channel === "earnings") {
    upsertEarnings(db, data);
  } else if (channel === "concalls") {
    upsertConcalls(db, data);
  } else if (channel === "alerts") {
    upsertAlerts(db, data);
  }
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

function channelLogger(db, channel) {
  return (data) => {
    persistChannelData(db, channel, data);
    log(`[${channel}] ${preview(data)}`);
  };
}

const options = parseArgs(process.argv.slice(2));
if (!options.apiKey) {
  console.error("Missing API key. Pass --api-key or set DRISHTI_API_KEY.");
  process.exit(1);
}

const symbols = parseSymbols(options.symbols);
const client = new DrishtiClient({ apiKey: options.apiKey, baseUrl: options.baseUrl });
const db = prepareDb(options.dbPath);
const cleanupTimer = setInterval(() => {
  try {
    pruneOldRows(db);
  } catch (error) {
    log(`[db] retention sweep failed: ${error instanceof Error ? error.message : String(error)}`);
  }
}, RETENTION_SWEEP_INTERVAL_MS);
cleanupTimer.unref?.();

log(`[db] saving websocket data to ${resolve(options.dbPath)}`);
pruneOldRows(db);

const ws = client.websocket({
  webSocketImpl: WebSocket,
  onOpen: () => log("[lifecycle] connected"),
  onClose: (reason) => log(`[lifecycle] closed: ${reason}`),
  onReconnectAttempt: (attempt, delayMs, reason) => {
    log(`[lifecycle] reconnect attempt=${attempt} delay=${delayMs}ms reason=${reason}`);
  },
  onReconnectWarning: (attempt, reason) => {
    log(`[lifecycle] reconnect warning after ${attempt} attempts (${reason})`);
  },
  onNews: channelLogger(db, "news"),
  onAnnouncements: channelLogger(db, "announcements"),
  onEarnings: channelLogger(db, "earnings"),
  onConcalls: channelLogger(db, "concalls"),
  onAlerts: channelLogger(db, "alerts"),
  onError: (event) => {
    if (event.kind === "error") {
      log(`[error] ${event.message}${event.code ? ` (${event.code})` : ""}`);
    }
  },
});

for (const product of DRISHTI_WS_PRODUCTS) {
  await ws.subscribe({ product, symbols, detailed: options.detailed });
  log(`[subscribe] queued ${product} symbols=${symbols.length ? symbols.join(",") : "[]"}`);
}

log("Listening on all channels. Press Ctrl+C to stop.");

process.on("SIGINT", async () => {
  log("Stopping...");
  clearInterval(cleanupTimer);
  await ws.close();
  db.close();
  process.exit(0);
});

for await (const event of ws.events()) {
  if (event.kind === "subscribed") {
    persistMessage(db, event.kind, event.product, event);
    log(
      `[subscribed] product=${event.product} tier=${event.tier} full_feed=${event.fullFeed} symbols=${event.symbols.join(",")}`,
    );
  } else if (event.kind === "data") {
    if (!DRISHTI_WS_PRODUCTS.includes(event.channel)) {
      persistMessage(db, event.kind, event.channel, event);
    }
  } else if (event.kind === "heartbeat") {
    persistMessage(db, event.kind, undefined, event);
  } else if (event.kind === "raw") {
    persistMessage(db, event.kind, undefined, event);
    log("[raw]", event.payload);
  } else if (event.kind === "error") {
    persistMessage(db, event.kind, undefined, event);
  } else {
    persistMessage(db, event.kind, undefined, event);
  }
}
