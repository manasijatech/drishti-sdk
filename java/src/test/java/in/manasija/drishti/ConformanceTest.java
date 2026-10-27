package in.manasija.drishti;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.assertj.core.api.Assertions.fail;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ArrayNode;
import com.fasterxml.jackson.databind.node.JsonNodeFactory;
import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.exception.DrishtiApiException;
import in.manasija.drishti.http.TransportRequest;
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
import in.manasija.drishti.params.SymbolsMetadataParams;
import in.manasija.drishti.params.TranscriptLookup;
import in.manasija.drishti.params.TranscriptParams;
import in.manasija.drishti.params.UpcomingConcallsParams;
import in.manasija.drishti.params.UpcomingEarningsParams;
import in.manasija.drishti.support.RecordingTransport;
import in.manasija.drishti.websocket.Products;
import in.manasija.drishti.websocket.WebSocketMessages;
import java.io.IOException;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Stream;
import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.TestFactory;

/**
 * Runs the language-neutral fixtures in {@code ../conformance} against the Java SDK.
 *
 * <p>Fixture names are snake_case; {@link #invoke} maps each fixture call onto the Java API, which
 * documents the Java naming conventions in one place: params objects instead of keyword
 * arguments, {@code poll_interval_ms} as a {@link Duration}, {@code content_retention} as
 * {@link ContentRetention}, numeric job ids as strings, and PDF bytes compared as UTF-8 text.
 */
class ConformanceTest {

    private static final String TEST_API_KEY = "test-key";
    private static final String TEST_BASE_URL = "https://api.test";
    private static final JsonNodeFactory NODES = JsonNodeFactory.instance;

    private static Path conformanceDir() {
        String configured = System.getProperty("conformance.dir");
        Path dir = configured != null ? Path.of(configured) : Path.of("..", "conformance");
        assertThat(dir).as("conformance fixtures directory").isDirectory();
        return dir;
    }

    private static JsonNode readJson(String relative) {
        try {
            return Json.parse(Files.readString(conformanceDir().resolve(relative), StandardCharsets.UTF_8));
        } catch (IOException e) {
            throw new AssertionError("Cannot read fixture " + relative, e);
        }
    }

    private static Stream<JsonNode> cases(String relative) {
        List<JsonNode> out = new ArrayList<>();
        readJson(relative).path("cases").forEach(out::add);
        assertThat(out).as("cases in " + relative).isNotEmpty();
        return out.stream();
    }

    // --- REST ----------------------------------------------------------------------------------

    @TestFactory
    Stream<DynamicTest> rest() throws IOException {
        List<Path> files;
        try (Stream<Path> listing = Files.list(conformanceDir().resolve("rest"))) {
            files = listing.filter(p -> p.toString().endsWith(".json")).sorted().toList();
        }
        assertThat(files).isNotEmpty();
        return files.stream()
                .flatMap(file -> cases("rest/" + file.getFileName()))
                .map(testCase -> DynamicTest.dynamicTest(testCase.path("id").asText(), () -> runRestCase(testCase)));
    }

