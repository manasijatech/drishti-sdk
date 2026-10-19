package in.manasija.drishti;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import in.manasija.drishti.exception.DrishtiApiException;
import in.manasija.drishti.exception.DrishtiNetworkException;
import in.manasija.drishti.internal.Json;
import in.manasija.drishti.params.AnnouncementsParams;
import in.manasija.drishti.params.BatchJobRequest;
import in.manasija.drishti.support.MiniWebSocketServer;
import in.manasija.drishti.support.RecordingLogger;
import in.manasija.drishti.websocket.DrishtiWebSocketSession;
import java.io.IOException;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.Timeout;

/**
 * End-to-end tests of the default JDK transports against loopback servers: real sockets, real
 * HTTP and websocket framing, no Drishti credits.
 */
@Timeout(30)
class JdkTransportIntegrationTest {

    private HttpServer server;
    private final List<Map<String, Object>> seen = new CopyOnWriteArrayList<>();

    @BeforeEach
    void startServer() throws IOException {
        server = HttpServer.create(new InetSocketAddress(InetAddress.getLoopbackAddress(), 0), 0);
        server.start();
    }

    @AfterEach
    void stopServer() {
        server.stop(0);
    }

    private DrishtiClient client() {
        return DrishtiClient.builder()
                .apiKey("live-test-key")
                .baseUrl("http://127.0.0.1:" + server.getAddress().getPort() + "/")
                .initialRetryDelay(Duration.ofMillis(10))
                .timeout(Duration.ofSeconds(5))
                .build();
    }

    private static void respond(HttpExchange exchange, int status, String contentType, byte[] body) throws IOException {
        if (contentType != null) {
            exchange.getResponseHeaders().add("Content-Type", contentType);
        }
        exchange.sendResponseHeaders(status, body.length == 0 ? -1 : body.length);
        if (body.length > 0) {
            exchange.getResponseBody().write(body);
        }
        exchange.close();
    }

    private void record(HttpExchange exchange) throws IOException {
        seen.add(Map.of(
                "method", exchange.getRequestMethod(),
                "rawPath", exchange.getRequestURI().getRawPath(),
                "rawQuery", String.valueOf(exchange.getRequestURI().getRawQuery()),
                "apiKey", String.valueOf(exchange.getRequestHeaders().getFirst("X-API-Key")),
                "accept", String.valueOf(exchange.getRequestHeaders().getFirst("Accept")),
                "userAgent", String.valueOf(exchange.getRequestHeaders().getFirst("User-Agent")),
                "contentType", String.valueOf(exchange.getRequestHeaders().getFirst("Content-Type")),
                "retention", String.valueOf(exchange.getRequestHeaders().getFirst("X-Alpha-Content-Retention")),
                "body", new String(exchange.getRequestBody().readAllBytes(), StandardCharsets.UTF_8)));
    }

    @Test
    void getSendsAuthHeadersAndEncodedQuery() {
        server.createContext("/v1/announcements", exchange -> {
            record(exchange);
            respond(exchange, 200, "application/json; charset=utf-8",
                    "{\"data\":[{\"symbol\":\"RELIANCE\",\"summary\":\"Board approves results\"}],\"has_next\":false}"
                            .getBytes(StandardCharsets.UTF_8));
        });

        JsonNode result = client().getAnnouncements(new AnnouncementsParams()
                .symbols("RELIANCE", "TCS").categories("Board Meeting").important(true).limit(5));

        assertThat(result.path("data").get(0).path("summary").asText()).isEqualTo("Board approves results");
        Map<String, Object> request = seen.get(0);
        assertThat(request).containsEntry("apiKey", "live-test-key").containsEntry("accept", "application/json");
        assertThat((String) request.get("userAgent")).startsWith("drishti-sdk-java/");
        assertThat((String) request.get("rawQuery"))
                .contains("symbols=RELIANCE%2CTCS", "categories=Board%20Meeting", "important=true", "limit=5");
    }

    @Test
    void pdfCitationsAreBinarySafe() {
        byte[] pdf = {'%', 'P', 'D', 'F', '-', '1', '.', '4', '\n', (byte) 0xE2, (byte) 0xFF, 0x00, (byte) 0x9C};
        server.createContext("/v1/earnings/citations", exchange -> {
            record(exchange);
            respond(exchange, 200, "application/pdf", pdf);
        });

        byte[] result = client().getEarningsCitationPdf("E 1/2", 3);

        assertThat(result).isEqualTo(pdf);
        assertThat(seen.get(0)).containsEntry("rawPath", "/v1/earnings/citations/E%201%2F2/pdf").containsEntry("rawQuery", "page=3");
    }

