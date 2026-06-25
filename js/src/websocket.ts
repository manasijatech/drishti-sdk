import { DEFAULT_BASE_URL } from "./client.js";
import { DrishtiWebSocketError } from "./errors.js";
import {
  DRISHTI_WS_PRODUCTS,
} from "./websocket-types.js";
import type {
  ChannelDataHandler,
  DrishtiWebSocketProduct,
  DataPayloadByChannel,
  SubscribeOptions,
  SubscribedEvent,
  WebSocketEvent,
  WebSocketHandler,
} from "./websocket-types.js";

export {
  DRISHTI_WS_PRODUCTS,
  type DrishtiWebSocketProduct,
  type DataEvent,
  type DataPayloadByChannel,
  type ErrorEvent,
  type HeartbeatEvent,
  type KnownDataEvent,
  type RawEvent,
  type SubscribeOptions,
  type SubscribedEvent,
  type UnknownDataEvent,
  type ChannelDataHandler,
  type WebSocketEvent,
  type WebSocketHandler,
} from "./websocket-types.js";

export type DrishtiWebSocketSessionOptions = Readonly<{
  apiKey: string;
  baseUrl?: string;
  headers?: Record<string, string>;
  webSocketImpl?: typeof WebSocket;
  reconnectInitialDelayMs?: number;
  reconnectMaxDelayMs?: number;
  reconnectBackoffMultiplier?: number;
  reconnectJitterRatio?: number;
  reconnectWarnAfterAttempts?: number;
  heartbeatTimeoutMs?: number;
  heartbeatCheckIntervalMs?: number;
  subscribeAckTimeoutMs?: number;
  subscribeMaxAttempts?: number;
  subscribeRetryInitialDelayMs?: number;
  subscribeRetryMaxDelayMs?: number;
  subscribeRetryBackoffMultiplier?: number;
  enableLifecycleLogging?: boolean;
  onSubscribed?: WebSocketHandler;
  onData?: WebSocketHandler;
  onNews?: ChannelDataHandler<"news">;
  onAnnouncements?: ChannelDataHandler<"announcements">;
  onEarnings?: ChannelDataHandler<"earnings">;
  onConcalls?: ChannelDataHandler<"concalls">;
  onAlerts?: ChannelDataHandler<"alerts">;
  onError?: WebSocketHandler;
  onMessage?: WebSocketHandler;
  onOpen?: () => void | Promise<void>;
  onClose?: (reason: string) => void | Promise<void>;
  onReconnectAttempt?: (attempt: number, delayMs: number, reason: string) => void | Promise<void>;
  onReconnectWarning?: (attempt: number, reason: string) => void | Promise<void>;
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
  if (record.type === "heartbeat") {
    return {
      kind: "heartbeat",
      sentAt: String(record.sent_at ?? ""),
    };
  }
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
      if (isDrishtiWebSocketProduct(channel)) {
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
    if (isDrishtiWebSocketProduct(channel)) {
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

function isDrishtiWebSocketProduct(value: string): value is DrishtiWebSocketProduct {
  return (DRISHTI_WS_PRODUCTS as readonly string[]).includes(value);
}

function subscribeMessage(options: SubscribeOptions): string {
  return JSON.stringify({
    op: "subscribe",
    product: options.product,
    symbols: normalizeSymbols(options.symbols),
    detailed: options.detailed !== false,
  });
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

type PendingSubscribe = Readonly<{
  product: string;
  resolve: (event: SubscribedEvent) => void;
  reject: (error: DrishtiWebSocketError) => void;
  timeoutId: ReturnType<typeof setTimeout>;
}>;

export class DrishtiWebSocketSession {
  private readonly apiKey: string;
  private readonly baseUrl: string;
  private readonly headers: Record<string, string>;
  private readonly webSocketImpl: typeof WebSocket;
  private readonly reconnectInitialDelayMs: number;
  private readonly reconnectMaxDelayMs: number;
  private readonly reconnectBackoffMultiplier: number;
  private readonly reconnectJitterRatio: number;
  private readonly reconnectWarnAfterAttempts: number;
  private readonly heartbeatTimeoutMs: number;
  private readonly heartbeatCheckIntervalMs: number;
  private readonly subscribeAckTimeoutMs: number;
  private readonly subscribeMaxAttempts: number;
  private readonly subscribeRetryInitialDelayMs: number;
  private readonly subscribeRetryMaxDelayMs: number;
  private readonly subscribeRetryBackoffMultiplier: number;
  private readonly enableLifecycleLogging: boolean;
  private readonly onOpen?: () => void | Promise<void>;
  private readonly onClose?: (reason: string) => void | Promise<void>;
  private readonly onReconnectAttempt?: (attempt: number, delayMs: number, reason: string) => void | Promise<void>;
  private readonly onReconnectWarning?: (attempt: number, reason: string) => void | Promise<void>;
  private readonly handlers = new Map<string, WebSocketHandler[]>();
  private readonly channelHandlers = new Map<DrishtiWebSocketProduct, ChannelDataHandler<DrishtiWebSocketProduct>[]>();
  private readonly subscriptions = new Map<DrishtiWebSocketProduct, SubscribeOptions>();
  private socket: WebSocket | null = null;
  private readonly queue: WebSocketEvent[] = [];
  private readonly waiters: Array<{
    resolve: (event: WebSocketEvent) => void;
    reject: (error: Error) => void;
  }> = [];
  private manuallyClosed = false;
  private maintainingConnection = false;
  private pendingSubscribe: PendingSubscribe | null = null;
  private subscribeChain: Promise<void> = Promise.resolve();
  private lastHeartbeatAt: number | null = null;
  private lastMessageAt: number | null = null;
  private heartbeatWatchdog: ReturnType<typeof setInterval> | null = null;

  constructor(options: DrishtiWebSocketSessionOptions) {
    if (!options.apiKey || options.apiKey.trim().length === 0) {
      throw new Error("DrishtiWebSocketSession requires a non-empty apiKey");
    }
    this.apiKey = options.apiKey;
    this.baseUrl = options.baseUrl ?? DEFAULT_BASE_URL;
    this.headers = { ...(options.headers ?? {}) };
    this.webSocketImpl = options.webSocketImpl ?? WebSocket;
    this.reconnectInitialDelayMs = Math.max(100, options.reconnectInitialDelayMs ?? 1_000);
    this.reconnectMaxDelayMs = Math.max(this.reconnectInitialDelayMs, options.reconnectMaxDelayMs ?? 30_000);
    this.reconnectBackoffMultiplier = Math.max(1, options.reconnectBackoffMultiplier ?? 2);
    this.reconnectJitterRatio = Math.min(1, Math.max(0, options.reconnectJitterRatio ?? 0.2));
    this.reconnectWarnAfterAttempts = Math.max(1, options.reconnectWarnAfterAttempts ?? 10);
    this.heartbeatTimeoutMs = Math.max(0, options.heartbeatTimeoutMs ?? 90_000);
    this.heartbeatCheckIntervalMs = Math.max(1_000, options.heartbeatCheckIntervalMs ?? 15_000);
    this.subscribeAckTimeoutMs = Math.max(1_000, options.subscribeAckTimeoutMs ?? 10_000);
    this.subscribeMaxAttempts = Math.max(1, options.subscribeMaxAttempts ?? 10);
    this.subscribeRetryInitialDelayMs = Math.max(100, options.subscribeRetryInitialDelayMs ?? 1_000);
    this.subscribeRetryMaxDelayMs = Math.max(
      this.subscribeRetryInitialDelayMs,
      options.subscribeRetryMaxDelayMs ?? 30_000,
    );
    this.subscribeRetryBackoffMultiplier = Math.max(1, options.subscribeRetryBackoffMultiplier ?? 2);
    this.enableLifecycleLogging = options.enableLifecycleLogging !== false;
    this.onOpen = options.onOpen;
    this.onClose = options.onClose;
    this.onReconnectAttempt = options.onReconnectAttempt;
    this.onReconnectWarning = options.onReconnectWarning;
    if (options.onSubscribed) {
      this.on("subscribed", options.onSubscribed);
    }
    if (options.onData) {
      this.on("data", options.onData);
    }
    if (options.onNews) {
      this.on("news", options.onNews);
    }
    if (options.onAnnouncements) {
      this.on("announcements", options.onAnnouncements);
    }
    if (options.onEarnings) {
      this.on("earnings", options.onEarnings);
    }
    if (options.onConcalls) {
      this.on("concalls", options.onConcalls);
    }
    if (options.onAlerts) {
      this.on("alerts", options.onAlerts);
    }
    if (options.onError) {
      this.on("error", options.onError);
    }
    if (options.onMessage) {
      this.on("message", options.onMessage);
    }
    this.startHeartbeatWatchdog();
    this.startConnectionMaintenance("initial");
  }

  get connected(): boolean {
    return this.socket !== null && this.socket.readyState === this.webSocketImpl.OPEN;
  }

  get lastHeartbeatReceivedAt(): number | null {
    return this.lastHeartbeatAt;
  }

  get lastMessageReceivedAt(): number | null {
    return this.lastMessageAt;
  }

  on(eventName: "data" | "subscribed" | "error" | "heartbeat" | "message" | "raw", handler: WebSocketHandler): void;
  on<K extends DrishtiWebSocketProduct>(channel: K, handler: ChannelDataHandler<K>): void;
  on(eventName: string, handler: WebSocketHandler | ChannelDataHandler<DrishtiWebSocketProduct>): void {
    if (isDrishtiWebSocketProduct(eventName)) {
      this.addChannelListener(eventName, handler as ChannelDataHandler<DrishtiWebSocketProduct>);
      return;
    }
    this.addEventListener(eventName, handler as WebSocketHandler);
  }

  off(eventName: "data" | "subscribed" | "error" | "heartbeat" | "message" | "raw", handler: WebSocketHandler): void;
  off<K extends DrishtiWebSocketProduct>(channel: K, handler: ChannelDataHandler<K>): void;
  off(eventName: string, handler: WebSocketHandler | ChannelDataHandler<DrishtiWebSocketProduct>): void {
    if (isDrishtiWebSocketProduct(eventName)) {
      this.removeChannelListener(eventName, handler as ChannelDataHandler<DrishtiWebSocketProduct>);
      return;
    }
    this.removeEventListener(eventName, handler as WebSocketHandler);
  }

  onNews(handler: ChannelDataHandler<"news">): void {
    this.addChannelListener("news", handler as ChannelDataHandler<DrishtiWebSocketProduct>);
  }

  onAnnouncements(handler: ChannelDataHandler<"announcements">): void {
    this.addChannelListener("announcements", handler as ChannelDataHandler<DrishtiWebSocketProduct>);
  }

  onEarnings(handler: ChannelDataHandler<"earnings">): void {
    this.addChannelListener("earnings", handler as ChannelDataHandler<DrishtiWebSocketProduct>);
  }

  onConcalls(handler: ChannelDataHandler<"concalls">): void {
    this.addChannelListener("concalls", handler as ChannelDataHandler<DrishtiWebSocketProduct>);
  }

  onAlerts(handler: ChannelDataHandler<"alerts">): void {
    this.addChannelListener("alerts", handler as ChannelDataHandler<DrishtiWebSocketProduct>);
  }

  private addEventListener(eventName: string, handler: WebSocketHandler): void {
    const existing = this.handlers.get(eventName) ?? [];
    existing.push(handler);
    this.handlers.set(eventName, existing);
  }

  private removeEventListener(eventName: string, handler: WebSocketHandler): void {
    const existing = this.handlers.get(eventName) ?? [];
    const next = existing.filter((item) => item !== handler);
    if (next.length === 0) {
      this.handlers.delete(eventName);
      return;
    }
    this.handlers.set(eventName, next);
  }

  private addChannelListener(
    channel: DrishtiWebSocketProduct,
    handler: ChannelDataHandler<DrishtiWebSocketProduct>,
  ): void {
    const existing = this.channelHandlers.get(channel) ?? [];
    existing.push(handler);
    this.channelHandlers.set(channel, existing);
  }

  private removeChannelListener(
    channel: DrishtiWebSocketProduct,
    handler: ChannelDataHandler<DrishtiWebSocketProduct>,
  ): void {
    const existing = this.channelHandlers.get(channel) ?? [];
    const next = existing.filter((item) => item !== handler);
    if (next.length === 0) {
      this.channelHandlers.delete(channel);
      return;
    }
    this.channelHandlers.set(channel, next);
  }

  private startConnectionMaintenance(reason: string): void {
    if (this.manuallyClosed || this.maintainingConnection || this.connected) {
      return;
    }
    void this.maintainConnection(reason);
  }

  private async maintainConnection(reason: string): Promise<void> {
    if (this.manuallyClosed || this.maintainingConnection || this.connected) {
      return;
    }
    this.maintainingConnection = true;
    let attempt = 0;
    let delay = this.reconnectInitialDelayMs;
    while (!this.manuallyClosed && !this.connected) {
      attempt += 1;
      if (attempt > 1) {
        await this.onReconnectAttempt?.(attempt - 1, delay, reason);
        await new Promise((resolve) => setTimeout(resolve, this.withJitter(delay)));
      } else if (reason !== "initial" && reason !== "subscribe" && reason !== "events") {
        this.logLifecycle(`reconnecting (${reason})`);
      }
      try {
        const socket = await this.openSocket();
        this.socket = socket;
        this.lastMessageAt = Date.now();
        this.logLifecycle(`connected; resubscribing ${this.subscriptions.size} product(s)`);
        await this.resubscribeAll();
        await this.onOpen?.();
        attempt = 0;
        delay = this.reconnectInitialDelayMs;
        break;
      } catch (error) {
        if (this.socket !== null) {
          try {
            this.socket.close();
          } catch {
            /* ignore */
          }
          this.socket = null;
        }
        const message = error instanceof Error ? error.message : String(error);
        this.logLifecycle(`connection setup failed: ${message}`);
        if (delay >= this.reconnectMaxDelayMs && attempt >= this.reconnectWarnAfterAttempts) {
          await this.emitReconnectWarning(attempt, reason);
          attempt = 0;
        }
        delay = Math.min(
          Math.floor(delay * this.reconnectBackoffMultiplier),
          this.reconnectMaxDelayMs,
        );
      }
    }
    this.maintainingConnection = false;
  }

  private async emitReconnectWarning(attempt: number, reason: string): Promise<void> {
    if (this.onReconnectWarning) {
      await this.onReconnectWarning(attempt, reason);
      return;
    }
    console.warn(
      `[drishti-sdk] WebSocket still unable to connect after ${attempt} attempts (${reason}); retrying`,
    );
  }

  private async openSocket(): Promise<WebSocket> {
    const socket = this.createSocket();
    await new Promise<void>((resolve, reject) => {
      const onOpen = (): void => {
        cleanup();
        resolve();
      };
      const onError = (): void => {
        cleanup();
        reject(new DrishtiWebSocketError("WebSocket connection failed"));
      };
      const onClose = (event: CloseEvent): void => {
        cleanup();
        const code = "code" in event ? event.code : 0;
        reject(new DrishtiWebSocketError(`WebSocket closed before open (code ${code})`));
      };
      const cleanup = (): void => {
        socket.removeEventListener("open", onOpen);
        socket.removeEventListener("error", onError);
        socket.removeEventListener("close", onClose);
      };
      socket.addEventListener("open", onOpen);
      socket.addEventListener("error", onError);
      socket.addEventListener("close", onClose);
    });
    this.attachSocketHandlers(socket);
    return socket;
  }

  private attachSocketHandlers(socket: WebSocket): void {
    socket.addEventListener("message", (messageEvent) => {
      this.lastMessageAt = Date.now();
      const raw = typeof messageEvent.data === "string" ? messageEvent.data : String(messageEvent.data);
      void this.handleIncoming(raw);
    });
    socket.addEventListener("error", () => {
      if (this.socket !== socket || this.manuallyClosed) {
        return;
      }
      void this.forceReconnect("WebSocket error");
    });
    socket.addEventListener("close", () => {
      if (this.socket !== socket) {
        return;
      }
      this.socket = null;
      this.clearPendingSubscribe();
      this.lastMessageAt = null;
      void this.onClose?.("WebSocket closed");
      if (this.manuallyClosed) {
        this.rejectWaiters("WebSocket closed");
        return;
      }
      this.startConnectionMaintenance("WebSocket closed");
    });
  }

  private createSocket(): WebSocket {
    const url = buildWebSocketUrl(this.baseUrl, this.apiKey);
    const impl = this.webSocketImpl as unknown as {
      new (url: string, protocols?: string | string[], options?: { headers?: Record<string, string> }): WebSocket;
    };
    const headers = { ...this.headers, "X-API-Key": this.apiKey };
    try {
      return new impl(url, undefined, { headers });
    } catch {
      return new this.webSocketImpl(url);
    }
  }

  async close(): Promise<void> {
    this.manuallyClosed = true;
    this.maintainingConnection = false;
    this.stopHeartbeatWatchdog();
    this.clearPendingSubscribe();
    if (this.socket === null) {
      this.rejectWaiters("WebSocket closed");
      return;
    }
    this.socket.close();
    this.socket = null;
    this.rejectWaiters("WebSocket closed");
  }

  async subscribe(options: SubscribeOptions): Promise<void> {
    const normalized: SubscribeOptions = {
      product: options.product,
      symbols: normalizeSymbols(options.symbols),
      detailed: options.detailed !== false,
    };
    this.subscriptions.set(options.product, normalized);
    this.startConnectionMaintenance("subscribe");
    if (!this.connected || this.socket === null) {
      return;
    }
    await this.enqueueSubscribeWork(() =>
      this.sendSubscribeWithRetry(normalized, "subscribe"),
    );
  }

  private enqueueSubscribeWork(work: () => Promise<void>): Promise<void> {
    const next = this.subscribeChain.then(work);
    this.subscribeChain = next.catch(() => undefined);
    return next;
  }

  private logLifecycle(message: string): void {
    if (!this.enableLifecycleLogging) {
      return;
    }
    console.info(`[drishti-sdk] ${message}`);
  }

  private clearPendingSubscribe(): void {
    if (this.pendingSubscribe === null) {
      return;
    }
    clearTimeout(this.pendingSubscribe.timeoutId);
    this.pendingSubscribe = null;
  }

  private async sendSubscribeWithRetry(
    options: SubscribeOptions,
    reason: "subscribe" | "reconnect",
  ): Promise<void> {
    let attempt = 0;
    let delay = this.subscribeRetryInitialDelayMs;
    while (attempt < this.subscribeMaxAttempts) {
      if (!this.connected || this.socket === null) {
        throw new DrishtiWebSocketError("WebSocket is not connected");
      }
      attempt += 1;
      try {
        const ack = await this.sendSubscribeAndWaitForAck(options);
        this.logLifecycle(
          `subscribed ${options.product} tier=${ack.tier} full_feed=${ack.fullFeed} (${reason}, attempt ${attempt})`,
        );
        return;
      } catch (error) {
        const wsError = error instanceof DrishtiWebSocketError
          ? error
          : new DrishtiWebSocketError(error instanceof Error ? error.message : String(error));
        this.logLifecycle(
          `subscribe failed product=${options.product} attempt=${attempt}/${this.subscribeMaxAttempts} (${reason}): ${wsError.message}${wsError.code ? ` [${wsError.code}]` : ""}`,
        );
        if (!this.connected || this.socket === null) {
          throw wsError;
        }
        if (attempt >= this.subscribeMaxAttempts) {
          throw wsError;
        }
        await sleep(this.withJitter(delay));
        delay = Math.min(
          Math.floor(delay * this.subscribeRetryBackoffMultiplier),
          this.subscribeRetryMaxDelayMs,
        );
      }
    }
  }

  private async sendSubscribeAndWaitForAck(options: SubscribeOptions): Promise<SubscribedEvent> {
    if (!this.connected || this.socket === null) {
      throw new DrishtiWebSocketError("WebSocket is not connected");
    }
    this.clearPendingSubscribe();
    return await new Promise<SubscribedEvent>((resolve, reject) => {
      const timeoutId = setTimeout(() => {
        if (this.pendingSubscribe?.product === options.product) {
          this.clearPendingSubscribe();
          reject(new DrishtiWebSocketError(`Subscribe acknowledgement timed out for ${options.product}`));
        }
      }, this.subscribeAckTimeoutMs);
      this.pendingSubscribe = {
        product: options.product,
        timeoutId,
        resolve: (event) => {
          this.clearPendingSubscribe();
          resolve(event);
        },
        reject: (error) => {
          this.clearPendingSubscribe();
          reject(error);
        },
      };
      this.socket!.send(subscribeMessage(options));
    });
  }

  private async resubscribeAll(): Promise<void> {
    if (!this.connected || this.socket === null || this.subscriptions.size === 0) {
      return;
    }
    for (const options of this.subscriptions.values()) {
      await this.sendSubscribeWithRetry(options, "reconnect");
    }
  }

  private resolvePendingSubscribe(event: WebSocketEvent): boolean {
    if (this.pendingSubscribe === null) {
      return false;
    }
    if (event.kind === "subscribed" && event.product === this.pendingSubscribe.product) {
      this.pendingSubscribe.resolve(event);
      return true;
    }
    if (event.kind === "error") {
      this.pendingSubscribe.reject(new DrishtiWebSocketError(event.message, event.code));
      return true;
    }
    return false;
  }

  private async dispatch(event: WebSocketEvent): Promise<void> {
    const names = [event.kind, "message"];
    for (const name of names) {
      const handlers = this.handlers.get(name) ?? [];
      for (const handler of handlers) {
        await handler(event);
      }
    }
    if (event.kind !== "data" || !isDrishtiWebSocketProduct(event.channel)) {
      return;
    }
    const channelHandlers = this.channelHandlers.get(event.channel) ?? [];
    const payload = event.data as DataPayloadByChannel[typeof event.channel];
    for (const handler of channelHandlers) {
      await handler(payload);
    }
  }

  private async handleIncoming(raw: string): Promise<void> {
    const event = parseWebSocketMessage(raw);
    if (event.kind === "heartbeat") {
      this.lastHeartbeatAt = Date.now();
      await this.dispatch(event);
      return;
    }
    const resolvedPending = this.resolvePendingSubscribe(event);
    if (!resolvedPending) {
      await this.dispatch(event);
    } else if (event.kind === "subscribed") {
      await this.dispatch(event);
    } else if (event.kind === "error") {
      await this.dispatch(event);
    }
    if (resolvedPending) {
      return;
    }
    const waiter = this.waiters.shift();
    if (waiter) {
      waiter.resolve(event);
      return;
    }
    this.queue.push(event);
  }

  private rejectWaiters(message: string): void {
    const error = new DrishtiWebSocketError(message);
    this.clearPendingSubscribe();
    while (this.waiters.length > 0) {
      this.waiters.shift()?.reject(error);
    }
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
    if (this.manuallyClosed) {
      throw new DrishtiWebSocketError("WebSocket closed");
    }
    return await new Promise<WebSocketEvent>((resolve, reject) => {
      this.waiters.push({ resolve, reject });
    });
  }

  async *events(): AsyncGenerator<WebSocketEvent> {
    this.startConnectionMaintenance("events");
    while (!this.manuallyClosed) {
      yield await this.nextEvent();
    }
  }

  private startHeartbeatWatchdog(): void {
    if (this.heartbeatTimeoutMs <= 0 || this.heartbeatWatchdog !== null) {
      return;
    }
    this.heartbeatWatchdog = setInterval(() => {
      if (this.manuallyClosed || !this.connected || this.socket === null || this.lastMessageAt === null) {
        return;
      }
      const silentForMs = Date.now() - this.lastMessageAt;
      if (silentForMs < this.heartbeatTimeoutMs) {
        return;
      }
      void this.forceReconnect(`heartbeat timeout after ${silentForMs}ms`);
    }, this.heartbeatCheckIntervalMs);
  }

  private stopHeartbeatWatchdog(): void {
    if (this.heartbeatWatchdog === null) {
      return;
    }
    clearInterval(this.heartbeatWatchdog);
    this.heartbeatWatchdog = null;
  }

  private async forceReconnect(reason: string): Promise<void> {
    if (this.manuallyClosed || this.socket === null) {
      return;
    }
    const staleSocket = this.socket;
    this.socket = null;
    this.lastMessageAt = null;
    this.clearPendingSubscribe();
    this.logLifecycle(`forcing reconnect (${reason})`);
    await this.onClose?.(reason);
    try {
      const terminable = staleSocket as WebSocket & { terminate?: () => void };
      if (typeof terminable.terminate === "function") {
        terminable.terminate();
      } else {
        staleSocket.close();
      }
    } catch {
      /* ignore */
    }
    this.startConnectionMaintenance(reason);
  }
}

export async function* streamProduct(options: {
  apiKey: string;
  product: DrishtiWebSocketProduct;
  symbols?: readonly string[];
  detailed?: boolean;
  baseUrl?: string;
  headers?: Record<string, string>;
  webSocketImpl?: typeof WebSocket;
}): AsyncGenerator<Record<string, unknown>> {
  const session = new DrishtiWebSocketSession({
    apiKey: options.apiKey,
    baseUrl: options.baseUrl,
    headers: options.headers,
    webSocketImpl: options.webSocketImpl,
  });
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
