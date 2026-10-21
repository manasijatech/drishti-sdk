# Drishti SDK (.NET)

Official .NET SDK for the Manasija Drishti API (`/v1`).

It behaves the same as the Python, JS, PHP and Java SDKs. All of them pass the shared
[conformance suite](https://github.com/manasijatech/drishti-sdk/tree/main/conformance).

This SDK provides:
- An async, thread-safe client with a method for every endpoint, typed parameter records and typed response models
- `RequestAsync` for custom calls
- A managed WebSocket session (`/v1/ws`) that reconnects, resubscribes and detects stalled connections
- Batch job helpers that build JSONL input, submit it, poll and parse results
- `IServiceCollection.AddDrishtiClient(...)` for ASP.NET Core and the generic host, built on `IHttpClientFactory`
- Configurable retry and backoff for transient HTTP failures, and structured logging through `ILogger`

## Requirements

- .NET 8 or later, or any runtime that supports .NET Standard 2.0 (.NET Framework 4.6.2+, Unity, Xamarin)
- A valid Drishti API key ([create one](https://platform.manasija.in/developer-portal))

## Installation

```bash
dotnet add package Manasija.Drishti
```

## Quick start

```csharp
using Manasija.Drishti;
using Manasija.Drishti.Params;

using var client = DrishtiClient.FromEnvironment(); // reads DRISHTI_API_KEY

var news = await client.GetNewsAsync(new NewsParams { Symbols = ["RELIANCE", "TCS"], Limit = 10 });
foreach (var item in news!.Data)
{
    Console.WriteLine($"{item.Symbol}: {item.Title}");
}
```

Create one `DrishtiClient` and share it: it is thread-safe. Every request sends `X-API-Key`.

> **Keep your API key on the server.** Don't embed it in front-end, desktop or mobile apps you ship to users.

### Dependency injection

```csharp
// appsettings.json: { "Drishti": { "ApiKey": "...", "Timeout": "00:00:30" } }
builder.Services.AddDrishtiClient(builder.Configuration);

app.MapGet("/news/{symbol}", async (string symbol, IDrishtiClient drishti) =>
    (await drishti.GetNewsAsync(new NewsParams { Symbols = [symbol] }))?.Data);
```

Options are validated at startup (a missing `ApiKey` fails fast). `AddDrishtiClient` returns the
`IHttpClientBuilder`, so you can add handlers such as a proxy or Polly policies.

## Response models

Each model is a typed view over the JSON the API returned:

- Properties are read on demand. A missing, `null` or mistyped value returns `null`, so new API fields never break you.
- `model.Json` is the full `JsonElement`. Use `model.Deserialize<MyType>()` to map it onto your own class (snake_case names).
- Free-form fields (`EarningsTable`, `ExtractedInformation`, `Metadata`, …) are `JsonElement?`.
- CSV, JSONL and HTML responses come back as `string`; PDF citations as `byte[]`.
- An empty response (`204`, or an empty body) comes back as `null`.

## Endpoints

Filters are records from `Manasija.Drishti.Params`; anything left unset is not sent.

| Area | Methods |
|------|---------|
| News, block deals | `GetNewsAsync`, `GetBlockDealsAsync` |
| Symbols | `GetSymbolsAsync` (CSV), `GetSymbolsJsonAsync`, `GetSymbolsMetadataAsync` |
| Announcements | `GetAnnouncementsAsync`, `GetAnnouncementsCategoriesAsync`, `GetAnnouncementsAttachmentsAsync`, `GetAnnouncementCitationPdfAsync` |
| Earnings | `GetEarningsAsync`, `GetEarningsIndexAsync`, `GetEarningsDetailAsync`, `GetEarningsAttachmentsAsync`, `GetEarningsCitationPdfAsync`, `GetEarningsCitationPageAsync`, `GetUpcomingEarningsAsync` |
| Concalls | `GetConcallsAsync`, `GetConcallsIndexAsync`, `GetConcallsDetailAsync`, `GetConcallsTranscriptAsync`, `PostConcallsTranscriptsAsync`, `GetConcallCitationPdfAsync`, `GetUpcomingConcallsAsync` |
| Alerts | `GetAlertsAsync` |
| Daily summary | `PostDailySummaryAsync` |
| Account | `GetAccountAsync`, `GetAccountLimitsAsync`, `GetAccountUsageAsync`, `GetAccountLedgerAsync` |
| Batch jobs | `PostBatchJobsAsync`, `GetBatchJobsAsync`, `GetBatchJobAsync`, `CancelBatchJobAsync`, `GetBatchJobResultsAsync`, `GetBatchJobResultsParsedAsync`, `WaitForBatchJobCompletionAsync`, `SubmitBatchJobAndWaitAsync` |
| WebSocket | `CreateWebSocketSession` |

Every method takes an optional `CancellationToken`.

### Daily summary

```csharp
var summary = await client.PostDailySummaryAsync(
    new DailySummaryRequest
    {
        Portfolio = [new PortfolioPosition("TCS") { Exposure = 0.6 }, new PortfolioPosition("INFY")],
        Mode = "exposure",
    },
    ContentRetention.None); // ask the API not to retain the request
Console.WriteLine(summary!.Summary);
```

The body is sent as given: unset fields are omitted and the API applies its own defaults. Any
JSON-serializable object (or a `JsonElement`) also works as the body.

### Batch jobs

```csharp
using Manasija.Drishti.Batch;

var jsonl = BatchJsonl.BuildInput(
[
    new DailySummaryLine("watchlist-1") { Symbols = ["TCS", "INFY"], Mode = "news_context" },
    new DailySummaryLine("portfolio-1") { Portfolio = [new PortfolioPosition("RELIANCE") { Exposure = 1 }] },
]);

var job = await client.SubmitBatchJobAndWaitAsync(
    new BatchJobRequest("nightly.jsonl", jsonl) { DisplayName = "Nightly run" },
    pollInterval: TimeSpan.FromSeconds(5),
    timeout: TimeSpan.FromMinutes(30));

if (job.Status == "succeeded" || job.Status == "partial")
{
    foreach (var line in await client.GetBatchJobResultsParsedAsync(job.Id!))
    {
        Console.WriteLine($"{line.CustomId}: {line.Summary}");
    }
}
```

## Errors

| Exception | When |
|-----------|------|
| `DrishtiApiException` | The API returned 400 or above after retries. Exposes `StatusCode`, `Detail` (`error.message`, else `detail`, else the body), `ErrorCode`, `BodyJson`, `BodyText`, `Headers` and `RetryAfter`. |
| `DrishtiNetworkException` | No response after all retries (connection failure, DNS, timeout). |
| `DrishtiWebSocketException` | A subscribe was rejected or timed out, or the socket is not connected. `Code` carries the server code (`forbidden`, `bad_request`, …). |
| `DrishtiException` | Base class of the above; also invalid JSON on a success response and batch wait timeouts. |
| `ArgumentException` | Invalid input, such as a blank API key or an empty id list. |

```csharp
try
{
    await client.GetAccountAsync();
}
catch (DrishtiApiException ex) when (ex.StatusCode == 429)
{
    await Task.Delay(ex.RetryAfter ?? TimeSpan.FromMinutes(1));
}
```

## Retries and timeouts

| Option | Default |
|--------|---------|
| `MaxRetries` | `2` (3 attempts in total) |
| `InitialRetryDelay`, `RetryMultiplier`, `MaxRetryDelay` | `300 ms`, `2`, `5 s` (no jitter) |
| `RetryOnStatuses` | `408, 409, 425, 429, 500, 502, 503, 504`, plus network errors and timeouts |
| `Timeout` | `60 s` per response |

Redirects are followed up to 5 hops. The API key and custom headers are dropped on cross-origin
redirects (citation PDFs redirect to file storage), and HTTPS-to-HTTP redirects are refused.

## WebSocket

```csharp
using Manasija.Drishti.WebSocket;

await using var session = client.CreateWebSocketSession();
session
    .OnAnnouncements(a => Console.WriteLine($"{a.Symbol}: {a.Summary}"))
    .OnError(e => { Console.Error.WriteLine($"{e.Code}: {e.Message}"); return Task.CompletedTask; });

await session.SubscribeAsync(new SubscribeOptions("announcements")
{
    Symbols = ["RELIANCE", "TCS"],
    Categories = ["Dividend"],
});
await session.RunAsync(stoppingToken); // until cancelled
```

Or read events as a stream:

```csharp
await session.StartAsync();
await foreach (var e in session.ReadEventsAsync(stoppingToken))
{
    if (e is DataEvent data) Console.WriteLine($"{data.Channel}: {data.Data}");
}
```

Products: `news`, `block_deals`, `announcements`, `earnings`, `concalls`, `alerts`.

Session behaviour, shared with the other SDKs:
- Reconnects with backoff (1 s → 30 s, ×2, ±20% jitter) and re-sends every subscription.
- Reconnects when nothing arrives for 90 s (checked every 15 s).
- Waits 10 s for each subscribe acknowledgement and retries up to 10 times. `forbidden`, `bad_request` and `unauthorized` fail at once.
- Handlers run one at a time, off the receive loop. A handler that throws is logged and the session carries on.

Every setting is on `DrishtiWebSocketOptions`, passed through `CreateWebSocketSession(o => ...)`.

## Development

```bash
cd dotnet
dotnet test --filter "TestCategory!=Live"   # conformance, unit and WebSocket session tests
DRISHTI_API_KEY=... dotnet test --filter "TestCategory=Live"   # against the real API
dotnet pack src/Manasija.Drishti -c Release
```