    @Test
    void citationRedirectToStorageIsFollowedWithoutLeakingTheApiKey() throws IOException {
        byte[] pdf = {'%', 'P', 'D', 'F', '-', '1', '.', '7', '\n', (byte) 0xC3, 0x00};
        HttpServer storage = HttpServer.create(new InetSocketAddress(InetAddress.getLoopbackAddress(), 0), 0);
        storage.createContext("/bucket/C1.pdf", exchange -> {
            record(exchange);
            respond(exchange, 200, "application/pdf", pdf);
        });
        storage.start();
        try {
            String storageUrl = "http://127.0.0.1:" + storage.getAddress().getPort() + "/bucket/C1.pdf?X-Amz-Signature=abc";
            server.createContext("/v1/concalls/citations/C1/pdf", exchange -> {
                record(exchange);
                exchange.getResponseHeaders().add("Location", storageUrl);
                respond(exchange, 307, "text/html", new byte[0]);
            });
            DrishtiClient client = DrishtiClient.builder()
                    .apiKey("secret-key")
                    .baseUrl("http://127.0.0.1:" + server.getAddress().getPort())
                    .header("X-Team", "ops")
                    .build();

            byte[] result = client.getConcallCitationPdf("C1");

            assertThat(result).isEqualTo(pdf);
            assertThat(seen).hasSize(2);
            assertThat(seen.get(0)).containsEntry("apiKey", "secret-key");
            assertThat(seen.get(1)).as("storage host must not receive the API key")
                    .containsEntry("apiKey", "null").containsEntry("rawPath", "/bucket/C1.pdf")
                    .containsEntry("rawQuery", "X-Amz-Signature=abc");
        } finally {
            storage.stop(0);
        }
    }

    @Test
    void batchUploadIsValidMultipart() {
        server.createContext("/v1/batch/jobs", exchange -> {
            record(exchange);
            respond(exchange, 200, "application/json", "{\"id\":\"job_7\",\"status\":\"pending\"}".getBytes(StandardCharsets.UTF_8));
        });
        String jsonl = Params.buildBatchInputJsonl(List.of(Map.of("custom_id", "a", "symbols", List.of("TCS"))));

        JsonNode job = client().postBatchJobsFile(new BatchJobRequest("watch \"list\".jsonl", jsonl)
                .displayName("Nightly").contentRetention(ContentRetention.NONE));

        assertThat(job.path("id").asText()).isEqualTo("job_7");
        Map<String, Object> request = seen.get(0);
        assertThat((String) request.get("contentType")).startsWith("multipart/form-data; boundary=drishti-");
        assertThat(request).containsEntry("retention", "none");
        String body = (String) request.get("body");
        assertThat(body)
                .contains("Content-Disposition: form-data; name=\"file\"; filename=\"watch %22list%22.jsonl\"\r\n"
                        + "Content-Type: application/jsonl\r\n\r\n" + jsonl + "\r\n")
                .contains("name=\"display_name\"\r\n\r\nNightly\r\n")
                .endsWith("--\r\n");
    }

    @Test
    void retriesThenSucceedsOverRealHttp() {
        AtomicInteger calls = new AtomicInteger();
        server.createContext("/v1/account", exchange -> {
            record(exchange);
            if (calls.incrementAndGet() == 1) {
                respond(exchange, 503, "application/json", "{\"detail\":\"warming up\"}".getBytes(StandardCharsets.UTF_8));
            } else {
                respond(exchange, 200, "application/json", "{\"data\":{\"status\":\"active\"}}".getBytes(StandardCharsets.UTF_8));
            }
        });

        JsonNode account = client().getAccount();

        assertThat(account.path("data").path("status").asText()).isEqualTo("active");
        assertThat(calls.get()).isEqualTo(2);
    }

    @Test
    void errorStatusesBecomeApiExceptions() {
        server.createContext("/v1/news", exchange -> {
            record(exchange);
            respond(exchange, 402, "application/json", "{\"detail\":\"Insufficient credits\"}".getBytes(StandardCharsets.UTF_8));
        });

        assertThatThrownBy(() -> client().getNews())
                .isInstanceOfSatisfying(DrishtiApiException.class, e -> {
                    assertThat(e.getStatusCode()).isEqualTo(402);
                    assertThat(e.getDetail()).isEqualTo("Insufficient credits");
                    assertThat(e.getMessage()).contains("402");
                });
    }