    private static void runRestCase(JsonNode testCase) {
        List<JsonNode> responses = new ArrayList<>();
        if (testCase.has("responses")) {
            testCase.get("responses").forEach(responses::add);
        } else if (testCase.has("response")) {
            responses.add(testCase.get("response"));
        } else {
            ObjectNode ok = NODES.objectNode();
            ok.put("status", 200);
            ok.set("json", NODES.objectNode());
            responses.add(ok);
        }
        RecordingTransport transport = new RecordingTransport(responses);
        JsonNode clientConfig = testCase.path("client");
        JsonNode call = testCase.get("call");

        Object result = null;
        Throwable error = null;
        try {
            DrishtiClient.Builder builder = DrishtiClient.builder()
                    .apiKey(clientConfig.path("api_key").asText(TEST_API_KEY))
                    .baseUrl(clientConfig.path("base_url").asText(TEST_BASE_URL))
                    .transport(transport)
                    .maxRetries(clientConfig.path("max_retries").asInt(2))
                    .initialRetryDelay(Duration.ZERO)
                    .maxRetryDelay(Duration.ZERO);
            clientConfig.path("headers").fields().forEachRemaining(h -> builder.header(h.getKey(), h.getValue().asText()));
            result = invoke(builder.build(), call.get("method").asText(), call.path("args"), call.path("positional"));
        } catch (Throwable caught) {
            if (caught instanceof AssertionError assertion) {
                throw assertion;
            }
            error = caught;
        }

        List<JsonNode> expectedRequests = new ArrayList<>();
        if (testCase.has("expect_requests")) {
            testCase.get("expect_requests").forEach(expectedRequests::add);
        } else if (testCase.has("expect_request")) {
            expectedRequests.add(testCase.get("expect_request"));
        }
        if (testCase.has("expect_requests") || testCase.has("expect_request")) {
            assertThat(transport.requests).as("request count").hasSize(expectedRequests.size());
            for (int i = 0; i < expectedRequests.size(); i++) {
                assertRequest(transport.requests.get(i), expectedRequests.get(i));
            }
        }
        if (testCase.has("expect_request_count")) {
            assertThat(transport.requests).as("request count").hasSize(testCase.get("expect_request_count").asInt());
        }

        if (testCase.has("expect_error")) {
            JsonNode expectedError = testCase.get("expect_error");
            assertThat(error).as("expected " + expectedError.path("type").asText() + ", got a result").isNotNull();
            if ("api_error".equals(expectedError.path("type").asText())) {
                assertThat(error).isInstanceOf(DrishtiApiException.class);
                DrishtiApiException apiError = (DrishtiApiException) error;
                assertThat(apiError.getStatusCode()).isEqualTo(expectedError.get("status").asInt());
                assertThat(toJson(apiError.getBody())).isEqualTo(expectedError.get("body"));
            } else {
                assertThat(error).isInstanceOf(IllegalArgumentException.class);
            }
            return;
        }
        if (error != null) {
            throw new AssertionError("unexpected error: " + error, error);
        }
        if (testCase.has("expect_result")) {
            assertThat(toJson(result)).isEqualTo(testCase.get("expect_result"));
        }
    }

