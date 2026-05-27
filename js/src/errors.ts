export type ApiErrorBody = unknown;

export class DrishtiWebSocketError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "DrishtiWebSocketError";
  }
}

export class DrishtiApiError extends Error {
  readonly statusCode: number;
  readonly body: ApiErrorBody;

  constructor(statusCode: number, body: ApiErrorBody) {
    super(`Drishti API error ${statusCode}: ${String(body)}`);
    this.name = "DrishtiApiError";
    this.statusCode = statusCode;
    this.body = body;
  }
}
