package in.manasija.drishti;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ArrayNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.exception.DrishtiApiException;
import in.manasija.drishti.exception.DrishtiException;
import in.manasija.drishti.exception.DrishtiNetworkException;
import in.manasija.drishti.http.HttpTransport;
import in.manasija.drishti.http.JdkHttpTransport;
import in.manasija.drishti.http.TransportRequest;
import in.manasija.drishti.http.TransportResponse;
import in.manasija.drishti.internal.Json;
import in.manasija.drishti.params.AlertsParams;
import in.manasija.drishti.params.AnnouncementsParams;
import in.manasija.drishti.params.BatchJobRequest;
import in.manasija.drishti.params.BlockDealsParams;
import in.manasija.drishti.params.ConcallsParams;
import in.manasija.drishti.params.DetailParams;
import in.manasija.drishti.params.EarningsParams;
import in.manasija.drishti.params.IndexParams;
import in.manasija.drishti.params.NewsParams;
import in.manasija.drishti.params.QueryParams;
import in.manasija.drishti.params.SymbolsMetadataParams;
import in.manasija.drishti.params.TranscriptLookup;
import in.manasija.drishti.params.TranscriptParams;
import in.manasija.drishti.params.UpcomingConcallsParams;
import in.manasija.drishti.params.UpcomingEarningsParams;
import in.manasija.drishti.websocket.DrishtiWebSocketSession;
import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.Arrays;
import java.util.Collection;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Objects;
import java.util.Set;
import java.util.StringJoiner;
import java.util.UUID;
import java.util.concurrent.TimeUnit;

/**
 * Synchronous client for the Drishti API ({@code /v1}).
 *
 * <p>Create one client per API key and share it: it is immutable and thread-safe.
 *
 * <pre>{@code
 * DrishtiClient client = DrishtiClient.create(System.getenv("DRISHTI_API_KEY"));
 *
 * JsonNode news = client.getNews(new NewsParams().symbols("RELIANCE", "TCS").limit(10));
 * for (JsonNode item : news.path("data")) {
 *     System.out.println(item.path("symbol").asText() + " " + item.path("title").asText());
 * }
 * }</pre>
 *
 * <h2>Responses</h2>
 * JSON responses are returned as Jackson {@link JsonNode} trees whose field names match the API
 * reference. CSV, JSONL and HTML responses are returned as {@link String}s, PDF citations as
 * {@code byte[]}, and empty responses as {@code null}.
 *
 * <p>Redirects are followed, up to five hops. When a redirect leads to another host, such as file
 * storage for a citation PDF, the API key and custom headers are not sent there. A redirect from
 * HTTPS to HTTP is refused.
 *
 * <h2>Errors and retries</h2>
 * The client retries network failures and the statuses in {@link #DEFAULT_RETRY_ON_STATUSES} with
 * exponential backoff, twice by default. After that:
 *
 * <ul>
 *   <li>an HTTP error status throws {@link DrishtiApiException};</li>
 *   <li>a network failure throws {@link DrishtiNetworkException}.</li>
 * </ul>
 *
 * <h2>Streaming</h2>
 * {@link #websocket()} opens a websocket session that pushes events as they happen. It uses the
 * same API key and base URL.
 *
 * @see <a href="https://drishti.manasija.in/docs">Drishti API documentation</a>
 * @since 1.0.0
 */
public final class DrishtiClient {

    /** The production API base URL. */
    public static final String DEFAULT_BASE_URL = "https://developers.manasija.in";

    /** HTTP statuses retried by default: timeouts, conflicts, rate limits and gateway errors. */
    public static final List<Integer> DEFAULT_RETRY_ON_STATUSES = List.of(408, 409, 425, 429, 500, 502, 503, 504);

    /** Environment variable read by {@link #fromEnvironment()} for the API key. */
    public static final String API_KEY_ENV = "DRISHTI_API_KEY";

    /** Environment variable read by {@link #fromEnvironment()} for an optional base URL override. */
    public static final String BASE_URL_ENV = "DRISHTI_BASE_URL";

    private static final String API_KEY_HEADER = "X-API-Key";
    private static final String USER_AGENT = "drishti-sdk-java/1.1.2";
    private static final int MAX_REDIRECTS = 5;

    private final String apiKey;
    private final String baseUrl;
    private final Map<String, String> headers;
    private final HttpTransport transport;
    private final int maxRetries;
    private final Duration initialRetryDelay;
    private final Duration maxRetryDelay;
    private final double retryMultiplier;
    private final Set<Integer> retryOnStatuses;
    private final Duration timeout;

    private DrishtiClient(Builder builder) {
        if (builder.apiKey == null || builder.apiKey.isBlank()) {
            throw new IllegalArgumentException("DrishtiClient requires a non-empty apiKey");
        }
        this.apiKey = builder.apiKey;
        this.baseUrl = trimTrailingSlashes(Objects.requireNonNull(builder.baseUrl, "baseUrl"));
        this.headers = Collections.unmodifiableMap(withoutApiKey(builder.headers));
        this.timeout = builder.timeout;
        this.transport = builder.transport != null ? builder.transport : new JdkHttpTransport(builder.timeout);
        this.maxRetries = Math.max(0, builder.maxRetries);
        this.initialRetryDelay = builder.initialRetryDelay;
        this.maxRetryDelay = builder.maxRetryDelay;
        this.retryMultiplier = builder.retryMultiplier;
        this.retryOnStatuses = Set.copyOf(builder.retryOnStatuses);
    }

    /**
     * Creates a client with default settings.
     *
     * @param apiKey the Drishti API key
     * @return a new client
     * @throws IllegalArgumentException when the API key is {@code null} or blank
     */
    public static DrishtiClient create(String apiKey) {
        return builder().apiKey(apiKey).build();
    }

    /**
     * Creates a client from the {@value #API_KEY_ENV} environment variable, and the optional
     * {@value #BASE_URL_ENV} variable.
     *
     * @return a new client
     * @throws IllegalArgumentException when {@value #API_KEY_ENV} is unset or blank
     */
    public static DrishtiClient fromEnvironment() {
        Builder builder = builder().apiKey(System.getenv(API_KEY_ENV));
        String baseUrl = System.getenv(BASE_URL_ENV);
        if (baseUrl != null && !baseUrl.isBlank()) {
            builder.baseUrl(baseUrl);
        }
        return builder.build();
    }

    /**
     * Starts building a client with custom settings.
     *
     * <pre>{@code
     * DrishtiClient client = DrishtiClient.builder()
     *         .apiKey(apiKey)
     *         .timeout(Duration.ofSeconds(30))
     *         .maxRetries(4)
     *         .header("X-Team", "research")
     *         .build();
     * }</pre>
     *
     * @return a new builder
     */
    public static Builder builder() {
        return new Builder();
    }

