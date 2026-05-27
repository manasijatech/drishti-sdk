import { DEFAULT_BASE_URL } from "./client.js";
import { DrishtiWebSocketError } from "./errors.js";
import {
  ALPHA_WS_PRODUCTS,
} from "./websocket-types.js";
import type {
  AlphaWebSocketProduct,
  DataPayloadByChannel,
  SubscribeOptions,
  WebSocketEvent,
  WebSocketHandler,
} from "./websocket-types.js";

export {
  ALPHA_WS_PRODUCTS,
  type AlphaWebSocketProduct,
  type DataEvent,
  type DataPayloadByChannel,
  type ErrorEvent,
  type KnownDataEvent,
  type RawEvent,
  type SubscribeOptions,
  type SubscribedEvent,
  type UnknownDataEvent,
  type WebSocketEvent,
  type WebSocketHandler,
} from "./websocket-types.js";

export type AlphaWebSocketSessionOptions = Readonly<{
  apiKey: string;
  baseUrl?: string;
  headers?: Record<string, string>;
  webSocketImpl?: typeof WebSocket;
  autoReconnect?: boolean;
  reconnectInitialDelayMs?: number;
  reconnectMaxDelayMs?: number;
  reconnectBackoffMultiplier?: number;
  reconnectJitterRatio?: number;
  onSubscribed?: WebSocketHandler;
  onData?: WebSocketHandler;
  onError?: WebSocketHandler;
  onMessage?: WebSocketHandler;
  onOpen?: () => void | Promise<void>;
  onClose?: (reason: string) => void | Promise<void>;
  onReconnectAttempt?: (attempt: number, delayMs: number, reason: string) => void | Promise<void>;
}>;

function normalizeSymbols(symbols: readonly string[] | undefined): string[] {
  const seen = new Set<string>();
  for (const symbol of symbols ?? []) {
    const token = String(symbol).trim().toUpperCase();
    seen.add(token);
  }
  return Array.from(seen);
}

export function buildWebSocketUrl(baseUrl: string, apiKey?: string): string {
  const base = baseUrl.replace(/\/+$/, "");
  const wsBase = base.replace(/^https:/, "wss:").replace(/^http:/, "ws:");
  const url = new URL(`${wsBase}/v1/ws`);
  if (apiKey) {
    url.searchParams.set("api_key", apiKey);
  }
  return url.toString();
}

export function parseWebSocketMessage(raw: string): WebSocketEvent {
  let payload: unknown;
  try {
    payload = JSON.parse(raw) as unknown;
  } catch {
    return { kind: "error", message: "Invalid JSON" };
  }
  if (!payload || typeof payload !== "object" || Array.isArray(payload)) {
    return { kind: "error", message: "Expected a JSON object" };
  }
  const record = payload as Record<string, unknown>;
  if (record.status === "subscribed") {
    const symbols = Array.isArray(record.symbols)
      ? record.symbols.map((item) => String(item))
      : [];
    return {
      kind: "subscribed",
      product: String(record.product ?? ""),
      tier: String(record.tier ?? ""),
      fullFeed: Boolean(record.full_feed),
      symbols,
      detailed: record.detailed !== false,
    };
  }
  if (record.error !== undefined) {
    return {
      kind: "error",
      message: String(record.error),
      code: record.code === undefined || record.code === null ? undefined : String(record.code),
    };
  }
  if (record.channel !== undefined) {
    const channel = String(record.channel);
    const data = record.data;
    if (data && typeof data === "object" && !Array.isArray(data)) {
      if (isAlphaWebSocketProduct(channel)) {
        return {
          kind: "data",
          channel,
          data: data as DataPayloadByChannel[typeof channel],
        };
      }
      return {
        kind: "data",
        channel,
        data: data as Record<string, unknown>,
      };
    }
    if (isAlphaWebSocketProduct(channel)) {
      return {
        kind: "data",
        channel,
        data: { raw: data } as unknown as DataPayloadByChannel[typeof channel],
      };
    }
    return {
      kind: "data",
      channel,
      data: { raw: data },
    };
  }
  return { kind: "raw", payload: record };
}

function isAlphaWebSocketProduct(value: string): value is AlphaWebSocketProduct {
  return (ALPHA_WS_PRODUCTS as readonly string[]).includes(value);
}

function subscribeMessage(options: SubscribeOptions): string {
  return JSON.stringify({
    op: "subscribe",
    product: options.product,
    symbols: normalizeSymbols(options.symbols),
    detailed: options.detailed !== false,
  });
}

