import type {
  Alert,
  AnnouncementDetail,
  AnnouncementListItem,
  Concall,
  ConcallListItem,
  EarningsDetail,
  EarningsListItem,
  NewsItem,
} from "./types.js";

export const DRISHTI_WS_PRODUCTS = [
  "news",
  "announcements",
  "earnings",
  "concalls",
  "alerts",
] as const;

export type DrishtiWebSocketProduct = (typeof DRISHTI_WS_PRODUCTS)[number];

export type SubscribeOptions = Readonly<{
  product: DrishtiWebSocketProduct;
  symbols?: readonly string[];
  detailed?: boolean;
}>;

export type SubscribedEvent = Readonly<{
  kind: "subscribed";
  product: string;
  tier: string;
  fullFeed: boolean;
  symbols: string[];
  detailed: boolean;
}>;

export type DataPayloadByChannel = Readonly<{
  news: NewsItem;
  announcements: AnnouncementDetail | AnnouncementListItem;
  earnings: EarningsDetail | EarningsListItem;
  concalls: Concall | ConcallListItem;
  alerts: Alert;
}>;

export type KnownDataEvent = {
  [K in DrishtiWebSocketProduct]: Readonly<{
    kind: "data";
    channel: K;
    data: DataPayloadByChannel[K];
  }>;
}[DrishtiWebSocketProduct];

export type UnknownDataEvent = Readonly<{
  kind: "data";
  channel: string;
  data: Record<string, unknown>;
}>;

export type DataEvent = KnownDataEvent | UnknownDataEvent;

export type ErrorEvent = Readonly<{
  kind: "error";
  message: string;
  code?: string;
}>;

export type RawEvent = Readonly<{
  kind: "raw";
  payload: Record<string, unknown>;
}>;

export type WebSocketEvent = SubscribedEvent | DataEvent | ErrorEvent | RawEvent;

export type WebSocketHandler = (event: WebSocketEvent) => void | Promise<void>;