    /**
     * Returns the base URL requests are sent to, without a trailing slash.
     *
     * @return the base URL
     */
    public String baseUrl() {
        return baseUrl;
    }

    // --- Low-level requests ------------------------------------------------------------------

    /**
     * Sends a request to any API route and returns the response.
     *
     * <p>Use this for routes without a typed helper. Path placeholders such as {@code {job_id}} or
     * {@code :job_id} are replaced from {@code pathParams} and percent-encoded. Query values follow
     * {@link Params#query(Map)}.
     *
     * <pre>{@code
     * DrishtiResponse response = client.request("GET", "/v1/batch/jobs/{job_id}",
     *         Map.of(), Map.of("job_id", "job_1"), null, Map.of());
     * }</pre>
     *
     * @param method the HTTP method, for example {@code GET}
     * @param path the route, starting with {@code /}
     * @param query query parameters, or {@code null}
     * @param pathParams values for path placeholders, or {@code null}
     * @param jsonBody a body to send as JSON ({@link JsonNode}, {@link Map}, record or POJO), or
     *     {@code null} for no body
     * @param headers extra headers for this request, or {@code null}; they cannot override the API
     *     key
     * @return the response
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public DrishtiResponse request(
            String method,
            String path,
            Map<String, ?> query,
            Map<String, ?> pathParams,
            Object jsonBody,
            Map<String, String> headers) {
        Map<String, String> requestHeaders = new LinkedHashMap<>();
        byte[] body = null;
        if (jsonBody != null) {
            body = Json.write(jsonBody).getBytes(StandardCharsets.UTF_8);
            requestHeaders.put("Content-Type", "application/json");
        }
        if (headers != null) {
            requestHeaders.putAll(headers);
        }
        return execute(method, path, Params.query(query), pathParams, body, requestHeaders);
    }

    /**
     * Sends a request to any API route.
     *
     * @param method the HTTP method, for example {@code GET}
     * @param path the route, starting with {@code /}
     * @param query query parameters, or {@code null}
     * @param jsonBody a body to send as JSON, or {@code null}
     * @return the response
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #request(String, String, Map, Map, Object, Map)
     */
    public DrishtiResponse request(String method, String path, Map<String, ?> query, Object jsonBody) {
        return request(method, path, query, null, jsonBody, null);
    }

    /**
     * Sends a request to a {@code /v1} route, adding the {@code /v1} prefix when it is missing.
     *
     * @param method the HTTP method, for example {@code GET}
     * @param path the route, with or without the {@code /v1} prefix, for example {@code /news}
     * @param query query parameters, or {@code null}
     * @param jsonBody a body to send as JSON, or {@code null}
     * @return the response
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public DrishtiResponse requestV1(String method, String path, Map<String, ?> query, Object jsonBody) {
        String normalized = path.startsWith("/") ? path : "/" + path;
        if (!normalized.equals("/v1") && !normalized.startsWith("/v1/")) {
            normalized = "/v1" + normalized;
        }
        return request(method, normalized, query, jsonBody);
    }

    /**
     * Sends a {@code GET} request.
     *
     * @param path the route, starting with {@code /}
     * @return the response
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public DrishtiResponse get(String path) {
        return request("GET", path, null, null);
    }

    /**
     * Sends a {@code GET} request with query parameters.
     *
     * @param path the route, starting with {@code /}
     * @param query query parameters, or {@code null}
     * @return the response
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public DrishtiResponse get(String path, Map<String, ?> query) {
        return request("GET", path, query, null);
    }

    /**
     * Sends a {@code POST} request with a JSON body.
     *
     * @param path the route, starting with {@code /}
     * @param jsonBody the body to send as JSON, or {@code null}
     * @return the response
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public DrishtiResponse post(String path, Object jsonBody) {
        return request("POST", path, null, jsonBody);
    }

    /**
     * Sends a {@code POST} request with a JSON body and query parameters.
     *
     * @param path the route, starting with {@code /}
     * @param jsonBody the body to send as JSON, or {@code null}
     * @param query query parameters, or {@code null}
     * @return the response
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public DrishtiResponse post(String path, Object jsonBody, Map<String, ?> query) {
        return request("POST", path, query, jsonBody);
    }

    /**
     * Sends a {@code PUT} request with a JSON body.
     *
     * @param path the route, starting with {@code /}
     * @param jsonBody the body to send as JSON, or {@code null}
     * @return the response
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public DrishtiResponse put(String path, Object jsonBody) {
        return request("PUT", path, null, jsonBody);
    }

    /**
     * Sends a {@code PATCH} request with a JSON body.
     *
     * @param path the route, starting with {@code /}
     * @param jsonBody the body to send as JSON, or {@code null}
     * @return the response
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public DrishtiResponse patch(String path, Object jsonBody) {
        return request("PATCH", path, null, jsonBody);
    }

    /**
     * Sends a {@code DELETE} request.
     *
     * @param path the route, starting with {@code /}
     * @return the response
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public DrishtiResponse delete(String path) {
        return request("DELETE", path, null, null);
    }

    /**
     * Sends a {@code DELETE} request with query parameters.
     *
     * @param path the route, starting with {@code /}
     * @param query query parameters, or {@code null}
     * @return the response
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public DrishtiResponse delete(String path, Map<String, ?> query) {
        return request("DELETE", path, query, null);
    }

    // --- News, block deals, symbols ------------------------------------------------------------

    /**
     * Lists the latest news feed items, without filters.
     *
     * @return the paginated feed: {@code {"data": [...], "has_next": bool}}
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getNews(NewsParams)
     */
    public JsonNode getNews() {
        return getNews(new NewsParams());
    }

    /**
     * Lists news feed items: {@code GET /v1/news}.
     *
     * <pre>{@code
     * JsonNode news = client.getNews(new NewsParams().symbols("TCS").sentiment("negative"));
     * }</pre>
     *
     * @param params the filters
     * @return the paginated feed: {@code {"data": [...], "has_next": bool}}
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/news/list-news-feed-items">API reference</a>
     */
    public JsonNode getNews(NewsParams params) {
        return getJson("/v1/news", params);
    }

    /**
     * Lists the latest block deals, without filters.
     *
     * @return the paginated block deals
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getBlockDeals(BlockDealsParams)
     */
    public JsonNode getBlockDeals() {
        return getBlockDeals(new BlockDealsParams());
    }

    /**
     * Lists NSE and BSE block-trade prints: {@code GET /v1/block-deals}.
     *
     * @param params the filters
     * @return the paginated block deals
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/block-deals/list-block-deals">API reference</a>
     */
    public JsonNode getBlockDeals(BlockDealsParams params) {
        return getJson("/v1/block-deals", params);
    }

