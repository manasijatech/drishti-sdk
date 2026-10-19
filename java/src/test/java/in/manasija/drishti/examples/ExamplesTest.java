package in.manasija.drishti.examples;

import static org.assertj.core.api.Assertions.assertThat;

import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import in.manasija.drishti.support.MiniWebSocketServer;
import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.TimeUnit;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.Timeout;
import org.junit.jupiter.api.io.TempDir;

/**
 * Runs every example's {@code main} in a separate JVM against a local fake Drishti API. The fake
 * serves response shapes from the public OpenAPI contract, so this checks that each use case
 * works end to end: environment configuration, requests, response field names and exit status.
 */
@Timeout(120)
class ExamplesTest {

    private HttpServer api;
    private final List<String> calls = new CopyOnWriteArrayList<>();

    @TempDir
    Path workDir;

    @BeforeEach
    void startFakeApi() throws IOException {
        api = HttpServer.create(new InetSocketAddress(InetAddress.getLoopbackAddress(), 0), 0);
        route("/v1/announcements", "{\"data\":[{\"id\":\"a1\",\"symbol\":\"RELIANCE\",\"category\":\"Board Meeting\","
                + "\"summary\":\"Board approved Q2 results\",\"date\":\"2026-10-18\"}],\"has_next\":false}");
        route("/v1/news", "{\"data\":[{\"id\":\"n1\",\"symbol\":\"TCS\",\"title\":\"TCS wins large deal\","
                + "\"sentiment\":\"positive\"}],\"has_next\":false}");
        route("/v1/alerts", "{\"data\":[{\"id\":\"al1\",\"symbol\":\"INFY\",\"type\":\"price_alert\","
                + "\"reason\":\"Up 4% intraday\",\"price\":{\"value\":1890.5,\"change_percent\":4.1,\"as_of\":\"2026-10-19T09:30:00Z\"}}],"
                + "\"has_next\":false}");
        route("/v1/earnings/upcoming", "{\"data\":[{\"id\":\"u1\",\"symbol\":\"TCS\",\"date\":\"2026-10-23\","
                + "\"purpose\":\"Financial Results\"}],\"has_next\":false}");
        route("/v1/account/usage", "{\"data\":{\"account_id\":\"acct_9\",\"balance\":9500,\"debited_today\":12}}");
        route("/v1/account", "{\"data\":{\"account_id\":\"acct_9\",\"name\":\"Research Desk\",\"status\":\"active\","
                + "\"products\":[{\"product\":\"news\",\"enabled\":true,\"rpm\":60,\"daily\":5000}],"
                + "\"websocket_addons\":[{\"product\":\"announcements\",\"enabled\":true,\"tier\":\"starter_100\"}]}}");
        route("/v1/earnings/detail", "{\"id\":\"e77\",\"symbol\":\"TCS\",\"quarter\":\"q1_27\","
                + "\"earnings_table_extraction\":{\"revenue\":64259}}");
        route("/v1/concalls/transcript", "{\"transcript_url\":\"https://files.test/t.pdf\",\"audio_url\":null,\"expires_in\":600}");
        api.createContext("/v1/earnings/citations/e77/pdf", exchange -> respond(exchange, 200, "application/pdf",
                "%PDF-1.7 fake".getBytes(StandardCharsets.UTF_8)));
        api.createContext("/v1/batch/jobs", exchange -> {
            calls.add(exchange.getRequestMethod() + " " + exchange.getRequestURI().getPath());
            exchange.getRequestBody().readAllBytes();
            String path = exchange.getRequestURI().getPath();
            if (path.endsWith("/results")) {
                respond(exchange, 200, "application/x-ndjson", ("{\"custom_id\":\"client-001\",\"status\":\"succeeded\"}\n"
                        + "{\"custom_id\":\"client-002\",\"status\":\"succeeded\"}\n").getBytes(StandardCharsets.UTF_8));
            } else if (exchange.getRequestMethod().equals("POST")) {
                respond(exchange, 200, "application/json", "{\"id\":\"job_42\",\"status\":\"pending\"}".getBytes(StandardCharsets.UTF_8));
            } else {
                respond(exchange, 200, "application/json", ("{\"id\":\"job_42\",\"status\":\"succeeded\","
                        + "\"request_counts\":{\"total\":2,\"completed\":2,\"failed\":0}}").getBytes(StandardCharsets.UTF_8));
            }
        });
        api.start();
    }

    @AfterEach
    void stopFakeApi() {
        api.stop(0);
    }

    private void route(String path, String json) {
        api.createContext(path, exchange -> {
            calls.add(exchange.getRequestMethod() + " " + exchange.getRequestURI());
            // HttpServer matches by prefix; only answer the exact route.
            if (!exchange.getRequestURI().getPath().equals(path)) {
                respond(exchange, 404, "application/json", "{\"detail\":\"Not Found\"}".getBytes(StandardCharsets.UTF_8));
                return;
            }
            respond(exchange, 200, "application/json", json.getBytes(StandardCharsets.UTF_8));
        });
    }

