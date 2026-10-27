# Drishti SDK (PHP)

Official PHP SDK for the Manasija Drishti API (`/v1`).

It behaves the same as the Python and JS SDKs. All three pass the shared
[conformance suite](../conformance/README.md).

This SDK provides:
- A synchronous HTTP client with named-argument endpoint methods
- A low-level `request()` method for custom calls
- A blocking WebSocket client for real-time streams (`/v1/ws`) that reconnects automatically
- Configurable retry and backoff for transient HTTP failures

## Requirements

- PHP `8.2+` with `ext-json`
- A valid Drishti API key ([create one](https://platform.manasija.in/))

## Installation

```bash
composer require manasija/drishti-sdk
```

## Quick Start

```php
use Drishti\DrishtiClient;

$client = new DrishtiClient(apiKey: getenv('DRISHTI_API_KEY'));

$news = $client->getNews(symbols: ['RELIANCE', 'TCS'], limit: 10);
$blockDeals = $client->getBlockDeals(symbols: ['RECLTD'], exchange: 'NSE', limit: 20);
$universe = $client->getSymbols(format: 'json');

$summary = $client->postDailySummary([
    'symbols' => ['RELIANCE', 'TCS'],
    'mode' => 'news_context',
    'context_window_days' => 7,
]);

echo count($news['data']);
```

- JSON responses come back as associative arrays, in the same shape as the [API docs](https://developers.manasija.in/docs).
- CSV, PDF and JSONL responses come back as strings.
- An empty response comes back as `null`.
- Every request sends `X-API-Key`.

> **Keep your API key on the server.** Don't embed it in front-end code.

## Endpoints

Method names match the JS SDK. Arguments are camelCase named arguments, and list filters take arrays.

| Area | Methods |
|------|---------|
| News, block deals | `getNews`, `getBlockDeals` |
| Symbols | `getSymbols`, `getSymbolsMetadata` |
| Announcements | `getAnnouncements`, `getAnnouncementsCategories`, `getAnnouncementsAttachments`, `getAnnouncementCitationPdf` |
| Earnings | `getEarnings`, `getEarningsIndex`, `getEarningsDetail`, `getEarningsAttachments`, `getEarningsCitationPdf`, `getEarningsCitationPage`, `getUpcomingEarnings` |
| Concalls | `getConcalls`, `getConcallsIndex`, `getConcallsDetail`, `getConcallsTranscript`, `postConcallsTranscripts`, `getConcallCitationPdf`, `getUpcomingConcalls` |
| Alerts | `getAlerts` |
| Daily summary | `postDailySummary` |
| Account | `getAccount`, `getAccountLimits`, `getAccountUsage`, `getAccountLedger` |
| Batch jobs | `postBatchJobsFile`, `getBatchJobs`, `getBatchJobsJobId`, `deleteBatchJobsJobId`, `getBatchJobsJobIdResults`, `getBatchJobsJobIdResultsParsed`, `waitForBatchJobCompletion`, `submitBatchJobAndWait` |

```php
$earnings = $client->getEarnings(symbols: ['TCS'], quarter: 'q4_26', detailed: true);
$detail = $client->getConcallsDetail(quarter: 'q4_26', symbol: 'TCS');
$alerts = $client->getAlerts(type: ['price_alert', '52w_high'], important: true);
$pdf = $client->getEarningsCitationPdf('EARNINGS_ID', page: 3);   // PDF bytes
```

Detailed earnings rows include `results_pages`, the 1-indexed PDF pages that hold the results statements. Pass one as `page` to `getEarningsCitationPdf` or `getEarningsCitationPage`.

For a route without a helper method, use `request()` directly:

```php
$data = $client->request('GET', '/v1/news', query: ['limit' => 5]);
```

## Errors

```php
use Drishti\Exception\DrishtiApiException;

try {
    $client->getAccount();
} catch (DrishtiApiException $e) {
    $e->getStatusCode();  // e.g. 403
    $e->getBody();        // parsed JSON error body, or raw text
}
```

A blank API key throws `InvalidArgumentException`. Network failures surface as the PSR-18 client's exceptions once retries run out.

## Retries and HTTP client

The client retries on `408, 409, 425, 429, 500, 502, 503, 504` and on network errors. It waits with exponential backoff between attempts, retrying twice by default.

```php
$client = new DrishtiClient(
    apiKey: $key,
    maxRetries: 3,
    initialDelayMs: 250,
    maxDelayMs: 4000,
    multiplier: 2.0,
    retryOnStatuses: [429, 500, 502, 503, 504],
    timeout: 30.0,
);
```

Guzzle is the default HTTP client. You can pass any PSR-18 client (with PSR-17 factories) instead:

```php
$client = new DrishtiClient(apiKey: $key, httpClient: $psr18Client, requestFactory: $factory, streamFactory: $factory);
```

## Batch jobs

```php
use Drishti\Params;

$jsonl = Params::buildBatchInputJsonl([
    ['custom_id' => 'tcs', 'symbols' => ['TCS'], 'mode' => 'news_context'],
    ['custom_id' => 'infy', 'symbols' => ['INFY']],
]);

$job = $client->submitBatchJobAndWait(fileName: 'input.jsonl', fileContent: $jsonl, pollIntervalMs: 5000);
$results = $client->getBatchJobsJobIdResultsParsed($job['id']);
```

## WebSocket

PHP-FPM requests can't hold a socket open. Run the listener as a long-lived CLI worker instead, for example under supervisord, systemd, or a framework queue or console command. `run()` blocks:

```php
$session = $client->websocket(
    onOpen: fn (string $reason) => print("connected\n"),
    onClose: fn (string $reason) => print("disconnected: {$reason}\n"),
);

$session->onNews(fn (array $item) => print($item['title'] . "\n"));
$session->onAnnouncements(fn (array $item) => handleAnnouncement($item));
$session->on('error', fn ($event) => error_log(json_encode($event->toArray())));

$session->subscribe('news', symbols: ['RELIANCE', 'TCS']);
$session->subscribe('announcements', categories: ['Board Meeting']);

$session->run();          // until $session->stop()
// $session->run(300.0);  // or for a fixed number of seconds
```

The session behaves like the Python and JS SDKs:

- **Reconnects** with exponential backoff and jitter (`reconnectInitialDelay`, `reconnectMaxDelay`, `reconnectBackoffMultiplier`, `reconnectJitterRatio`). After `reconnectWarnAfterAttempts` failures at the maximum delay it logs a warning or calls `onReconnectWarning`.
- **Resubscribes** every subscription after each reconnect. Subscriptions the server rejects are kept and retried on the next reconnect.
- **Retries a subscribe** on the same connection with its own backoff (`subscribeMaxAttempts`, `subscribeAckTimeout`). It doesn't retry permanent rejections (`forbidden`, `bad_request`, `unauthorized`).
- **Treats the connection as dead** if no message arrives within `heartbeatTimeout` seconds (90 by default), and reconnects.
- **Supports `subscribe()` from inside a handler.** On a live connection it sends the subscribe immediately and throws `DrishtiWebSocketException` (with `getErrorCode()`) if the server rejects it.

Event kinds: `subscribed`, `data`, `error`, `heartbeat`, `raw`, plus `message` for every event. Each kind has a typed event class in `Drishti\WebSocket\Event`. Channel handlers (`onNews`, `onBlockDeals`, `onAnnouncements`, `onEarnings`, `onConcalls`, `onAlerts`, or `on('<product>')`) receive the payload array. Remove a handler with `off()`.

To get lifecycle logs, pass any PSR-3 logger: `$client->websocket(logger: $logger)`. See [`examples/ws_listen.php`](examples/ws_listen.php) for a complete worker.

## Laravel

The SDK works with Laravel 12 and 13. It supports both Guzzle 7 and Guzzle 8, so it uses whichever version your app already has. Register the client once in a service provider:

```php
// app/Providers/AppServiceProvider.php
use Drishti\DrishtiClient;

public function register(): void
{
    $this->app->singleton(DrishtiClient::class, fn () => new DrishtiClient(
        apiKey: config('services.drishti.key'),
    ));
}
```

Add `'drishti' => ['key' => env('DRISHTI_API_KEY')]` to `config/services.php`, then inject `DrishtiClient` into controllers, jobs or commands.

For the WebSocket, use an Artisan command and keep it running with Supervisor, like a queue worker. Laravel's logger works as the session logger:

```php
// routes/console.php
use Drishti\DrishtiClient;
use Illuminate\Support\Facades\Artisan;
use Illuminate\Support\Facades\Log;

Artisan::command('drishti:listen', function (DrishtiClient $client) {
    $session = $client->websocket(logger: Log::channel('stack'));
    $session->onAnnouncements(fn (array $item) => Log::info('announcement', $item));
    $session->subscribe('announcements', symbols: ['RELIANCE', 'TCS']);
    $session->run();
});
```

## Development

The library's `composer.json` lives at the repository root.

```bash
composer install
composer test      # PHPUnit: conformance fixtures + WebSocket session tests
composer analyse   # PHPStan level 9
```