    /**
     * Returns company metadata for symbols or scrip codes: {@code GET /v1/symbols/metadata}.
     *
     * @param params the symbols or scrip codes to look up
     * @return the metadata: {@code {"data": [...]}}
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/symbols/get-symbol-metadata">API reference</a>
     */
    public JsonNode getSymbolsMetadata(SymbolsMetadataParams params) {
        return getJson("/v1/symbols/metadata", params);
    }

    /**
     * Downloads the symbol universe as CSV: {@code GET /v1/symbols}.
     *
     * @return the CSV text, with a header row
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getSymbolsJson()
     */
    public String getSymbols() {
        return getText("/v1/symbols", Map.of(), Map.of());
    }

    /**
     * Returns the symbol universe as JSON: {@code GET /v1/symbols?format=json}.
     *
     * @return the symbols: {@code {"data": [...]}}
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getSymbols()
     */
    public JsonNode getSymbolsJson() {
        return asJson(execute("GET", "/v1/symbols", Map.of("format", "json"), null, null, Map.of()));
    }

    // --- Announcements -------------------------------------------------------------------------

    /**
     * Lists the announcement categories accepted by the {@code categories} filters:
     * {@code GET /v1/announcements/categories}.
     *
     * @return the categories: {@code {"data": ["Board Meeting", ...]}}
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public JsonNode getAnnouncementsCategories() {
        return getJson("/v1/announcements/categories", null);
    }

    /**
     * Lists the latest corporate announcements, without filters.
     *
     * @return the paginated announcements
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getAnnouncements(AnnouncementsParams)
     */
    public JsonNode getAnnouncements() {
        return getAnnouncements(new AnnouncementsParams());
    }

    /**
     * Lists corporate announcements: {@code GET /v1/announcements}.
     *
     * @param params the filters
     * @return the paginated announcements
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/announcements/list-announcements">API reference</a>
     */
    public JsonNode getAnnouncements(AnnouncementsParams params) {
        return getJson("/v1/announcements", params);
    }

    /**
     * Returns attachment URLs for announcements: {@code GET /v1/announcements/attachments}.
     *
     * @param ids the announcement ids
     * @return the attachment URLs per announcement
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public JsonNode getAnnouncementsAttachments(String... ids) {
        return getAnnouncementsAttachments(Arrays.asList(ids));
    }

    /**
     * Returns attachment URLs for announcements: {@code GET /v1/announcements/attachments}.
     *
     * @param ids the announcement ids
     * @return the attachment URLs per announcement
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public JsonNode getAnnouncementsAttachments(Collection<String> ids) {
        return asJson(execute("GET", "/v1/announcements/attachments", Params.query(mapOf("ids", ids)), null, null, Map.of()));
    }

    /**
     * Downloads the source PDF of an announcement:
     * {@code GET /v1/announcements/citations/{announcement_id}/pdf}.
     *
     * @param announcementId the announcement id
     * @return the PDF bytes
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public byte[] getAnnouncementCitationPdf(String announcementId) {
        return getBytes("/v1/announcements/citations/{announcement_id}/pdf",
                Map.of("announcement_id", announcementId), Map.of());
    }

    // --- Daily summary -------------------------------------------------------------------------

    /**
     * Generates an AI summary for a portfolio or watchlist: {@code POST /v1/daily-summary/}.
     *
     * <p>The body is sent as given and the API applies its own defaults.
     *
     * <pre>{@code
     * JsonNode summary = client.postDailySummary(Map.of(
     *         "symbols", List.of("TCS", "INFY"),
     *         "mode", "news_context",
     *         "context_window_days", 7));
     * System.out.println(summary.path("summary").asText());
     * }</pre>
     *
     * @param body the request body: a {@link Map}, {@link JsonNode} or serialisable object
     * @return the summary: {@code {"status", "summary", "details", "error"}}
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/daily-summary/generate-daily-portfolio-summary">API reference</a>
     */
    public JsonNode postDailySummary(Object body) {
        return postDailySummary(body, null);
    }

    /**
     * Generates an AI summary with a content retention preference: {@code POST /v1/daily-summary/}.
     *
     * @param body the request body: a {@link Map}, {@link JsonNode} or serialisable object
     * @param contentRetention the retention preference, or {@code null} for the account default
     * @return the summary: {@code {"status", "summary", "details", "error"}}
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public JsonNode postDailySummary(Object body, ContentRetention contentRetention) {
        Object payload = body == null ? Json.object() : body;
        return asJson(request("POST", "/v1/daily-summary/", null, null, payload, retentionHeaders(contentRetention)));
    }

    // --- Earnings ------------------------------------------------------------------------------

    /**
     * Lists the earnings index, without filters.
     *
     * @return the paginated index
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getEarningsIndex(IndexParams)
     */
    public JsonNode getEarningsIndex() {
        return getEarningsIndex(new IndexParams());
    }

    /**
     * Lists a lightweight index of earnings filings: {@code GET /v1/earnings/index}.
     *
     * @param params the filters
     * @return the paginated index
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/earnings/list-earnings-index">API reference</a>
     */
    public JsonNode getEarningsIndex(IndexParams params) {
        return getJson("/v1/earnings/index", params);
    }

    /**
     * Lists the latest earnings filings, without filters.
     *
     * @return the paginated filings
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getEarnings(EarningsParams)
     */
    public JsonNode getEarnings() {
        return getEarnings(new EarningsParams());
    }

    /**
     * Lists earnings filings: {@code GET /v1/earnings}.
     *
     * <p>When an item has {@code earnings_table_extraction} but no {@code earnings_table}, the
     * extraction is copied into {@code earnings_table}, so callers can always read the latter.
     *
     * @param params the filters
     * @return the paginated filings
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/earnings/list-earnings-filings">API reference</a>
     */
    public JsonNode getEarnings(EarningsParams params) {
        JsonNode response = getJson("/v1/earnings", params);
        if (response != null && response.get("data") instanceof ArrayNode items) {
            items.forEach(DrishtiClient::normalizeEarningsItem);
        }
        return response;
    }

    /**
     * Returns one earnings filing: {@code GET /v1/earnings/detail}.
     *
     * <p>Applies the same {@code earnings_table} fallback as {@link #getEarnings(EarningsParams)}.
     *
     * @param params the quarter and company to look up
     * @return the filing
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/earnings/get-one-earnings-filing">API reference</a>
     */
    public JsonNode getEarningsDetail(DetailParams params) {
        JsonNode response = getJson("/v1/earnings/detail", params);
        normalizeEarningsItem(response);
        return response;
    }

