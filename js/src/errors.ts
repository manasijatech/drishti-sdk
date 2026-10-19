export type ApiErrorBody = unknown;

export class DrishtiWebSocketError extends Error {
  readonly code?: string;

  constructor(message: string, code?: string) {
    super(message);
    this.name = "DrishtiWebSocketError";
    this.code = code;
  }
}

/** Renders an error body for the message: the API's `error.message` when present, else compact JSON. */
function describeBody(body: ApiErrorBody): string {
  if (body !== null && typeof body === "object") {
    const message = (body as { error?: { message?: unknown } }).error?.message;
    if (typeof message === "string") return message;
    try {
      return JSON.stringify(body);
    } catch {
      return String(body);
    }
  }
  return String(body);
}

export class DrishtiApiError extends Error {
  readonly statusCode: number;
  readonly body: ApiErrorBody;

  constructor(statusCode: number, body: ApiErrorBody) {
    super(`Drishti API error ${statusCode}: ${describeBody(body)}`);
    this.name = "DrishtiApiError";
    this.statusCode = statusCode;
    this.body = body;
  }
}
