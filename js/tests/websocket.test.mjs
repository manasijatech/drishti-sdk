import assert from "node:assert/strict";
import test from "node:test";

import {
  DRISHTI_WS_PRODUCTS,
  DrishtiWebSocketSession,
  parseWebSocketMessage,
} from "../dist/index.js";

class ReconnectingWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  static instances = [];

  constructor() {
    this.readyState = ReconnectingWebSocket.CONNECTING;
    this.listeners = new Map();
    this.sentProducts = [];
    this.sentRaw = [];
    this.connectionNumber = ReconnectingWebSocket.instances.length + 1;
    ReconnectingWebSocket.instances.push(this);
    setTimeout(() => {
      this.readyState = ReconnectingWebSocket.OPEN;
      this.emit("open", {});
    }, 10);
  }

  addEventListener(name, handler) {
    const handlers = this.listeners.get(name) ?? [];
    handlers.push(handler);
    this.listeners.set(name, handlers);
  }

  removeEventListener(name, handler) {
    const handlers = this.listeners.get(name) ?? [];
    this.listeners.set(name, handlers.filter((candidate) => candidate !== handler));
  }

  send(raw) {
    const message = JSON.parse(raw);
    this.sentRaw.push(raw);
    this.sentProducts.push(message.product);
    if (this.connectionNumber === 1 && message.product === "block-deals") {
      queueMicrotask(() => this.close(1011, "test disconnect"));
      return;
    }
    queueMicrotask(() => {
      this.emit("message", {
        data: JSON.stringify({
          status: "subscribed",
          product: message.product,
          tier: "scale",
          full_feed: true,
          symbols: [],
          detailed: true,
        }),
      });
    });
  }

  close(code = 1000, reason = "") {
    if (this.readyState === ReconnectingWebSocket.CLOSED) {
      return;
    }
    this.readyState = ReconnectingWebSocket.CLOSED;
    this.emit("close", { code, reason });
  }

  emit(name, event) {
    for (const handler of this.listeners.get(name) ?? []) {
      handler(event);
    }
  }
}

test("sends announcement categories and reads them from the ack", async () => {
  ReconnectingWebSocket.instances = [];
  const session = new DrishtiWebSocketSession({
    apiKey: "test-key",
    webSocketImpl: ReconnectingWebSocket,
  });
  await session.subscribe({
    product: "announcements",
    categories: [" Dividend ", "Board Meeting", "Dividend"],
  });
  await new Promise((resolve) => setTimeout(resolve, 30));
  const sent = JSON.parse(ReconnectingWebSocket.instances[0].sentRaw.at(-1));
  assert.deepEqual(sent.categories, ["Dividend", "Board Meeting"]);

  const event = parseWebSocketMessage(JSON.stringify({
    status: "subscribed",
    product: "announcements",
    tier: "scale",
    full_feed: true,
    symbols: [],
    categories: ["Dividend", "Board Meeting"],
    detailed: true,
  }));
  assert.equal(event.kind, "subscribed");
  assert.deepEqual(event.categories, ["Dividend", "Board Meeting"]);
  await session.close();
});

test("normalizes the block deals wire contract", () => {
  const subscribed = parseWebSocketMessage(
    JSON.stringify({
      status: "subscribed",
      product: "block-deals",
      tier: "scale",
      full_feed: true,
    }),
  );
  const data = parseWebSocketMessage(
    JSON.stringify({ channel: "block-deals", data: { id: "deal-1" } }),
  );

  assert.equal(subscribed.kind, "subscribed");
  assert.equal(subscribed.product, "block_deals");
  assert.equal(data.kind, "data");
  assert.equal(data.channel, "block_deals");
});

test("reconnects when the socket closes during a subscription", async () => {
  ReconnectingWebSocket.instances = [];
  let markConnected;
  const connected = new Promise((resolve) => {
    markConnected = resolve;
  });
  const session = new DrishtiWebSocketSession({
    apiKey: "test-key",
    webSocketImpl: ReconnectingWebSocket,
    reconnectInitialDelayMs: 100,
    reconnectJitterRatio: 0,
    onOpen: markConnected,
  });

  for (const product of DRISHTI_WS_PRODUCTS) {
    await session.subscribe({ product });
  }

  let timeoutId;
  const reconnectTimeout = new Promise((_, reject) => {
    timeoutId = setTimeout(() => reject(new Error("reconnect timed out")), 2_000);
  });
  try {
    await Promise.race([connected, reconnectTimeout]);

    assert.equal(ReconnectingWebSocket.instances.length, 2);
    assert.deepEqual(ReconnectingWebSocket.instances[0].sentProducts, ["news", "block-deals"]);
    assert.deepEqual(ReconnectingWebSocket.instances[1].sentProducts, [
      "news",
      "block-deals",
      "announcements",
      "earnings",
      "concalls",
      "alerts",
    ]);
  } finally {
    clearTimeout(timeoutId);
    await session.close();
  }
});

class RejectingWebSocket extends ReconnectingWebSocket {
  send(raw) {
    const message = JSON.parse(raw);
    this.sentRaw.push(raw);
    this.sentProducts.push(message.product);
    queueMicrotask(() => {
      this.emit("message", {
        data: JSON.stringify({
          error: `WebSocket addon '${message.product}' is not enabled for this key`,
          code: "forbidden",
        }),
      });
    });
  }
}

test("keeps reconnecting with backoff when every subscription is permanently rejected", async () => {
  ReconnectingWebSocket.instances = [];
  const reconnectDelays = [];
  const session = new DrishtiWebSocketSession({
    apiKey: "test-key",
    webSocketImpl: RejectingWebSocket,
    reconnectInitialDelayMs: 100,
    reconnectBackoffMultiplier: 2,
    reconnectJitterRatio: 0,
    subscribeRetryInitialDelayMs: 10,
    onReconnectAttempt: (_attempt, delay) => {
      reconnectDelays.push(delay);
    },
  });

  try {
    await session.subscribe({ product: "announcements" });
    await new Promise((resolve) => setTimeout(resolve, 800));

    const sockets = ReconnectingWebSocket.instances;
    assert.ok(sockets.length >= 3, `expected reconnects, got ${sockets.length} socket(s)`);
    for (const socket of sockets.slice(0, -1)) {
      assert.deepEqual(socket.sentProducts, ["announcements"]);
      assert.equal(socket.readyState, ReconnectingWebSocket.CLOSED);
    }
    assert.deepEqual(reconnectDelays.slice(0, 2), [200, 400]);
  } finally {
    await session.close();
  }
});