    /**
     * Returns attachment URLs for earnings filings: {@code GET /v1/earnings/attachments}.
     *
     * @param ids the earnings filing ids
     * @return the presigned attachment URLs, where available
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public JsonNode getEarningsAttachments(String... ids) {
        return getEarningsAttachments(Arrays.asList(ids));
    }

    /**
     * Returns attachment URLs for earnings filings: {@code GET /v1/earnings/attachments}.
     *
     * @param ids the earnings filing ids
     * @return the presigned attachment URLs, where available
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/earnings/get-earnings-attachment-urls">API reference</a>
     */
    public JsonNode getEarningsAttachments(Collection<String> ids) {
        return asJson(execute("GET", "/v1/earnings/attachments", Params.query(mapOf("ids", ids)), null, null, Map.of()));
    }

    /**
     * Downloads the source PDF of an earnings filing:
     * {@code GET /v1/earnings/citations/{earnings_id}/pdf}.
     *
     * @param earningsId the earnings filing id
     * @return the PDF bytes
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public byte[] getEarningsCitationPdf(String earningsId) {
        return getEarningsCitationPdf(earningsId, null);
    }

    /**
     * Downloads the source PDF of an earnings filing, opened at a page.
     *
     * <p>The API redirects to the stored file, or to the exchange copy. The SDK follows the
     * redirect and returns the file's bytes. One credit is charged once a PDF is resolved.
     *
     * <pre>{@code
     * Files.write(Path.of("TCS-q4_26.pdf"), client.getEarningsCitationPdf(earningsId, null));
     * }</pre>
     *
     * @param earningsId the earnings filing id
     * @param page the page to cite, starting at 1, or {@code null} for the whole document
     * @return the PDF bytes
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/earnings/open-earnings-pdf-citation">API reference</a>
     */
    public byte[] getEarningsCitationPdf(String earningsId, Integer page) {
        return getBytes("/v1/earnings/citations/{earnings_id}/pdf",
                Map.of("earnings_id", earningsId), Params.query(mapOf("page", page)));
    }

    /**
     * Returns an HTML viewer for one page of an earnings filing:
     * {@code GET /v1/earnings/citations/{earnings_id}/page/{page}}.
     *
     * @param earningsId the earnings filing id
     * @param page the page number, starting at 1
     * @return the HTML page
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/earnings/open-earnings-pdf-page-citation">API reference</a>
     */
    public String getEarningsCitationPage(String earningsId, int page) {
        return getText("/v1/earnings/citations/{earnings_id}/page/{page}",
                Map.of("earnings_id", earningsId, "page", page), Map.of());
    }

    /**
     * Lists upcoming earnings dates, without filters.
     *
     * @return the paginated calendar
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getUpcomingEarnings(UpcomingEarningsParams)
     */
    public JsonNode getUpcomingEarnings() {
        return getUpcomingEarnings(new UpcomingEarningsParams());
    }

    /**
     * Lists upcoming earnings dates: {@code GET /v1/earnings/upcoming}.
     *
     * @param params the filters
     * @return the paginated calendar
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/earnings/list-upcoming-earnings">API reference</a>
     */
    public JsonNode getUpcomingEarnings(UpcomingEarningsParams params) {
        return getJson("/v1/earnings/upcoming", params);
    }

    // --- Concalls ------------------------------------------------------------------------------

    /**
     * Lists the latest conference calls, without filters.
     *
     * @return the paginated conference calls
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getConcalls(ConcallsParams)
     */
    public JsonNode getConcalls() {
        return getConcalls(new ConcallsParams());
    }

    /**
     * Lists conference calls: {@code GET /v1/concalls}.
     *
     * @param params the filters
     * @return the paginated conference calls
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/conference-calls/list-conference-calls">API reference</a>
     */
    public JsonNode getConcalls(ConcallsParams params) {
        return getJson("/v1/concalls", params);
    }

    /**
     * Downloads the source PDF of a conference call:
     * {@code GET /v1/concalls/citations/{concall_id}/pdf}.
     *
     * @param concallId the conference call id
     * @return the PDF bytes
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/conference-calls/open-concall-pdf-citation">API reference</a>
     */
    public byte[] getConcallCitationPdf(String concallId) {
        return getBytes("/v1/concalls/citations/{concall_id}/pdf", Map.of("concall_id", concallId), Map.of());
    }

    /**
     * Lists the conference-call index, without filters.
     *
     * @return the paginated index
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getConcallsIndex(IndexParams)
     */
    public JsonNode getConcallsIndex() {
        return getConcallsIndex(new IndexParams());
    }

    /**
     * Lists a lightweight index of conference calls: {@code GET /v1/concalls/index}.
     *
     * @param params the filters
     * @return the paginated index
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/conference-calls/list-conference-call-index">API reference</a>
     */
    public JsonNode getConcallsIndex(IndexParams params) {
        return getJson("/v1/concalls/index", params);
    }

    /**
     * Lists upcoming conference calls, without filters.
     *
     * @return the paginated calendar
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getUpcomingConcalls(UpcomingConcallsParams)
     */
    public JsonNode getUpcomingConcalls() {
        return getUpcomingConcalls(new UpcomingConcallsParams());
    }

    /**
     * Lists upcoming conference calls: {@code GET /v1/concalls/upcoming}.
     *
     * @param params the filters
     * @return the paginated calendar
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/conference-calls/list-upcoming-conference-calls">API reference</a>
     */
    public JsonNode getUpcomingConcalls(UpcomingConcallsParams params) {
        return getJson("/v1/concalls/upcoming", params);
    }

    /**
     * Returns one conference call: {@code GET /v1/concalls/detail}.
     *
     * @param params the quarter and company to look up
     * @return the conference call
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/conference-calls/get-one-conference-call">API reference</a>
     */
    public JsonNode getConcallsDetail(DetailParams params) {
        return getJson("/v1/concalls/detail", params);
    }

    /**
     * Returns transcript and recording URLs for one company and quarter:
     * {@code GET /v1/concalls/transcript}.
     *
     * @param params the quarter and company to look up
     * @return the transcript and recording URLs, where ready
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/conference-calls/get-transcript-and-recording-urls">API reference</a>
     */
    public JsonNode getConcallsTranscript(TranscriptParams params) {
        return getJson("/v1/concalls/transcript", params);
    }

    /**
     * Returns transcript and recording URLs for several companies and quarters in one call:
     * {@code POST /v1/concalls/transcripts}.
     *
     * @param items the companies and quarters to look up
     * @return the transcript and recording URLs per item
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/conference-calls/get-transcript-and-recording-urls-in-bulk">API reference</a>
     */
    public JsonNode postConcallsTranscripts(List<TranscriptLookup> items) {
        return asJson(post("/v1/concalls/transcripts", Map.of("items", List.copyOf(items))));
    }

