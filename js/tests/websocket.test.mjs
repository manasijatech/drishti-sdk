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