    /** Maps a snake_case fixture call onto the Java API. */
    private static Object invoke(DrishtiClient client, String method, JsonNode args, JsonNode positional) {
        return switch (method) {
            case "get_news" -> client.getNews(new NewsParams()
                    .symbols(list(args, "symbols")).scripCodes(list(args, "scrip_codes")).sectors(list(args, "sectors"))
                    .sentiment(text(args, "sentiment")).from(text(args, "from")).to(text(args, "to"))
                    .page(integer(args, "page")).limit(integer(args, "limit")));
            case "get_block_deals" -> client.getBlockDeals(new BlockDealsParams()
                    .symbols(list(args, "symbols")).exchange(text(args, "exchange"))
                    .from(text(args, "from")).to(text(args, "to"))
                    .page(integer(args, "page")).limit(integer(args, "limit")));
            case "get_symbols_metadata" -> client.getSymbolsMetadata(new SymbolsMetadataParams()
                    .symbols(list(args, "symbols")).scripCodes(list(args, "scrip_codes")));
            case "get_symbols" -> "json".equals(text(args, "format")) ? client.getSymbolsJson() : client.getSymbols();
            case "get_announcements_categories" -> client.getAnnouncementsCategories();
            case "get_announcements" -> client.getAnnouncements(new AnnouncementsParams()
                    .symbols(list(args, "symbols")).scripCodes(list(args, "scrip_codes"))
                    .categories(list(args, "categories")).from(text(args, "from")).to(text(args, "to"))
                    .detailed(bool(args, "detailed")).important(bool(args, "important"))
                    .page(integer(args, "page")).limit(integer(args, "limit")));
            case "get_announcements_attachments" -> client.getAnnouncementsAttachments(list(args, "ids"));
            case "get_announcement_citation_pdf" -> client.getAnnouncementCitationPdf(positional.get(0).asText());
            case "post_daily_summary" -> client.postDailySummary(args.get("body"), retention(args));
            case "get_earnings_index" -> client.getEarningsIndex(indexParams(args));
            case "get_earnings" -> client.getEarnings(new EarningsParams()
                    .ids(list(args, "ids")).symbols(list(args, "symbols")).scripCodes(list(args, "scrip_codes"))
                    .from(text(args, "from")).to(text(args, "to")).quarter(text(args, "quarter"))
                    .detailed(bool(args, "detailed")).page(integer(args, "page")).limit(integer(args, "limit")));
            case "get_earnings_detail" -> client.getEarningsDetail(detailParams(args));
            case "get_earnings_attachments" -> client.getEarningsAttachments(list(args, "ids"));
            case "get_earnings_citation_pdf" -> client.getEarningsCitationPdf(positional.get(0).asText(), integer(args, "page"));
            case "get_earnings_citation_page" -> client.getEarningsCitationPage(positional.get(0).asText(), positional.get(1).asInt());
            case "get_upcoming_earnings" -> client.getUpcomingEarnings(new UpcomingEarningsParams()
                    .symbols(list(args, "symbols")).scripCodes(list(args, "scrip_codes"))
                    .page(integer(args, "page")).limit(integer(args, "limit")));
            case "get_concalls" -> client.getConcalls(new ConcallsParams()
                    .symbols(list(args, "symbols")).scripCodes(list(args, "scrip_codes"))
                    .from(text(args, "from")).to(text(args, "to")).detailed(bool(args, "detailed"))
                    .page(integer(args, "page")).limit(integer(args, "limit")));
            case "get_concall_citation_pdf" -> client.getConcallCitationPdf(positional.get(0).asText());
            case "get_concalls_index" -> client.getConcallsIndex(indexParams(args));
            case "get_upcoming_concalls" -> client.getUpcomingConcalls(new UpcomingConcallsParams()
                    .symbols(list(args, "symbols")).scripCodes(list(args, "scrip_codes")).detailed(bool(args, "detailed"))
                    .page(integer(args, "page")).limit(integer(args, "limit")));
            case "get_concalls_detail" -> client.getConcallsDetail(detailParams(args));
            case "get_concalls_transcript" -> client.getConcallsTranscript(new TranscriptParams(text(args, "quarter"))
                    .symbol(text(args, "symbol")).scripCode(text(args, "scrip_code")));
            case "post_concalls_transcripts" -> {
                List<TranscriptLookup> items = new ArrayList<>();
                args.get("items").forEach(item -> items.add(
                        new TranscriptLookup(item.get("symbol").asText(), item.get("quarter").asText())));
                yield client.postConcallsTranscripts(items);
            }
            case "get_alerts" -> client.getAlerts(new AlertsParams()
                    .symbols(list(args, "symbols")).scripCodes(list(args, "scrip_codes")).types(list(args, "type"))
                    .from(text(args, "from")).to(text(args, "to")).important(bool(args, "important"))
                    .page(integer(args, "page")).limit(integer(args, "limit")));
            case "get_account" -> client.getAccount();
            case "get_account_limits" -> client.getAccountLimits();
            case "get_account_usage" -> client.getAccountUsage();
            case "get_account_ledger" -> client.getAccountLedger(integer(args, "limit"));
            case "post_batch_jobs" -> client.postBatchJobs(batchRequest(args));
            case "post_batch_jobs_file" -> client.postBatchJobsFile(batchRequest(args));
            case "get_batch_jobs" -> client.getBatchJobs(integer(args, "limit"));
            case "get_batch_jobs_job_id" -> client.getBatchJobsJobId(args.get("job_id").asText());
            case "delete_batch_jobs_job_id" -> client.deleteBatchJobsJobId(args.get("job_id").asText());
            case "get_batch_jobs_job_id_results" -> client.getBatchJobsJobIdResults(args.get("job_id").asText());
            case "get_batch_jobs_job_id_results_parsed" -> client.getBatchJobsJobIdResultsParsed(args.get("job_id").asText());
            case "wait_for_batch_job_completion" -> client.waitForBatchJobCompletion(
                    args.get("job_id").asText(), pollInterval(args), Duration.ofSeconds(30));
            case "submit_batch_job_and_wait" -> client.submitBatchJobAndWait(
                    batchRequest(args), pollInterval(args), Duration.ofSeconds(30));
            default -> throw new AssertionError("Conformance fixture calls '" + method + "', which the Java runner does not map");
        };
    }

    private static IndexParams indexParams(JsonNode args) {
        return new IndexParams()
                .symbols(list(args, "symbols")).scripCodes(list(args, "scrip_codes"))
                .from(text(args, "from")).to(text(args, "to")).quarter(text(args, "quarter"))
                .page(integer(args, "page")).limit(integer(args, "limit"));
    }

    private static DetailParams detailParams(JsonNode args) {
        return new DetailParams(text(args, "quarter"))
                .symbol(text(args, "symbol")).scripCode(text(args, "scrip_code")).detailed(bool(args, "detailed"));
    }

    private static BatchJobRequest batchRequest(JsonNode args) {
        return new BatchJobRequest(text(args, "file_name"), text(args, "file_content"))
                .displayName(text(args, "display_name"))
                .metadata(text(args, "metadata"))
                .contentRetention(retention(args));
    }

    private static ContentRetention retention(JsonNode args) {
        String value = text(args, "content_retention");
        if (value == null) {
            return null;
        }
        for (ContentRetention retention : ContentRetention.values()) {
            if (retention.headerValue().equals(value)) {
                return retention;
            }
        }
        throw new AssertionError("Unknown content_retention " + value);
    }

    private static Duration pollInterval(JsonNode args) {
        return Duration.ofMillis(args.path("poll_interval_ms").asLong(0));
    }