    // --- Alerts --------------------------------------------------------------------------------

    /**
     * Lists the latest market alerts, without filters.
     *
     * @return the paginated alerts
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getAlerts(AlertsParams)
     */
    public JsonNode getAlerts() {
        return getAlerts(new AlertsParams());
    }

    /**
     * Lists market alerts such as 52-week highs, price moves and volume spikes:
     * {@code GET /v1/alerts}.
     *
     * @param params the filters
     * @return the paginated alerts
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see Types#ALERT_TYPES
     * @see <a href="https://drishti.manasija.in/docs/api-reference/alerts/list-market-alerts">API reference</a>
     */
    public JsonNode getAlerts(AlertsParams params) {
        return getJson("/v1/alerts", params);
    }

    // --- Account -------------------------------------------------------------------------------

    /**
     * Returns the live account profile, including enabled products and websocket add-ons:
     * {@code GET /v1/account}.
     *
     * @return the account profile
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/account/get-live-account-profile">API reference</a>
     */
    public JsonNode getAccount() {
        return getJson("/v1/account", null);
    }

    /**
     * Returns the plan-wide REST and websocket limits: {@code GET /v1/account/limits}.
     *
     * @return the effective limits
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/account/get-effective-plan-wide-rest-limits">API reference</a>
     */
    public JsonNode getAccountLimits() {
        return getJson("/v1/account/limits", null);
    }

    /**
     * Returns a live snapshot of credits and usage: {@code GET /v1/account/usage}.
     *
     * @return the balance, usage and rate-limit snapshot
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/account/get-live-credit-and-usage-snapshot">API reference</a>
     */
    public JsonNode getAccountUsage() {
        return getJson("/v1/account/usage", null);
    }

    /**
     * Lists recent ledger entries with the API's default page size.
     *
     * @return the ledger entries
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getAccountLedger(Integer)
     */
    public JsonNode getAccountLedger() {
        return getAccountLedger(null);
    }

    /**
     * Lists recent credit ledger entries: {@code GET /v1/account/ledger}.
     *
     * @param limit the maximum number of entries, or {@code null} for the API default
     * @return the ledger entries
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/account/list-recent-account-ledger-entries">API reference</a>
     */
    public JsonNode getAccountLedger(Integer limit) {
        return asJson(execute("GET", "/v1/account/ledger", Params.query(mapOf("limit", limit)), null, null, Map.of()));
    }

    // --- Batch jobs ----------------------------------------------------------------------------

    /**
     * Submits a batch job. Same as {@link #postBatchJobsFile(BatchJobRequest)}.
     *
     * @param request the input file and options
     * @return the created job, with its {@code id} and {@code status}
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     */
    public JsonNode postBatchJobs(BatchJobRequest request) {
        return postBatchJobsFile(request);
    }

    /**
     * Submits a batch job with a JSONL input file: {@code POST /v1/batch/jobs}.
     *
     * <p>The file is sent as multipart form data in the {@code file} field, with
     * {@code display_name} and {@code metadata} as optional fields.
     *
     * @param request the input file and options
     * @return the created job, with its {@code id} and {@code status}
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see Params#buildBatchInputJsonl(List)
     * @see <a href="https://drishti.manasija.in/docs/api-reference/batch-summary/create-batch-job">API reference</a>
     */
    public JsonNode postBatchJobsFile(BatchJobRequest request) {
        Objects.requireNonNull(request, "request");
        String boundary = "drishti-" + UUID.randomUUID();
        MultipartWriter multipart = new MultipartWriter(boundary)
                .file("file", request.fileName(), "application/jsonl", request.fileContent());
        if (request.displayName() != null) {
            multipart.field("display_name", request.displayName());
        }
        if (request.metadata() != null) {
            multipart.field("metadata", request.metadata());
        }
        Map<String, String> headers = new LinkedHashMap<>();
        headers.put("Content-Type", "multipart/form-data; boundary=" + boundary);
        headers.putAll(retentionHeaders(request.contentRetention()));
        return asJson(execute("POST", "/v1/batch/jobs", Map.of(), null, multipart.finish(), headers));
    }

    /**
     * Lists recent batch jobs with the API's default page size.
     *
     * @return the jobs, newest first
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getBatchJobs(Integer)
     */
    public JsonNode getBatchJobs() {
        return getBatchJobs(null);
    }

    /**
     * Lists batch jobs submitted by this API key, newest first: {@code GET /v1/batch/jobs}.
     *
     * @param limit the maximum number of jobs, or {@code null} for the API default
     * @return the jobs
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/batch-summary/list-batch-jobs">API reference</a>
     */
    public JsonNode getBatchJobs(Integer limit) {
        return asJson(execute("GET", "/v1/batch/jobs", Params.query(mapOf("limit", limit)), null, null, Map.of()));
    }

    /**
     * Returns one batch job: {@code GET /v1/batch/jobs/{job_id}}.
     *
     * @param jobId the job id
     * @return the job, with {@code status} and {@code request_counts}
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/batch-summary/get-batch-job">API reference</a>
     */
    public JsonNode getBatchJobsJobId(String jobId) {
        return asJson(execute("GET", "/v1/batch/jobs/{job_id}", Map.of(), Map.of("job_id", jobId), null, Map.of()));
    }

    /**
     * Cancels a batch job: {@code DELETE /v1/batch/jobs/{job_id}}.
     *
     * @param jobId the job id
     * @return the cancelled job, or {@code null} when the API answers {@code 204 No Content}
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see <a href="https://drishti.manasija.in/docs/api-reference/batch-summary/cancel-batch-job">API reference</a>
     */
    public JsonNode deleteBatchJobsJobId(String jobId) {
        return asJson(execute("DELETE", "/v1/batch/jobs/{job_id}", Map.of(), Map.of("job_id", jobId), null, Map.of()));
    }

    /**
     * Downloads a batch job's results as raw JSONL: {@code GET /v1/batch/jobs/{job_id}/results}.
     *
     * @param jobId the job id
     * @return the JSONL text, one result per line
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see #getBatchJobsJobIdResultsParsed(String)
     * @see <a href="https://drishti.manasija.in/docs/api-reference/batch-summary/get-batch-results">API reference</a>
     */
    public String getBatchJobsJobIdResults(String jobId) {
        return getText("/v1/batch/jobs/{job_id}/results", Map.of("job_id", jobId), Map.of());
    }

    /**
     * Downloads and parses a batch job's results.
     *
     * @param jobId the job id
     * @return one object per result line, each with {@code custom_id} and {@code status}
     * @throws DrishtiApiException when the API returns an error status
     * @throws DrishtiNetworkException when no response is received after all retries
     * @see Params#parseBatchResultJsonl(String)
     */
    public List<ObjectNode> getBatchJobsJobIdResultsParsed(String jobId) {
        return Params.parseBatchResultJsonl(getBatchJobsJobIdResults(jobId));
    }

