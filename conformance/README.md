# Cross-SDK conformance fixtures

Language-neutral test cases that every Drishti SDK must pass, so the SDKs behave the same
way. Each SDK has a thin runner that loads these JSON files:

| SDK | Runner | Command |
|-----|--------|---------|
| Python | `python/tests/test_conformance.py` | `cd python && pytest` |
| JS / TS | `js/tests/conformance.test.mjs` | `cd js && npm test` |
| PHP | `php/tests/ConformanceTest.php` | `composer test` (repo root) |
| Java | `java/src/test/java/in/manasija/drishti/ConformanceTest.java` | `cd java && ./mvnw test` |
| .NET | `dotnet/tests/Manasija.Drishti.Tests/Conformance/ConformanceTests.cs` | `cd dotnet && dotnet test --filter "TestCategory!=Live"` |

To change SDK behaviour, change the fixture first, watch every runner fail, then update each SDK
until they all pass. CI (`.github/workflows/test.yml`) runs every suite on every PR.

## Layout

- `rest/*.json`: HTTP client calls. Each case describes the request an SDK must send and what it
  must return for a scripted response.
- `helpers/*.json`: exported pure helpers (batch JSONL build and parse).
- `websocket/parse.json`: raw server message → parsed event.
- `websocket/subscribe.json`: subscribe options → wire `subscribe` message.
- `websocket/url.json`: HTTP base URL → WebSocket URL.
- `constants.json`: exported constants that must match exactly.

## Conventions

Names are canonical **snake_case** and match the Python SDK and the API. Runners translate them to
the local idiom:

- Method `get_news` → JS and PHP `getNews`.
- PHP passes arguments as camelCase named arguments (`scrip_codes` → `scripCodes:`, `job_id` → `jobId:`, `file_content` → `fileContent:`), with `positional` spread first.
- Argument `from` → Python `from_`.
- `content_retention` → JS `contentRetention`.
- `file_name` + `file_content` → Python `file_name` + `file_bytes`, JS `filename` + `file` (Blob).
- `poll_interval_ms` → Python `poll_interval` (seconds), JS `pollIntervalMs`.
- `post_daily_summary`'s `body` → Python `request`.
- Parsed WebSocket events use snake_case fields (`full_feed`, `sent_at`). An absent optional field
  is `null`.
- Java maps each call explicitly in `ConformanceTest.invoke`:
  - filters go into the endpoint's params object (`get_news` → `getNews(new NewsParams()...)`);
  - `content_retention` → `ContentRetention.NONE`;
  - `poll_interval_ms` → `Duration`;
  - numeric `job_id` → its string form;
  - `get_symbols` with `format: "json"` → `getSymbolsJson()`;
  - citation PDFs are `byte[]`, compared as UTF-8 text;
  - parsed events are compared through `WebSocketEvent.toJson()`.
- .NET maps each call explicitly in `ConformanceTests.Invoke`, like Java:
  - methods are PascalCase with an `Async` suffix (`get_news` → `GetNewsAsync(new NewsParams { ... })`);
  - batch routes use resource names (`get_batch_jobs_job_id` → `GetBatchJobAsync`, `delete_batch_jobs_job_id` → `CancelBatchJobAsync`), and `post_batch_jobs_file` is `PostBatchJobsAsync`;
  - `alerts` `type` → `AlertsParams.Types`; `poll_interval_ms` → `TimeSpan`; `content_retention` → `ContentRetention.None`;
  - models are compared through their underlying `Json`; parsed events through `WebSocketEvent.ToJson()`.

Runners use API key `test-key`, base URL `https://api.test`, and retry delays of 0.

## REST case fields

| Field | Meaning |
|-------|---------|
| `id` | Unique test name (`area.behaviour`). |
| `client` | Optional overrides: `api_key`, `base_url`, `headers`, `max_retries`. |
| `call.method` | Client method (snake_case). |
| `call.args` | Keyword/params-object arguments. |
| `call.positional` | Positional arguments. When present, JS passes `args` values positionally after them. |
| `response` / `responses` | Scripted reply, or a list of replies in order: `status`, `json` or `text`, `content_type` (default `application/json`), or `network_error: true`. Default: `200 {}`. |
| `expect_request` / `expect_requests` | Requests the SDK must send, exactly that many. `path` is the raw, percent-encoded path. `query` is a decoded multimap that must match exactly. `headers` must be present with these values (other headers are allowed). `json_body` and `multipart` are optional. |
| `expect_request_count` | Number of requests, when their details don't matter. |
| `expect_result` | Deep-equal return value. `null` means the SDK returns null/None. |
| `expect_error` | `{"type": "api_error", "status", "body"}` (an API error exposing the status and parsed body), or `{"type": "invalid_argument"}` (Python `ValueError`, JS `Error`, PHP `InvalidArgumentException`). |

## Adding an SDK

Write a runner that iterates every file above, applies the naming conventions, injects a fake
HTTP transport that records requests and replays `responses`, and asserts as described. Add the
runner to CI. The new SDK conforms when every case passes.