export class AlphaWebSocketSession {
  private readonly apiKey: string;
  private readonly baseUrl: string;
  private readonly headers: Record<string, string>;
  private readonly webSocketImpl: typeof WebSocket;
  private readonly autoReconnect: boolean;
  private readonly reconnectInitialDelayMs: number;
  private readonly reconnectMaxDelayMs: number;
  private readonly reconnectBackoffMultiplier: number;
  private readonly reconnectJitterRatio: number;
  private readonly onOpen?: () => void | Promise<void>;
  private readonly onClose?: (reason: string) => void | Promise<void>;
  private readonly onReconnectAttempt?: (attempt: number, delayMs: number, reason: string) => void | Promise<void>;
  private readonly handlers = new Map<string, WebSocketHandler[]>();
  private readonly subscriptions = new Map<AlphaWebSocketProduct, SubscribeOptions>();
  private socket: WebSocket | null = null;
  private readonly queue: WebSocketEvent[] = [];
  private readonly waiters: Array<{
    resolve: (event: WebSocketEvent) => void;
    reject: (error: Error) => void;
  }> = [];
  private closed = false;
  private manuallyClosed = false;
  private reconnecting = false;

  constructor(options: AlphaWebSocketSessionOptions) {
    if (!options.apiKey || options.apiKey.trim().length === 0) {
      throw new Error("AlphaWebSocketSession requires a non-empty apiKey");
    }
    this.apiKey = options.apiKey;
    this.baseUrl = options.baseUrl ?? DEFAULT_BASE_URL;
    this.headers = { ...(options.headers ?? {}) };
    this.webSocketImpl = options.webSocketImpl ?? WebSocket;
    this.autoReconnect = options.autoReconnect ?? false;
    this.reconnectInitialDelayMs = Math.max(100, options.reconnectInitialDelayMs ?? 1_000);
    this.reconnectMaxDelayMs = Math.max(this.reconnectInitialDelayMs, options.reconnectMaxDelayMs ?? 30_000);
    this.reconnectBackoffMultiplier = Math.max(1, options.reconnectBackoffMultiplier ?? 2);
    this.reconnectJitterRatio = Math.min(1, Math.max(0, options.reconnectJitterRatio ?? 0.2));
    this.onOpen = options.onOpen;
    this.onClose = options.onClose;
    this.onReconnectAttempt = options.onReconnectAttempt;
    if (options.onSubscribed) {
      this.on("subscribed", options.onSubscribed);
    }
    if (options.onData) {
      this.on("data", options.onData);
    }
    if (options.onError) {
      this.on("error", options.onError);
    }
    if (options.onMessage) {
      this.on("message", options.onMessage);
    }
  }

  get connected(): boolean {
    return this.socket !== null && this.socket.readyState === this.webSocketImpl.OPEN;
  }

  on(eventName: string, handler: WebSocketHandler): void {
    const existing = this.handlers.get(eventName) ?? [];
    existing.push(handler);
    this.handlers.set(eventName, existing);
  }

  async connect(): Promise<void> {
    if (this.connected) {
      return;
    }
    this.manuallyClosed = false;
    this.closed = false;
    const socket = await this.openSocket();
    this.socket = socket;
    await this.resubscribeAll();
    await this.onOpen?.();
  }

  private async openSocket(): Promise<WebSocket> {
    const url = buildWebSocketUrl(this.baseUrl);
    const socket = this.createSocket(url);
    await new Promise<void>((resolve, reject) => {
      const onOpen = (): void => {
        cleanup();
        resolve();
      };
      const onError = (): void => {
        cleanup();
        reject(new DrishtiWebSocketError("WebSocket connection failed"));
      };
      const cleanup = (): void => {
        socket.removeEventListener("open", onOpen);
        socket.removeEventListener("error", onError);
      };
      socket.addEventListener("open", onOpen);
      socket.addEventListener("error", onError);
    });
    this.attachSocketHandlers(socket);
    return socket;
  }

  private attachSocketHandlers(socket: WebSocket): void {
    socket.addEventListener("message", (messageEvent) => {
      const raw = typeof messageEvent.data === "string" ? messageEvent.data : String(messageEvent.data);
      void this.handleIncoming(raw);
    });
    socket.addEventListener("close", () => {
      this.socket = null;
      void this.onClose?.("WebSocket closed");
      if (this.manuallyClosed) {
        this.closed = true;
        this.rejectWaiters("WebSocket closed");
        return;
      }
      if (!this.autoReconnect) {
        this.closed = true;
        this.rejectWaiters("WebSocket closed");
        return;
      }
      void this.reconnectLoop("WebSocket closed");
    });
  }

  private createSocket(url: string): WebSocket {
    const impl = this.webSocketImpl as unknown as {
      new (url: string, protocols?: string | string[], options?: { headers?: Record<string, string> }): WebSocket;
    };
    const headers = { ...this.headers, "X-API-Key": this.apiKey };
    try {
      return new impl(url, undefined, { headers });
    } catch {
      // Browser-compatible constructor does not support custom headers.
      // Fallback to query-param auth for compatibility.
      return new this.webSocketImpl(buildWebSocketUrl(this.baseUrl, this.apiKey));
    }
  }

