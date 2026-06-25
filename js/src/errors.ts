export type ApiErrorBody = unknown;

export class DrishtiWebSocketError extends Error {
  readonly code?: string;

  constructor(message: string, code?: string) {
    super(message);
    this.name = "DrishtiWebSocketError";
    this.code = code;
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