    /**
     * Polls a batch job every two seconds until it finishes, for up to five minutes.
     *
     * @param jobId the job id
     * @return the finished job
     * @throws DrishtiException when the job does not finish in time, or the thread is interrupted
     * @throws DrishtiApiException when the API returns an error status
     * @see #waitForBatchJobCompletion(String, Duration, Duration, Collection)
     */
    public JsonNode waitForBatchJobCompletion(String jobId) {
        return waitForBatchJobCompletion(jobId, Duration.ofSeconds(2), Duration.ofMinutes(5));
    }

    /**
     * Polls a batch job until it reaches a terminal status in
     * {@link Types#BATCH_JOB_TERMINAL_STATUSES}.
     *
     * @param jobId the job id
     * @param pollInterval the wait between polls; {@link Duration#ZERO} polls without waiting
     * @param timeout the maximum total time to wait
     * @return the finished job
     * @throws DrishtiException when the job does not finish in time, or the thread is interrupted
     * @throws DrishtiApiException when the API returns an error status
     */
    public JsonNode waitForBatchJobCompletion(String jobId, Duration pollInterval, Duration timeout) {
        return waitForBatchJobCompletion(jobId, pollInterval, timeout, Types.BATCH_JOB_TERMINAL_STATUSES);
    }

    /**
     * Polls a batch job until it reaches one of the given statuses. Status matching ignores case.
     *
     * <pre>{@code
     * JsonNode job = client.waitForBatchJobCompletion(jobId, Duration.ofSeconds(5), Duration.ofMinutes(30),
     *         Types.BATCH_JOB_TERMINAL_STATUSES);
     * if ("succeeded".equalsIgnoreCase(job.path("status").asText())) {
     *     List<ObjectNode> results = client.getBatchJobsJobIdResultsParsed(jobId);
     * }
     * }</pre>
     *
     * @param jobId the job id
     * @param pollInterval the wait between polls; {@link Duration#ZERO} polls without waiting
     * @param timeout the maximum total time to wait
     * @param terminalStatuses the statuses that end polling
     * @return the finished job
     * @throws DrishtiException when the job does not finish in time, or the thread is interrupted
     * @throws DrishtiApiException when the API returns an error status
     */
    public JsonNode waitForBatchJobCompletion(
            String jobId, Duration pollInterval, Duration timeout, Collection<String> terminalStatuses) {
        Set<String> terminal = new LinkedHashSet<>();
        for (String status : terminalStatuses) {
            terminal.add(status.toLowerCase(Locale.ROOT));
        }
        long startedAt = System.nanoTime();
        while (true) {
            JsonNode job = getBatchJobsJobId(jobId);
            String status = job == null ? "" : job.path("status").asText("").toLowerCase(Locale.ROOT);
            if (terminal.contains(status)) {
                return job;
            }
            if (System.nanoTime() - startedAt >= timeout.toNanos()) {
                throw new DrishtiException("Timed out waiting for batch job " + jobId + " to complete");
            }
            sleep(pollInterval);
        }
    }

    /**
     * Submits a batch job, then waits for it with the defaults of
     * {@link #waitForBatchJobCompletion(String)}.
     *
     * @param request the input file and options
     * @return the finished job
     * @throws DrishtiException when the job does not finish in time, or the response has no id
     * @throws DrishtiApiException when the API returns an error status
     */
    public JsonNode submitBatchJobAndWait(BatchJobRequest request) {
        return submitBatchJobAndWait(request, Duration.ofSeconds(2), Duration.ofMinutes(5));
    }

    /**
     * Submits a batch job, then polls it until it reaches a terminal status.
     *
     * <pre>{@code
     * JsonNode job = client.submitBatchJobAndWait(
     *         new BatchJobRequest("watchlist.jsonl", Params.buildBatchInputJsonl(lines)),
     *         Duration.ofSeconds(5), Duration.ofMinutes(30));
     * }</pre>
     *
     * @param request the input file and options
     * @param pollInterval the wait between polls
     * @param timeout the maximum total time to wait
     * @return the finished job
     * @throws DrishtiException when the job does not finish in time, or the response has no id
     * @throws DrishtiApiException when the API returns an error status
     */
    public JsonNode submitBatchJobAndWait(BatchJobRequest request, Duration pollInterval, Duration timeout) {
        JsonNode job = postBatchJobsFile(request);
        JsonNode id = job == null ? null : job.get("id");
        if (id == null || !(id.isTextual() || id.isIntegralNumber())) {
            throw new DrishtiException("Batch job response did not include an id");
        }
        return waitForBatchJobCompletion(id.asText(), pollInterval, timeout);
    }

    // --- WebSocket -----------------------------------------------------------------------------

    /**
     * Creates a websocket session with default settings that uses this client's API key, base URL
     * and headers.
     *
     * <pre>{@code
     * DrishtiWebSocketSession ws = client.websocket()
     *         .onAnnouncements(a -> System.out.println(a.path("symbol").asText()));
     * ws.subscribe("announcements", List.of("RELIANCE", "TCS"));
     * ws.run();
     * }</pre>
     *
     * @return a new, unconnected session
     * @see DrishtiWebSocketSession
     */
    public DrishtiWebSocketSession websocket() {
        return websocketBuilder().build();
    }

    /**
     * Returns a session builder that already has this client's API key, base URL and headers.
     *
     * @return a pre-configured builder; set any other options, then call
     *     {@link DrishtiWebSocketSession.Builder#build()}
     */
    public DrishtiWebSocketSession.Builder websocketBuilder() {
        return DrishtiWebSocketSession.builder().apiKey(apiKey).baseUrl(baseUrl).headers(headers);
    }

    // --- Internals -----------------------------------------------------------------------------

    private JsonNode getJson(String path, QueryParams<?> params) {
        Map<String, String> query = params == null ? Map.of() : params.toQuery();
        return asJson(execute("GET", path, query, null, null, Map.of()));
    }

    private String getText(String path, Map<String, ?> pathParams, Map<String, String> query) {
        DrishtiResponse response = execute("GET", path, query, pathParams, null, Map.of());
        if (response.isEmpty()) {
            return "";
        }
        return response.isJson() ? response.json().toString() : response.text();
    }

    private byte[] getBytes(String path, Map<String, ?> pathParams, Map<String, String> query) {
        DrishtiResponse response = execute("GET", path, query, pathParams, null, Map.of());
        if (response.isEmpty()) {
            return new byte[0];
        }
        return response.isJson() ? response.json().toString().getBytes(StandardCharsets.UTF_8) : response.bytes();
    }