    private static void respond(HttpExchange exchange, int status, String contentType, byte[] body) throws IOException {
        exchange.getResponseHeaders().add("Content-Type", contentType);
        exchange.sendResponseHeaders(status, body.length);
        exchange.getResponseBody().write(body);
        exchange.close();
    }

    private Process launch(String baseUrl, Class<?> main, String... args) throws IOException {
        List<String> command = new ArrayList<>(List.of(
                Path.of(System.getProperty("java.home"), "bin", "java").toString(),
                "-cp", System.getProperty("java.class.path"),
                main.getName()));
        command.addAll(List.of(args));
        ProcessBuilder builder = new ProcessBuilder(command).directory(workDir.toFile()).redirectErrorStream(true);
        builder.environment().put("DRISHTI_API_KEY", "example-key");
        builder.environment().put("DRISHTI_BASE_URL", baseUrl);
        return builder.start();
    }

    private String run(Class<?> main, String... args) throws Exception {
        Process process = launch("http://127.0.0.1:" + api.getAddress().getPort(), main, args);
        String output = new String(process.getInputStream().readAllBytes(), StandardCharsets.UTF_8);
        assertThat(process.waitFor(60, TimeUnit.SECONDS)).isTrue();
        assertThat(process.exitValue()).as(output).isZero();
        return output;
    }

    @Test
    void watchlistDigest() throws Exception {
        String output = run(WatchlistDigest.class, "RELIANCE", "TCS", "INFY");

        assertThat(output)
                .contains("RELIANCE   Board Meeting        Board approved Q2 results")
                .contains("TCS        [positive] TCS wins large deal")
                .contains("INFY       price_alert    Up 4% intraday (1890.50, +4.10%)")
                .contains("TCS        2026-10-23   Financial Results");
        assertThat(calls).anyMatch(c -> c.startsWith("GET /v1/announcements?symbols=RELIANCE%2CTCS%2CINFY"));
    }

    @Test
    void accountHealth() throws Exception {
        String output = run(AccountHealth.class);

        assertThat(output)
                .contains("Account Research Desk (acct_9), status active")
                .contains("news             enabled=true  rpm=60 daily=5000")
                .contains("announcements    enabled=true  tier=starter_100")
                .contains("\"balance\" : 9500");
    }

    @Test
    void earningsDeepDive() throws Exception {
        String output = run(EarningsDeepDive.class, "TCS", "q1_27");

        assertThat(output)
                .contains("Filing e77 for TCS q1_27")
                .contains("Results table: {\"revenue\":64259}")
                .contains("Concall transcript: https://files.test/t.pdf (audio: -, link valid 600s)")
                .contains("Saved source PDF (13 bytes)");
        assertThat(Files.readString(workDir.resolve("TCS-q1_27.pdf"))).isEqualTo("%PDF-1.7 fake");
    }

    @Test
    void portfolioBatchSummary() throws Exception {
        String output = run(PortfolioBatchSummary.class);

        assertThat(output)
                .contains("job job_42 finished: succeeded")
                .contains("client-001 -> succeeded")
                .contains("client-002 -> succeeded");
        assertThat(calls).containsSubsequence("POST /v1/batch/jobs", "GET /v1/batch/jobs/job_42", "GET /v1/batch/jobs/job_42/results");
    }

    @Test
    void listenAnnouncementsStreamsUntilStopped() throws Exception {
        try (MiniWebSocketServer ws = new MiniWebSocketServer(null, (peer, text) -> {
            String product = text.replaceAll(".*\"product\":\"([^\"]+)\".*", "$1");
            peer.send("{\"status\":\"subscribed\",\"product\":\"" + product + "\",\"tier\":\"starter_100\","
                    + "\"full_feed\":false,\"symbols\":[\"RELIANCE\",\"TCS\"],\"detailed\":true}");
            if (product.equals("alerts")) {
                peer.send("{\"channel\":\"announcements\",\"data\":{\"symbol\":\"RELIANCE\",\"category\":\"Dividend\","
                        + "\"summary\":\"Interim dividend declared\"}}");
            }
        })) {
            Process process = launch(ws.baseUrl(), ListenAnnouncements.class, "reliance", "tcs");
            List<String> lines = new ArrayList<>();
            try (BufferedReader reader = new BufferedReader(new InputStreamReader(process.getInputStream(), StandardCharsets.UTF_8))) {
                String line;
                while ((line = reader.readLine()) != null) {
                    lines.add(line);
                    if (line.startsWith("[announcement]")) {
                        break;
                    }
                }
            } finally {
                process.destroy();
                process.waitFor(10, TimeUnit.SECONDS);
            }

            assertThat(lines).contains(
                    "connected",
                    "subscribed announcements on tier starter_100 (2 symbols)",
                    "subscribed earnings on tier starter_100 (2 symbols)",
                    "subscribed alerts on tier starter_100 (2 symbols)",
                    "[announcement] RELIANCE Dividend: Interim dividend declared");
            assertThat(ws.handshakes.get(0)).containsEntry("x-api-key", "example-key");
            assertThat(ws.received).hasSize(3);
        }
    }
}
