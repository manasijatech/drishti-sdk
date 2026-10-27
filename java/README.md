# Drishti SDK (Java)

Official Java SDK for the Manasija Drishti API (`/v1`).

It behaves the same as the Python, JS and PHP SDKs. All four pass the shared
[conformance suite](../conformance/README.md).

This SDK provides:
- A synchronous, thread-safe HTTP client with a method for every endpoint and fluent parameter objects
- Low-level `request()`, `get()`, `post()` and related methods for custom calls
- A managed websocket session (`/v1/ws`) that reconnects, resubscribes and detects stalled connections
- Batch job helpers that build JSONL input, submit it, poll and parse results
- Configurable retry and backoff for transient HTTP failures

## Requirements

- Java `17+`
- A valid Drishti API key ([create one](https://platform.manasija.in/developer-portal))
- One runtime dependency, Jackson Databind. HTTP and websockets use the JDK's `java.net.http`.

## Installation

Maven:

```xml
<dependency>
  <groupId>io.github.techmanasija</groupId>
  <artifactId>drishti-sdk</artifactId>
  <version>1.1.1</version>
</dependency>
```

Gradle:

```groovy
implementation("io.github.techmanasija:drishti-sdk:1.1.1")
```

To drop the SDK onto a classpath without a build tool, use `drishti-sdk-1.1.1-all.jar`. It
includes Jackson.

## Quick start

```java
import com.fasterxml.jackson.databind.JsonNode;
import in.manasija.drishti.DrishtiClient;
import in.manasija.drishti.params.NewsParams;

DrishtiClient client = DrishtiClient.fromEnvironment(); // reads DRISHTI_API_KEY

JsonNode news = client.getNews(new NewsParams().symbols("RELIANCE", "TCS").limit(10));
for (JsonNode item : news.path("data")) {
    System.out.println(item.path("symbol").asText() + ": " + item.path("title").asText());
}
```

Response types:
- JSON responses come back as Jackson `JsonNode` trees, with the field names from the [API docs](https://drishti.manasija.in/docs).
- CSV, JSONL and HTML responses come back as `String`; PDF citations come back as `byte[]`.
- An empty response comes back as `null`.

Every request sends `X-API-Key`. Create one `DrishtiClient` and share it: it is immutable and thread-safe.

> **Keep your API key on the server.** Don't embed it in front-end or mobile code.

## Endpoints

Method names match the JS and PHP SDKs. Filters are fluent objects from `in.manasija.drishti.params`;
anything left unset is not sent.

| Area | Methods |
|------|---------|
| News, block deals | `getNews`, `getBlockDeals` |
| Symbols | `getSymbols` (CSV), `getSymbolsJson`, `getSymbolsMetadata` |
| Announcements | `getAnnouncements`, `getAnnouncementsCategories`, `getAnnouncementsAttachments`, `getAnnouncementCitationPdf` |
| Earnings | `getEarnings`, `getEarningsIndex`, `getEarningsDetail`, `getEarningsAttachments`, `getEarningsCitationPdf`, `getEarningsCitationPage`, `getUpcomingEarnings` |
| Concalls | `getConcalls`, `getConcallsIndex`, `getConcallsDetail`, `getConcallsTranscript`, `postConcallsTranscripts`, `getConcallCitationPdf`, `getUpcomingConcalls` |
| Alerts | `getAlerts` |
| Daily summary | `postDailySummary` |
| Account | `getAccount`, `getAccountLimits`, `getAccountUsage`, `getAccountLedger` |
| Batch jobs | `postBatchJobsFile` (alias `postBatchJobs`), `getBatchJobs`, `getBatchJobsJobId`, `deleteBatchJobsJobId`, `getBatchJobsJobIdResults`, `getBatchJobsJobIdResultsParsed`, `waitForBatchJobCompletion`, `submitBatchJobAndWait` |
| Websocket | `websocket()`, `websocketBuilder()` |

```java
JsonNode earnings = client.getEarnings(new EarningsParams().symbols("TCS").quarter("q4_26").detailed(true));
JsonNode concall  = client.getConcallsDetail(new DetailParams("q4_26").symbol("TCS"));
JsonNode alerts   = client.getAlerts(new AlertsParams().types("price_alert", "52w_high").important(true));
JsonNode digest   = client.getAnnouncements(new AnnouncementsParams()
        .symbols("RELIANCE").categories("Board Meeting").from(LocalDate.now().minusDays(7)));
byte[] pdf        = client.getEarningsCitationPdf("EARNINGS_ID", 3);
JsonNode summary  = client.postDailySummary(Map.of(
        "symbols", List.of("RELIANCE", "TCS"), "mode", "news_context", "context_window_days", 7),
        ContentRetention.NONE);
```

`getEarnings` and `getEarningsDetail` copy `earnings_table_extraction` into `earnings_table` when
the API only returns the extraction, so you can always read `earnings_table`. Detailed rows also
include `results_pages`, the 1-indexed PDF pages that hold the results statements; pass one to
`getEarningsCitationPage`.

For a route without a helper method, use `request()` directly:

```java
DrishtiResponse response = client.get("/v1/news", Map.of("limit", 5));
JsonNode body = response.json();
```

## Errors

All SDK exceptions are unchecked and extend `DrishtiException`.

```java
try {
    client.getAccount();
} catch (DrishtiApiException e) {      // HTTP 4xx/5xx after retries
    e.getStatusCode();                  // e.g. 402 (insufficient credits), 403, 429
    e.getBody();                        // JsonNode error body, or raw text
    e.getDetail();                      // the "detail" message
} catch (DrishtiNetworkException e) {   // no response after retries
    e.getCause();                       // the IOException
}
```

A blank API key throws `IllegalArgumentException`.

## Retries, timeouts and redirects

The client retries on `408, 409, 425, 429, 500, 502, 503, 504` and on network errors. It waits
with exponential backoff between attempts, retrying twice by default.

```java
DrishtiClient client = DrishtiClient.builder()
        .apiKey(key)
        .maxRetries(3)
        .initialRetryDelay(Duration.ofMillis(250))
        .maxRetryDelay(Duration.ofSeconds(4))
        .retryMultiplier(2.0)
        .retryOnStatuses(List.of(429, 500, 502, 503, 504))
        .timeout(Duration.ofSeconds(30))
        .header("X-Team", "research")
        .build();
```

Redirects are followed for up to five hops. The PDF citation routes redirect to file storage. When
a redirect leads to another host, the SDK does **not** send your API key there. It also refuses
redirects from HTTPS to HTTP.

The default transport is the JDK `HttpClient`. To use your own HTTP stack (a proxy, a custom SSL
context, a test double), implement `HttpTransport`:

```java
DrishtiClient client = DrishtiClient.builder().apiKey(key).transport(myTransport).build();
```

## Batch jobs

Run many AI summaries in one job. Each JSONL line is a `POST /v1/daily-summary/` body with a `custom_id`.

```java
String jsonl = Params.buildBatchInputJsonl(List.of(
        Map.of("custom_id", "client-001", "portfolio", List.of(Map.of("symbol", "TCS", "exposure", 0.6))),
        Map.of("custom_id", "client-002", "symbols", List.of("RELIANCE"), "mode", "news_context")));

JsonNode job = client.submitBatchJobAndWait(
        new BatchJobRequest("portfolios.jsonl", jsonl).displayName("Nightly").contentRetention(ContentRetention.NONE),
        Duration.ofSeconds(5), Duration.ofMinutes(30));

for (ObjectNode result : client.getBatchJobsJobIdResultsParsed(job.path("id").asText())) {
    System.out.println(result.path("custom_id").asText() + " " + result.path("status").asText());
}
```

## WebSocket session

Websocket streams push events as they are published. They need a paid plan with the websocket
add-on:
- Starter streams up to **100** symbols account-wide, and Pro up to **1,000**.
- Only **Scale** may subscribe with an empty symbol list to receive the whole market.
- Delivered events do not use REST credits.

```java
DrishtiWebSocketSession ws = client.websocket()
        .onSubscribed(ack -> System.out.println("subscribed " + ack.product() + " on " + ack.tier()))
        .onAnnouncements(a -> System.out.println(a.path("symbol").asText() + ": " + a.path("summary").asText()))
        .onAlerts(a -> System.out.println("alert " + a.path("type").asText()))
        .onError(e -> System.err.println(e.message() + " [" + e.code() + "]"));

ws.subscribe("announcements", List.of("RELIANCE", "TCS"));
ws.subscribe("announcements", List.of("RELIANCE"), List.of("Board Meeting", "Dividend"), false); // replaces the one above
ws.subscribe("alerts", List.of("RELIANCE", "TCS"));

ws.run();   // blocks; reconnects and resubscribes until ws.stop()
```

In a web application or service, run it in the background instead:

```java
ws.start();  // daemon thread "drishti-websocket"
// ... on shutdown:
ws.close();  // stops and waits for the thread
```

How the session behaves:
- **Reconnects.** Backoff runs from 1 s to 30 s, doubling, with ±20% jitter. `onReconnectWarning`
  fires (or a warning is logged) after 10 failed attempts in a row at the maximum delay.
- **Resubscribes.** Every subscription is sent again after each reconnect, including ones the
  server rejected.
- **Retries subscribes.** Each subscribe waits up to 10 s for an acknowledgement and is retried up
  to 10 times. The permanent errors `forbidden`, `bad_request` and `unauthorized` fail fast.
- **Detects stalls.** If no message arrives for 90 s, the session reconnects.
- **Runs handlers on the session thread.** Keep handlers fast. An exception thrown in a handler
  is logged and does not drop the connection.
- **Logs lifecycle lines.** Connect, subscribe and reconnect lines, prefixed `[drishti-sdk]`, go to
  `System.Logger` at `INFO`. Silence them with `lifecycleLogging(false)` on `client.websocketBuilder()`.

| Session API | Description |
| --- | --- |
| `client.websocket()` / `client.websocketBuilder()` | Create a session with the client's key, base URL and headers |
| `subscribe(product, symbols[, categories, detailed])` | Send or record a subscription; returns after the ack when called from a handler |
| `onNews`, `onBlockDeals`, `onAnnouncements`, `onEarnings`, `onConcalls`, `onAlerts`, `on(channel, …)` | Payload handlers (`ObjectNode`) |
| `onSubscribed`, `onData`, `onError`, `onHeartbeat`, `onRaw`, `onMessage` | Typed event handlers |
| `onOpen`, `onClose`, `onReconnectAttempt`, `onReconnectWarning` | Lifecycle callbacks |
| `off(handler)` | Remove a handler |
| `run()` / `run(Duration)` / `start()` / `stop()` / `close()` | Lifecycle control |

## Examples

Runnable programs live in [`src/test/java/in/manasija/drishti/examples`](src/test/java/in/manasija/drishti/examples).
`ExamplesTest` runs each one against a local fake API on every build.

| Example | Use case |
| --- | --- |
| `WatchlistDigest` | Morning digest for a watchlist: recent announcements, news, alerts, upcoming earnings |
| `ListenAnnouncements` | Long-running worker that streams announcements, earnings and alerts |
| `PortfolioBatchSummary` | Nightly AI summaries for many portfolios in one batch job |
| `EarningsDeepDive` | One company's quarter: filing, results table, concall transcript, source PDF |
| `AccountHealth` | Pre-flight check of products, websocket tiers, credits and limits |

```bash
DRISHTI_API_KEY=... ./mvnw -q test-compile exec:java -Dexec.classpathScope=test \
    -Dexec.mainClass=in.manasija.drishti.examples.WatchlistDigest -Dexec.args="RELIANCE TCS"
```

## Development

```bash
cd java
./mvnw verify                                         # tests, Javadoc gate, jars
DRISHTI_API_KEY=... ./mvnw test -Dtest=LiveSmokeTest  # optional: real API, uses about 2 credits
```

`./mvnw verify` builds the following:
- runs every shared conformance fixture, the websocket session tests (a port of the PHP suite),
  loopback tests for the real JDK HTTP and websocket transports, and the examples;
- fails on any Javadoc warning (`doclint=all`);
- writes `target/drishti-sdk-1.1.1.jar`, `-sources.jar`, `-javadoc.jar` and `-all.jar`.

The API reference is generated at `target/reports/apidocs/index.html`.

## Releasing to Maven Central

Releases go out through the Sonatype Central Portal under the `io.github.techmanasija` namespace (verified through GitHub, no DNS needed).

**To release:** bump `<version>` in `pom.xml` and merge to `main`.
- `.github/workflows/publish-maven.yml` sees the version change, runs the full build, signs every artifact and uploads them.
- The deployment then waits for a manual **Publish** click at
  [central.sonatype.com → Deployments](https://central.sonatype.com/publishing/deployments).
- A published version can never be changed or deleted, so check the deployment's file list before clicking.
- To retry an upload, run the workflow by hand from the Actions tab.

**Repository secrets the workflow needs:** `CENTRAL_TOKEN_USERNAME` and `CENTRAL_TOKEN_PASSWORD`, a Central Portal
user token (Account → Generate User Token), not your login.

**Signing:** each release is signed with a fresh key that the workflow creates on the CI runner. It publishes
the public key to keyserver.ubuntu.com and keys.openpgp.org, where Central checks signatures, and waits
until the key is downloadable. No private key or passphrase is stored anywhere. To sign with a long-lived
key instead, import it in the "Create release signing key" step.

**Publishing without the manual click:** run the workflow by hand with **auto_publish** checked. It then
publishes straight to Maven Central and waits until the release is live.

To check the signed bundle locally without uploading, run `./mvnw -P release -DskipPublishing=true deploy`.
It needs a `central` server entry in `~/.m2/settings.xml` (dummy values are fine) and a local GPG key
(pass `-Dgpg.keyname=<KEY_ID>` to pick it).