    private static String text(JsonNode args, String key) {
        JsonNode node = args.get(key);
        return node == null || node.isNull() ? null : node.asText();
    }

    private static Integer integer(JsonNode args, String key) {
        JsonNode node = args.get(key);
        return node == null || node.isNull() ? null : node.asInt();
    }

    private static Boolean bool(JsonNode args, String key) {
        JsonNode node = args.get(key);
        return node == null || node.isNull() ? null : node.asBoolean();
    }

    private static List<String> list(JsonNode args, String key) {
        JsonNode node = args.get(key);
        if (node == null || node.isNull()) {
            return null;
        }
        List<String> out = new ArrayList<>();
        node.forEach(item -> out.add(item.asText()));
        return out;
    }

    /** Converts an SDK return value to JSON for deep comparison with the fixture. */
    private static JsonNode toJson(Object value) {
        if (value == null) {
            return NODES.nullNode();
        }
        if (value instanceof JsonNode node) {
            return node;
        }
        if (value instanceof String text) {
            return NODES.textNode(text);
        }
        if (value instanceof byte[] bytes) {
            return NODES.textNode(new String(bytes, StandardCharsets.UTF_8));
        }
        if (value instanceof Collection<?> collection) {
            ArrayNode array = NODES.arrayNode();
            collection.forEach(item -> array.add(toJson(item)));
            return array;
        }
        return Json.toTree(value);
    }

    private static void assertRequest(TransportRequest request, JsonNode expected) {
        assertThat(request.method()).as("method").isEqualTo(expected.get("method").asText());
        assertThat(request.uri().getRawPath()).as("path").isEqualTo(expected.get("path").asText());
        assertThat(queryMultimap(request.uri().getRawQuery())).as("query").isEqualTo(expected.path("query"));
        expected.path("headers").fields().forEachRemaining(header ->
                assertThat(request.header(header.getKey())).as("header " + header.getKey()).isEqualTo(header.getValue().asText()));
        if (expected.has("json_body")) {
            assertThat(request.body()).as("json body").isNotNull();
            try {
                assertThat(Json.parse(new String(request.body(), StandardCharsets.UTF_8))).isEqualTo(expected.get("json_body"));
            } catch (IOException e) {
                throw new AssertionError("Request body is not JSON", e);
            }
        }
        if (expected.has("multipart")) {
            assertThat(multipart(request)).isEqualTo(expected.get("multipart"));
        }
    }

    private static ObjectNode queryMultimap(String rawQuery) {
        ObjectNode out = NODES.objectNode();
        if (rawQuery == null || rawQuery.isEmpty()) {
            return out;
        }
        for (String pair : rawQuery.split("&")) {
            String[] parts = pair.split("=", 2);
            String key = URLDecoder.decode(parts[0], StandardCharsets.UTF_8);
            String value = parts.length > 1 ? URLDecoder.decode(parts[1], StandardCharsets.UTF_8) : "";
            JsonNode existing = out.get(key);
            ArrayNode values = existing instanceof ArrayNode array ? array : out.putArray(key);
            values.add(value);
        }
        return out;
    }

    private static ObjectNode multipart(TransportRequest request) {
        String contentType = request.header("Content-Type");
        assertThat(contentType).startsWith("multipart/form-data; boundary=");
        String boundary = contentType.substring(contentType.indexOf("boundary=") + 9).replace("\"", "");
        String body = new String(request.body(), StandardCharsets.UTF_8);
        String[] parts = body.split(Pattern.quote("--" + boundary));
        ObjectNode fields = NODES.objectNode();
        ObjectNode files = NODES.objectNode();
        Pattern namePattern = Pattern.compile("name=\"([^\"]*)\"");
        Pattern filePattern = Pattern.compile("filename=\"([^\"]*)\"");
        for (int i = 1; i < parts.length - 1; i++) {
            String part = parts[i].replaceFirst("^\r\n", "");
            int split = part.indexOf("\r\n\r\n");
            String rawHeaders = part.substring(0, split);
            String content = part.substring(split + 4, part.length() - 2);
            Matcher name = namePattern.matcher(rawHeaders);
            if (!name.find()) {
                fail("multipart part without a name: " + rawHeaders);
            }
            Matcher file = filePattern.matcher(rawHeaders);
            if (file.find()) {
                ObjectNode entry = files.putObject(name.group(1));
                entry.put("filename", file.group(1));
                entry.put("content", content);
            } else {
                fields.put(name.group(1), content);
            }
        }
        ObjectNode out = NODES.objectNode();
        out.set("fields", fields);
        out.set("files", files);
        return out;
    }

