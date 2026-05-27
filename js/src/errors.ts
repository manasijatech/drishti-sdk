export type ApiErrorBody = unknown;

export class MarketStackWebSocketError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "MarketStackWebSocketError";
  }
}

export class MarketStackApiError extends Error {
  readonly statusCode: number;
  readonly body: ApiErrorBody;

  constructor(statusCode: number, body: ApiErrorBody) {
    super(`Market-Stack API error ${statusCode}: ${String(body)}`);
    this.name = "MarketStackApiError";
    this.statusCode = statusCode;
    this.body = body;
  }
}