    private static JsonNode asJson(DrishtiResponse response) {
        return response.json();
    }

    private DrishtiResponse execute(
            String method,
            String path,
            Map<String, String> query,
            Map<String, ?> pathParams,
            byte[] body,
            Map<String, String> extraHeaders) {
        URI uri = buildUri(path, pathParams, query);
        Map<String, String> requestHeaders = new LinkedHashMap<>();
        requestHeaders.put("Accept", "application/json");
        requestHeaders.put("User-Agent", USER_AGENT);
        requestHeaders.putAll(headers);
        requestHeaders.putAll(withoutApiKey(extraHeaders));
        requestHeaders.put(API_KEY_HEADER, apiKey);
        TransportRequest request = new TransportRequest(method.toUpperCase(Locale.ROOT), uri, requestHeaders, body, timeout);

        TransportResponse response = sendWithRetries(request);
        for (int hop = 0; isRedirect(response) && hop < MAX_REDIRECTS; hop++) {
            request = redirectRequest(request, response);
            response = sendWithRetries(request);
        }
        return parseResponse(response);
    }

    private TransportResponse sendWithRetries(TransportRequest request) {
        int attempts = maxRetries + 1;
        for (int attempt = 1; ; attempt++) {
            TransportResponse response;
            try {
                response = transport.send(request);
            } catch (IOException e) {
                if (attempt >= attempts || Thread.currentThread().isInterrupted()) {
                    throw new DrishtiNetworkException("Request to " + request.method() + " "
                            + request.uri().getRawPath() + " failed: " + e.getMessage(), e);
                }
                sleep(retryDelay(attempt));
                continue;
            }
            int status = response.statusCode();
            if (status >= 400 && attempt < attempts && retryOnStatuses.contains(status)) {
                sleep(retryDelay(attempt));
                continue;
            }
            return response;
        }
    }