    // --- Helpers -------------------------------------------------------------------------------

    @TestFactory
    Stream<DynamicTest> helpers() {
        return cases("helpers/batch_jsonl.json").map(testCase -> DynamicTest.dynamicTest(testCase.path("id").asText(), () -> {
            String function = testCase.get("function").asText();
            if (function.equals("build_batch_input_jsonl")) {
                List<JsonNode> input = new ArrayList<>();
                testCase.get("input").forEach(input::add);
                if (testCase.has("expect_error")) {
                    assertThatThrownBy(() -> Params.buildBatchInputJsonl(input)).isInstanceOf(IllegalArgumentException.class);
                    return;
                }
                String text = Params.buildBatchInputJsonl(input);
                assertThat(text).endsWith("\n");
                ArrayNode lines = NODES.arrayNode();
                for (String line : text.substring(0, text.length() - 1).split("\n")) {
                    lines.add(Json.parse(line));
                }
                assertThat((JsonNode) lines).isEqualTo(testCase.get("expect_lines"));
            } else if (function.equals("parse_batch_result_jsonl")) {
                assertThat(toJson(Params.parseBatchResultJsonl(testCase.get("input").asText())))
                        .isEqualTo(testCase.get("expect_result"));
            } else {
                fail("unknown helper " + function);
            }
        }));
    }

    // --- WebSocket -----------------------------------------------------------------------------

    @TestFactory
    Stream<DynamicTest> websocketParse() {
        return cases("websocket/parse.json").map(testCase -> DynamicTest.dynamicTest(testCase.path("id").asText(), () ->
                assertThat((JsonNode) WebSocketMessages.parse(testCase.get("raw").asText()).toJson())
                        .isEqualTo(testCase.get("expect"))));
    }

    @TestFactory
    Stream<DynamicTest> websocketSubscribe() {
        return cases("websocket/subscribe.json").map(testCase -> DynamicTest.dynamicTest(testCase.path("id").asText(), () -> {
            JsonNode options = testCase.get("options");
            List<String> symbols = list(options, "symbols");
            List<String> categories = list(options, "categories");
            Boolean detailed = bool(options, "detailed");
            ObjectNode message = WebSocketMessages.buildSubscribeMessage(
                    options.get("product").asText(),
                    symbols == null ? List.of() : symbols,
                    categories == null ? List.of() : categories,
                    detailed == null || detailed);
            assertThat((JsonNode) message).isEqualTo(testCase.get("expect"));
        }));
    }

    @TestFactory
    Stream<DynamicTest> websocketUrl() {
        return cases("websocket/url.json").map(testCase -> DynamicTest.dynamicTest(testCase.path("id").asText(), () ->
                assertThat(WebSocketMessages.buildWebSocketUrl(testCase.get("base_url").asText(), text(testCase, "api_key")))
                        .isEqualTo(testCase.get("expect").asText())));
    }

    // --- Constants -----------------------------------------------------------------------------

    @Test
    void constants() {
        JsonNode expected = readJson("constants.json");
        List<String> permanent = new ArrayList<>(Products.PERMANENT_SUBSCRIBE_ERROR_CODES);
        permanent.sort(null);

        assertThat(DrishtiClient.DEFAULT_BASE_URL).isEqualTo(expected.get("default_base_url").asText());
        assertThat(Products.ALL).isEqualTo(list(expected, "ws_products"));
        assertThat(permanent).isEqualTo(list(expected, "permanent_subscribe_error_codes"));
        assertThat(Types.ALERT_TYPES).isEqualTo(list(expected, "alert_types"));
        assertThat(Types.BATCH_JOB_STATUSES).isEqualTo(list(expected, "batch_job_statuses"));
        assertThat(Types.BATCH_JOB_TERMINAL_STATUSES).isEqualTo(list(expected, "batch_job_terminal_statuses"));
    }

    /** Guards against fixture keys this runner does not understand, so new fixture fields fail loudly. */
    @Test
    void everyRestFixtureUsesKnownFields() throws IOException {
        List<String> known = List.of("id", "description", "client", "call", "response", "responses", "expect_request",
                "expect_requests", "expect_request_count", "expect_result", "expect_error");
        try (Stream<Path> listing = Files.list(conformanceDir().resolve("rest"))) {
            for (Path file : listing.toList()) {
                cases("rest/" + file.getFileName()).forEach(testCase -> testCase.fieldNames().forEachRemaining(field ->
                        assertThat(known).as(file.getFileName() + " " + testCase.path("id").asText()).contains(field)));
            }
        }
    }
}