    @Test
    void connectionRefusedBecomesNetworkExceptionAfterRetries() throws IOException {
        int closedPort;
        try (ServerSocket socket = new ServerSocket(0, 1, InetAddress.getLoopbackAddress())) {
            closedPort = socket.getLocalPort();
        }
        DrishtiClient client = DrishtiClient.builder()
                .apiKey("k")
                .baseUrl("http://127.0.0.1:" + closedPort)
                .initialRetryDelay(Duration.ofMillis(1))
                .timeout(Duration.ofSeconds(2))
                .build();

        assertThatThrownBy(client::getAccount)
                .isInstanceOf(DrishtiNetworkException.class)
                .hasCauseInstanceOf(IOException.class)
                .hasMessageContaining("/v1/account");
    }

    @Test
    void websocketSessionStreamsOverTheRealJdkConnector() throws Exception {
        CountDownLatch delivered = new CountDownLatch(1);
        try (MiniWebSocketServer ws = new MiniWebSocketServer(null, (peer, text) -> {
            ObjectNode message = (ObjectNode) Json.parse(text);
            peer.send("{\"type\":\"heartbeat\",\"sent_at\":\"2026-10-19T09:15:00Z\"}");
            peer.send("{\"status\":\"subscribed\",\"product\":\"" + message.path("product").asText()
                    + "\",\"tier\":\"starter_100\",\"full_feed\":false,\"symbols\":" + message.path("symbols") + ",\"detailed\":true}");
            // A payload larger than one small frame, to cover extended length encoding.
            String summary = "x".repeat(70_000);
            peer.send("{\"channel\":\"announcements\",\"data\":{\"symbol\":\"RELIANCE\",\"summary\":\"" + summary + "\"}}");
        })) {
            RecordingLogger logger = new RecordingLogger();
            List<String> symbols = new CopyOnWriteArrayList<>();
            List<String> tiers = new CopyOnWriteArrayList<>();
            DrishtiClient client = DrishtiClient.builder().apiKey("ws-key").baseUrl(ws.baseUrl()).header("X-Team", "ops").build();
            DrishtiWebSocketSession session = client.websocketBuilder().logger(logger).build()
                    .onSubscribed(ack -> tiers.add(ack.tier()))
                    .onAnnouncements(data -> {
                        symbols.add(data.path("symbol").asText() + ":" + data.path("summary").asText().length());
                        delivered.countDown();
                    });
            session.subscribe("announcements", List.of("reliance"));

            try (session) {
                session.start();
                assertThat(delivered.await(10, TimeUnit.SECONDS)).isTrue();
            }

            assertThat(ws.handshakes.get(0)).containsEntry("x-api-key", "ws-key").containsEntry("x-team", "ops");
            assertThat(ws.handshakes.get(0).get(":request-line")).startsWith("GET /v1/ws ");
            assertThat(Json.parse(ws.received.get(0))).isEqualTo(Json.parse(
                    "{\"op\":\"subscribe\",\"product\":\"announcements\",\"symbols\":[\"RELIANCE\"],\"detailed\":true}"));
            assertThat(tiers).containsExactly("starter_100");
            assertThat(symbols).containsExactly("RELIANCE:70000");
            assertThat(session.lastHeartbeatNanoTime()).isPresent();
            assertThat(logger.messages()).contains("[drishti-sdk] subscribed announcements tier=starter_100 full_feed=false (reconnect, attempt 1)");
        }
    }

    @Test
    void rejectedWebsocketHandshakeIsReportedWithItsStatus() throws Exception {
        try (MiniWebSocketServer ws = new MiniWebSocketServer(401, (peer, text) -> { })) {
            RecordingLogger logger = new RecordingLogger();
            DrishtiWebSocketSession session = DrishtiWebSocketSession.builder()
                    .apiKey("bad-key").baseUrl(ws.baseUrl()).logger(logger)
                    .reconnectInitialDelay(Duration.ofMillis(100))
                    .build();

            session.run(Duration.ofMillis(1500));

            assertThat(ws.handshakes).isNotEmpty();
            assertThat(logger.messages()).anyMatch(m -> m.startsWith("[drishti-sdk] connection setup failed:") && m.contains("401"));
        }
    }
}