    private static boolean isRedirect(TransportResponse response) {
        int status = response.statusCode();
        return (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                && response.header("Location") != null;
    }

    /**
     * Builds the follow-up request for a redirect. Citation routes redirect to file storage on
     * another host, so the API key and custom headers are only kept for same-origin redirects.
     */
    private TransportRequest redirectRequest(TransportRequest request, TransportResponse response) {
        URI target;
        try {
            target = request.uri().resolve(response.header("Location"));
        } catch (IllegalArgumentException e) {
            throw new DrishtiException("Invalid redirect location: " + response.header("Location"), e);
        }
        String fromScheme = request.uri().getScheme();
        if ("https".equalsIgnoreCase(fromScheme) && !"https".equalsIgnoreCase(target.getScheme())) {
            throw new DrishtiException("Refusing to follow a redirect from HTTPS to " + target.getScheme() + ": " + target);
        }
        Map<String, String> nextHeaders = new LinkedHashMap<>();
        if (sameOrigin(request.uri(), target)) {
            nextHeaders.putAll(request.headers());
        } else {
            nextHeaders.put("Accept", "*/*");
            nextHeaders.put("User-Agent", USER_AGENT);
        }
        int status = response.statusCode();
        boolean switchToGet = status == 303 || ((status == 301 || status == 302) && !request.method().equals("GET"));
        if (switchToGet) {
            nextHeaders.keySet().removeIf(name -> name.equalsIgnoreCase("Content-Type"));
        }
        return new TransportRequest(switchToGet ? "GET" : request.method(), target, nextHeaders,
                switchToGet ? null : request.body(), request.timeout());
    }

    private static boolean sameOrigin(URI a, URI b) {
        return a.getScheme().equalsIgnoreCase(b.getScheme())
                && a.getHost() != null && a.getHost().equalsIgnoreCase(b.getHost())
                && effectivePort(a) == effectivePort(b);
    }

    private static int effectivePort(URI uri) {
        if (uri.getPort() != -1) {
            return uri.getPort();
        }
        return "https".equalsIgnoreCase(uri.getScheme()) ? 443 : 80;
    }

    private URI buildUri(String path, Map<String, ?> pathParams, Map<String, String> query) {
        String resolved = path;
        if (pathParams != null) {
            for (Map.Entry<String, ?> entry : pathParams.entrySet()) {
                String encoded = encodePathSegment(String.valueOf(entry.getValue()));
                resolved = resolved.replace("{" + entry.getKey() + "}", encoded).replace(":" + entry.getKey(), encoded);
            }
        }
        while (resolved.startsWith("/")) {
            resolved = resolved.substring(1);
        }
        StringBuilder url = new StringBuilder(baseUrl).append('/').append(resolved);
        if (query != null && !query.isEmpty()) {
            StringJoiner pairs = new StringJoiner("&");
            for (Map.Entry<String, String> entry : query.entrySet()) {
                pairs.add(encodeQuery(entry.getKey()) + "=" + encodeQuery(entry.getValue()));
            }
            url.append('?').append(pairs);
        }
        try {
            return URI.create(url.toString());
        } catch (IllegalArgumentException e) {
            throw new IllegalArgumentException("Invalid request URL: " + url, e);
        }
    }

    private DrishtiResponse parseResponse(TransportResponse response) {
        int status = response.statusCode();
        byte[] body = response.body();
        String contentType = response.contentType();
        JsonNode json = null;
        if (status != 204 && body.length > 0 && isJsonContentType(contentType)) {
            try {
                json = Json.parse(new String(body, StandardCharsets.UTF_8));
            } catch (JsonProcessingException e) {
                if (status < 400) {
                    throw new DrishtiException("Invalid JSON in Drishti API response (status " + status + ")", e);
                }
            }
        }
        if (status >= 400) {
            Object errorBody = json != null ? json : (body.length == 0 ? null : new String(body, StandardCharsets.UTF_8));
            throw new DrishtiApiException(status, errorBody);
        }
        return new DrishtiResponse(status, contentType, body, json);
    }

    /**
     * Reports whether a {@code Content-Type} is {@code application/json} or a {@code +json} type,
     * ignoring parameters and case. A substring check would also match {@code application/jsonl}
     * (batch results), which is not a single JSON document.
     */
    private static boolean isJsonContentType(String contentType) {
        int semicolon = contentType.indexOf(';');
        String mediaType = (semicolon < 0 ? contentType : contentType.substring(0, semicolon)).trim().toLowerCase(Locale.ROOT);
        return mediaType.equals("application/json") || mediaType.endsWith("+json");
    }

    private Duration retryDelay(int attempt) {
        double millis = initialRetryDelay.toMillis() * Math.pow(retryMultiplier, Math.max(0, attempt - 1));
        return Duration.ofMillis((long) Math.min(maxRetryDelay.toMillis(), millis));
    }

    private static void sleep(Duration duration) {
        if (duration.isZero() || duration.isNegative()) {
            return;
        }
        try {
            TimeUnit.NANOSECONDS.sleep(duration.toNanos());
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new DrishtiException("Interrupted while waiting to retry", e);
        }
    }

    private static Map<String, String> retentionHeaders(ContentRetention contentRetention) {
        return contentRetention == null ? Map.of() : Map.of(ContentRetention.HEADER, contentRetention.headerValue());
    }

    private static void normalizeEarningsItem(JsonNode item) {
        if (item instanceof ObjectNode object && !object.has("earnings_table") && object.has("earnings_table_extraction")) {
            object.set("earnings_table", object.get("earnings_table_extraction"));
        }
    }

    private static Map<String, Object> mapOf(String key, Object value) {
        Map<String, Object> map = new LinkedHashMap<>();
        map.put(key, value);
        return map;
    }

    private static Map<String, String> withoutApiKey(Map<String, String> source) {
        Map<String, String> out = new LinkedHashMap<>();
        if (source != null) {
            source.forEach((name, value) -> {
                if (!API_KEY_HEADER.equalsIgnoreCase(name)) {
                    out.put(name, value);
                }
            });
        }
        return out;
    }

    private static String trimTrailingSlashes(String url) {
        String trimmed = url.strip();
        while (trimmed.endsWith("/")) {
            trimmed = trimmed.substring(0, trimmed.length() - 1);
        }
        return trimmed;
    }

    /** Percent-encodes a path segment like PHP's rawurlencode / JS encodeURIComponent. */
    private static String encodePathSegment(String value) {
        return URLEncoder.encode(value, StandardCharsets.UTF_8)
                .replace("+", "%20")
                .replace("*", "%2A")
                .replace("%7E", "~");
    }

    private static String encodeQuery(String value) {
        return URLEncoder.encode(value, StandardCharsets.UTF_8).replace("+", "%20");
    }

    /**
     * Builder for {@link DrishtiClient}.
     *
     * <p>Only the API key is required. Builders are not thread-safe.
     *
     * @since 1.0.0
     */
    public static final class Builder {

        private String apiKey;
        private String baseUrl = DEFAULT_BASE_URL;
        private final Map<String, String> headers = new LinkedHashMap<>();
        private HttpTransport transport;
        private int maxRetries = 2;
        private Duration initialRetryDelay = Duration.ofMillis(300);
        private Duration maxRetryDelay = Duration.ofSeconds(5);
        private double retryMultiplier = 2.0;
        private Collection<Integer> retryOnStatuses = DEFAULT_RETRY_ON_STATUSES;
        private Duration timeout = Duration.ofSeconds(60);

        private Builder() {
        }

        /**
         * Sets the API key sent as the {@code X-API-Key} header.
         *
         * @param apiKey the Drishti API key
         * @return this builder
         */
        public Builder apiKey(String apiKey) {
            this.apiKey = apiKey;
            return this;
        }

        /**
         * Sets the API base URL. Trailing slashes are removed; a path prefix (for a proxy) is kept.
         *
         * @param baseUrl the base URL; defaults to {@value DrishtiClient#DEFAULT_BASE_URL}
         * @return this builder
         */
        public Builder baseUrl(String baseUrl) {
            this.baseUrl = Objects.requireNonNull(baseUrl, "baseUrl");
            return this;
        }

        /**
         * Adds a header to every request. It cannot replace the {@code X-API-Key} header.
         *
         * @param name the header name
         * @param value the header value
         * @return this builder
         */
        public Builder header(String name, String value) {
            headers.put(Objects.requireNonNull(name, "name"), Objects.requireNonNull(value, "value"));
            return this;
        }

        /**
         * Adds several headers to every request. They cannot replace the {@code X-API-Key} header.
         *
         * @param headers the headers to add
         * @return this builder
         */
        public Builder headers(Map<String, String> headers) {
            headers.forEach(this::header);
            return this;
        }

        /**
         * Replaces the HTTP transport, for example to use a proxy-aware client or a test double.
         *
         * @param transport the transport; defaults to a {@link JdkHttpTransport}
         * @return this builder
         */
        public Builder transport(HttpTransport transport) {
            this.transport = transport;
            return this;
        }

        /**
         * Sets how many times a failed request is retried.
         *
         * @param maxRetries the retry count; {@code 0} disables retries; default {@code 2}
         * @return this builder
         */
        public Builder maxRetries(int maxRetries) {
            this.maxRetries = maxRetries;
            return this;
        }

        /**
         * Sets the delay before the first retry.
         *
         * @param delay the initial delay; default 300 ms
         * @return this builder
         */
        public Builder initialRetryDelay(Duration delay) {
            this.initialRetryDelay = requireNonNegative(delay, "initialRetryDelay");
            return this;
        }

        /**
         * Sets the longest delay between retries.
         *
         * @param delay the maximum delay; default 5 seconds
         * @return this builder
         */
        public Builder maxRetryDelay(Duration delay) {
            this.maxRetryDelay = requireNonNegative(delay, "maxRetryDelay");
            return this;
        }

        /**
         * Sets the factor the retry delay grows by after each attempt.
         *
         * @param multiplier the backoff multiplier, at least {@code 1.0}; default {@code 2.0}
         * @return this builder
         */
        public Builder retryMultiplier(double multiplier) {
            if (multiplier < 1.0 || Double.isNaN(multiplier)) {
                throw new IllegalArgumentException("retryMultiplier must be at least 1.0");
            }
            this.retryMultiplier = multiplier;
            return this;
        }

        /**
         * Sets the HTTP statuses that are retried.
         *
         * @param statuses the retryable statuses; default {@link DrishtiClient#DEFAULT_RETRY_ON_STATUSES}
         * @return this builder
         */
        public Builder retryOnStatuses(Collection<Integer> statuses) {
            this.retryOnStatuses = List.copyOf(statuses);
            return this;
        }

        /**
         * Sets the connect timeout and the time allowed for each complete response.
         *
         * @param timeout the timeout; default 60 seconds
         * @return this builder
         */
        public Builder timeout(Duration timeout) {
            Objects.requireNonNull(timeout, "timeout");
            if (timeout.isZero() || timeout.isNegative()) {
                throw new IllegalArgumentException("timeout must be positive");
            }
            this.timeout = timeout;
            return this;
        }

        /**
         * Builds the client.
         *
         * @return a new client
         * @throws IllegalArgumentException when the API key is {@code null} or blank
         */
        public DrishtiClient build() {
            return new DrishtiClient(this);
        }

        private static Duration requireNonNegative(Duration value, String name) {
            Objects.requireNonNull(value, name);
            if (value.isNegative()) {
                throw new IllegalArgumentException(name + " must not be negative");
            }
            return value;
        }
    }
}