  async close(): Promise<void> {
    this.manuallyClosed = true;
    this.reconnecting = false;
    if (this.socket === null) {
      this.closed = true;
      this.rejectWaiters("WebSocket closed");
      return;
    }
    this.closed = true;
    this.socket.close();
    this.socket = null;
    this.rejectWaiters("WebSocket closed");
  }

  async subscribe(options: SubscribeOptions): Promise<void> {
    this.subscriptions.set(options.product, {
      product: options.product,
      symbols: normalizeSymbols(options.symbols),
      detailed: options.detailed !== false,
    });
    if (!this.connected || this.socket === null) {
      throw new DrishtiWebSocketError("WebSocket is not connected; call connect() first");
    }
    this.socket.send(subscribeMessage(options));
  }

  private async dispatch(event: WebSocketEvent): Promise<void> {
    const names = [event.kind, "message"];
    for (const name of names) {
      const handlers = this.handlers.get(name) ?? [];
      for (const handler of handlers) {
        await handler(event);
      }
    }
  }

  private async handleIncoming(raw: string): Promise<void> {
    const event = parseWebSocketMessage(raw);
    await this.dispatch(event);
    const waiter = this.waiters.shift();
    if (waiter) {
      waiter.resolve(event);
      return;
    }
    this.queue.push(event);
  }

  private rejectWaiters(message: string): void {
    const error = new DrishtiWebSocketError(message);
    while (this.waiters.length > 0) {
      this.waiters.shift()?.reject(error);
    }
  }

  private async resubscribeAll(): Promise<void> {
    if (!this.connected || this.socket === null) {
      return;
    }
    for (const options of this.subscriptions.values()) {
      this.socket.send(subscribeMessage(options));
    }
  }

  private async reconnectLoop(reason: string): Promise<void> {
    if (this.reconnecting || this.manuallyClosed) {
      return;
    }
    this.reconnecting = true;
    let attempt = 0;
    let delay = this.reconnectInitialDelayMs;
    while (!this.manuallyClosed) {
      attempt += 1;
      await this.onReconnectAttempt?.(attempt, delay, reason);
      await new Promise((resolve) => setTimeout(resolve, this.withJitter(delay)));
      try {
        const socket = await this.openSocket();
        this.socket = socket;
        this.closed = false;
        await this.resubscribeAll();
        await this.onOpen?.();
        this.reconnecting = false;
        return;
      } catch {
        delay = Math.min(
          Math.floor(delay * this.reconnectBackoffMultiplier),
          this.reconnectMaxDelayMs,
        );
      }
    }
    this.reconnecting = false;
  }

  private withJitter(delayMs: number): number {
    if (this.reconnectJitterRatio <= 0) {
      return delayMs;
    }
    const spread = Math.floor(delayMs * this.reconnectJitterRatio);
    const offset = Math.floor(Math.random() * (spread * 2 + 1)) - spread;
    return Math.max(0, delayMs + offset);
  }

  private async nextEvent(): Promise<WebSocketEvent> {
    const queued = this.queue.shift();
    if (queued) {
      return queued;
    }
    if (this.closed) {
      throw new DrishtiWebSocketError("WebSocket closed");
    }
    return await new Promise<WebSocketEvent>((resolve, reject) => {
      this.waiters.push({ resolve, reject });
    });
  }

  async *events(): AsyncGenerator<WebSocketEvent> {
    if (!this.connected) {
      throw new DrishtiWebSocketError("WebSocket is not connected; call connect() first");
    }
    while (!this.closed) {
      yield await this.nextEvent();
    }
  }

  async run(): Promise<void> {
    for await (const _event of this.events()) {
      // Callbacks run during handleIncoming; keep the read loop alive.
    }
  }
}

export async function* streamProduct(options: {
  apiKey: string;
  product: AlphaWebSocketProduct;
  symbols?: readonly string[];
  detailed?: boolean;
  baseUrl?: string;
  headers?: Record<string, string>;
  webSocketImpl?: typeof WebSocket;
}): AsyncGenerator<Record<string, unknown>> {
  const session = new AlphaWebSocketSession({
    apiKey: options.apiKey,
    baseUrl: options.baseUrl,
    headers: options.headers,
    webSocketImpl: options.webSocketImpl,
  });
  await session.connect();
  try {
    await session.subscribe({
      product: options.product,
      symbols: options.symbols,
      detailed: options.detailed,
    });
    for await (const event of session.events()) {
      if (event.kind === "data") {
        yield event.data;
      }
    }
  } finally {
    await session.close();
  }
}
