package in.manasija.drishti.websocket;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.DrishtiClient;
import in.manasija.drishti.exception.DrishtiWebSocketException;
import in.manasija.drishti.internal.Json;
import in.manasija.drishti.support.FakeClock;
import in.manasija.drishti.support.FakeConnection;
import in.manasija.drishti.support.RecordingLogger;
import in.manasija.drishti.support.RecordingTransport;
import in.manasija.drishti.support.ScriptedConnector;
import in.manasija.drishti.websocket.event.SubscribedEvent;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
import java.util.function.Consumer;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.Timeout;

/**
 * Session behaviour mirrored from php/tests/WebSocketSessionTest.php, python/tests/test_websocket.py
 * and js/tests/websocket.test.mjs. Time is faked: it only advances when the session sleeps or waits
 * on the socket.
 */
@Timeout(10)
class DrishtiWebSocketSessionTest {

    private FakeClock clock;
    private RecordingLogger logger;

    @BeforeEach
    void setUp() {
        clock = new FakeClock();
        logger = new RecordingLogger();
    }

    private DrishtiWebSocketSession.Builder session(ScriptedConnector connector) {
        return DrishtiWebSocketSession.builder()
                .apiKey("test-key")
                .baseUrl("https://api.test")
                .connector(connector)
                .logger(logger)
                .reconnectJitterRatio(0.0)
                .clock(clock);
    }

    private static Map<String, Object> message(Object... keyValues) {
        Map<String, Object> map = new java.util.LinkedHashMap<>();
        for (int i = 0; i < keyValues.length; i += 2) {
            map.put((String) keyValues[i], keyValues[i + 1]);
        }
        return map;
    }

    @Test
    void subscribesOnConnectAndLogsTheAck() {
        ScriptedConnector connector = new ScriptedConnector(clock, (n, c) -> c.onSend = ScriptedConnector.acknowledge("pro"));
        List<SubscribedEvent> acks = new ArrayList<>();
        DrishtiWebSocketSession session = session(connector).build();
        session.onSubscribed(acks::add).onOpen(reason -> session.stop());
        session.subscribe("news", List.of(" tcs ", "TCS", ""));

        session.run(Duration.ofSeconds(60));

        assertThat(connector.connections).hasSize(1);
        assertThat(connector.connections.get(0).sent).containsExactly(
                (ObjectNode) Json.toTree(message("op", "subscribe", "product", "news", "symbols", List.of("TCS"), "detailed", true)));
        assertThat(acks).hasSize(1);
        assertThat(acks.get(0).tier()).isEqualTo("pro");
        assertThat(logger.messages()).contains(
                "[drishti-sdk] connected; resubscribing 1 product(s)",
                "[drishti-sdk] subscribed news tier=pro full_feed=false (reconnect, attempt 1)");
        assertThat(connector.connections.get(0).closed).as("stop() closes the socket").isTrue();
    }

    @Test
    void rejectionWhileConnectedRaisesTheServerError() {
        ScriptedConnector connector = new ScriptedConnector(clock,
                (n, c) -> c.onSend = ScriptedConnector.reject("subscription denied", "forbidden"));
        AtomicReference<DrishtiWebSocketException> caught = new AtomicReference<>();
        DrishtiWebSocketSession session = session(connector).subscribeMaxAttempts(1).build();
        session.onOpen(reason -> {
            try {
                session.subscribe("announcements");
            } catch (DrishtiWebSocketException e) {
                caught.set(e);
            }
            session.stop();
        });

        session.run(Duration.ofSeconds(60));

        assertThat(caught.get()).isNotNull();
        assertThat(caught.get().getMessage()).isEqualTo("subscription denied");
        assertThat(caught.get().getErrorCode()).isEqualTo("forbidden");
        assertThat(logger.messages()).contains(
                "[drishti-sdk] subscribe failed product=announcements attempt=1/1 (subscribe): subscription denied [forbidden]");
    }

    @Test
    void permanentRejectionIsNotRetriedInPlace() {
        ScriptedConnector connector = new ScriptedConnector(clock, (n, c) -> c.onSend =
                ScriptedConnector.reject("WebSocket addon 'announcements' is not enabled for this key", "forbidden"));
        AtomicReference<DrishtiWebSocketException> caught = new AtomicReference<>();
        DrishtiWebSocketSession session = session(connector).subscribeMaxAttempts(10).build();
        session.onOpen(reason -> {
            try {
                session.subscribe("announcements");
            } catch (DrishtiWebSocketException e) {
                caught.set(e);
            }
            session.stop();
        });

        session.run(Duration.ofSeconds(60));

        assertThat(caught.get().getErrorCode()).isEqualTo("forbidden");
        assertThat(connector.connections.get(0).subscribedProducts()).containsExactly("announcements");
        assertThat(clock.sleeps).isEmpty();
    }

    @Test
    void transientRejectionIsRetriedWithBackoff() {
        ScriptedConnector connector = new ScriptedConnector(clock, (n, c) -> c.onSend = (connection, msg) -> {
            if (connection.sent.size() < 3) {
                connection.push(message("error", "busy", "code", "rate_limited"));
                return;
            }
            ScriptedConnector.acknowledge().accept(connection, msg);
        });
        DrishtiWebSocketSession session = session(connector)
                .subscribeMaxAttempts(3)
                .subscribeRetryInitialDelay(Duration.ofSeconds(1))
                .subscribeRetryBackoffMultiplier(2.0)
                .build();
        session.onOpen(reason -> {
            session.subscribe("alerts");
            session.stop();
        });

        session.run(Duration.ofSeconds(60));

        assertThat(connector.connections.get(0).subscribedProducts()).containsExactly("alerts", "alerts", "alerts");
        assertThat(clock.sleepSeconds()).containsExactly(1.0, 2.0);
    }

    @Test
    void connectionCloseDuringPendingSubscribeFailsWithoutRetry() {
        ScriptedConnector connector = new ScriptedConnector(clock,
                (n, c) -> c.onSend = (connection, msg) -> connection.closeFromServer());
        AtomicReference<DrishtiWebSocketException> caught = new AtomicReference<>();
        DrishtiWebSocketSession session = session(connector).build();
        session.onOpen(reason -> {
            try {
                session.subscribe("news");
            } catch (DrishtiWebSocketException e) {
                caught.set(e);
            }
            session.stop();
        });

        session.run(Duration.ofSeconds(60));

        assertThat(caught.get()).isNotNull();
        assertThat(caught.get().getMessage()).isEqualTo("WebSocket connection closed");
        assertThat(connector.connections.get(0).subscribedProducts()).containsExactly("news");
    }

    @Test
    void channelHandlersReceiveTypedPayloads() {
        ScriptedConnector connector = new ScriptedConnector(clock, (n, c) -> c.push(
                message("channel", "announcements", "data", message("symbol", "RELIANCE", "title", "Results")),
                message("channel", "alerts", "data", message("symbol", "TCS", "message", "Price move")),
                message("channel", "block-deals", "data", message("symbol", "INFY"))));
        List<JsonNode> announcements = new ArrayList<>();
        List<JsonNode> alerts = new ArrayList<>();
        List<JsonNode> blockDeals = new ArrayList<>();
        List<String> channels = new ArrayList<>();
        List<String> kinds = new ArrayList<>();
        DrishtiWebSocketSession session = session(connector).build()
                .on("announcements", announcements::add)
                .onAlerts(alerts::add)
                .onBlockDeals(blockDeals::add)
                .onData(event -> channels.add(event.channel()))
                .onMessage(event -> kinds.add(event.kind()));

        session.run(Duration.ofSeconds(5));

        assertThat(announcements).containsExactly(Json.toTree(message("symbol", "RELIANCE", "title", "Results")));
        assertThat(alerts).containsExactly(Json.toTree(message("symbol", "TCS", "message", "Price move")));
        assertThat(blockDeals).containsExactly(Json.toTree(message("symbol", "INFY")));
        assertThat(channels).containsExactly("announcements", "alerts", "block_deals");
        assertThat(kinds).containsExactly("data", "data", "data");
    }

    @Test
    void offRemovesAChannelHandler() {
        ScriptedConnector connector = new ScriptedConnector(clock, (n, c) -> c.push(
                message("channel", "announcements", "data", message("symbol", "RELIANCE")),
                message("channel", "announcements", "data", message("symbol", "TCS"))));
        List<String> received = new ArrayList<>();
        DrishtiWebSocketSession session = session(connector).build();
        AtomicReference<Consumer<JsonNode>> handler = new AtomicReference<>();
        handler.set(data -> {
            received.add(data.path("symbol").asText());
            session.off(handler.get());
        });
        session.onAnnouncements(handler.get());

        session.run(Duration.ofSeconds(5));

        assertThat(received).containsExactly("RELIANCE");
    }

    @Test
    void heartbeatsAreDispatchedAndTracked() {
        ScriptedConnector connector = new ScriptedConnector(clock,
                (n, c) -> c.push(message("type", "heartbeat", "sent_at", "2026-01-01T00:00:00Z")));
        List<String> beats = new ArrayList<>();
        DrishtiWebSocketSession session = session(connector).build().onHeartbeat(event -> beats.add(event.sentAt()));
        long startedAt = clock.nanos;
        assertThat(session.lastHeartbeatNanoTime()).isEmpty();

        session.run(Duration.ofSeconds(3));

        assertThat(beats).containsExactly("2026-01-01T00:00:00Z");
        assertThat(session.lastHeartbeatNanoTime()).hasValue(startedAt);
    }

    @Test
    void failedConnectionSetupClosesTheSocket() {
        AtomicReference<DrishtiWebSocketSession> ref = new AtomicReference<>();
        ScriptedConnector connector = new ScriptedConnector(clock, (n, c) -> {
            if (n == 3) {
                ref.get().stop();
            }
        });
        DrishtiWebSocketSession session = session(connector)
                .subscribeMaxAttempts(1)
                .subscribeAckTimeout(Duration.ofSeconds(1))
                .build();
        ref.set(session);
        session.subscribe("news", List.of("INFY"));

        session.run(Duration.ofSeconds(600));

        assertThat(connector.connections).hasSize(3).allMatch(connection -> connection.closed);
        assertThat(logger.messages()).contains("[drishti-sdk] connection setup failed: Subscribe acknowledgement timed out");
    }

    @Test
    void rejectedSubscriptionsKeepReconnectingWithBackoff() {
        AtomicReference<DrishtiWebSocketSession> ref = new AtomicReference<>();
        ScriptedConnector connector = new ScriptedConnector(clock, (n, c) -> c.onSend = (connection, msg) -> {
            ScriptedConnector.reject("not enabled", "forbidden").accept(connection, msg);
            if (n >= 4) {
                ref.get().stop();
            }
        });
        DrishtiWebSocketSession session = session(connector)
                .reconnectInitialDelay(Duration.ofSeconds(1))
                .reconnectBackoffMultiplier(2.0)
                .reconnectMaxDelay(Duration.ofSeconds(30))
                .build();
        ref.set(session);
        session.subscribe("announcements");

        session.run(Duration.ofSeconds(600));

        assertThat(connector.connections).hasSize(4);
        for (FakeConnection connection : connector.connections) {
            assertThat(connection.closed).isTrue();
            assertThat(connection.subscribedProducts()).as("subscription is kept").containsExactly("announcements");
        }
        assertThat(clock.sleepSeconds()).containsExactly(2.0, 4.0, 8.0);
    }

    @Test
    void reconnectsImmediatelyAfterAnEstablishedConnectionDrops() {
        ScriptedConnector connector = new ScriptedConnector(clock, (n, c) -> c.onSend = (connection, msg) -> {
            ScriptedConnector.acknowledge().accept(connection, msg);
            if (n == 1) {
                connection.closeFromServer();
            }
        });
        List<String> closeReasons = new ArrayList<>();
        AtomicInteger opens = new AtomicInteger();
        DrishtiWebSocketSession session = session(connector).build();
        session.onOpen(reason -> {
            if (opens.incrementAndGet() == 2) {
                session.stop();
            }
        }).onClose(closeReasons::add);
        session.subscribe("news");

        session.run(Duration.ofSeconds(60));

        assertThat(connector.connections).hasSize(2);
        assertThat(connector.connections.get(1).subscribedProducts()).as("resubscribed after reconnect").containsExactly("news");
        assertThat(clock.sleeps).as("first reconnect is immediate").isEmpty();
        assertThat(closeReasons.get(0)).startsWith("closed");
    }

    @Test
    void heartbeatTimeoutForcesReconnect() {
        ScriptedConnector connector = new ScriptedConnector(clock, (n, c) -> c.onSend = ScriptedConnector.acknowledge());
        List<String> closeReasons = new ArrayList<>();
        AtomicInteger opens = new AtomicInteger();
        DrishtiWebSocketSession session = session(connector).heartbeatTimeout(Duration.ofSeconds(5)).build();
        session.onOpen(reason -> {
            if (opens.incrementAndGet() == 2) {
                session.stop();
            }
        }).onClose(closeReasons::add);
        session.subscribe("news");

        session.run(Duration.ofSeconds(60));

        assertThat(connector.connections).hasSize(2);
        assertThat(connector.connections.get(0).closed).isTrue();
        assertThat(closeReasons).containsExactly("heartbeat timeout");
    }

    @Test
    void warnsAfterRepeatedConnectionFailures() {
        ScriptedConnector connector = new ScriptedConnector(clock, (n, c) -> {
            throw new IllegalStateException("connection refused");
        });
        List<String> warnings = new ArrayList<>();
        DrishtiWebSocketSession session = session(connector)
                .reconnectInitialDelay(Duration.ofSeconds(1))
                .reconnectMaxDelay(Duration.ofSeconds(2))
                .reconnectWarnAfterAttempts(2)
                .build();
        session.onReconnectWarning((attempt, reason) -> {
            warnings.add(attempt + ":" + reason);
            session.stop();
        });

        session.run(Duration.ofSeconds(600));

        assertThat(warnings).containsExactly("2:initial");
        assertThat(clock.sleepSeconds()).containsExactly(2.0);
    }

    @Test
    void clientWebsocketSharesCredentialsAndHeaders() {
        ScriptedConnector connector = new ScriptedConnector(clock, (n, c) -> {
        });
        DrishtiClient client = DrishtiClient.builder()
                .apiKey("test-key")
                .baseUrl("https://api.test/")
                .header("X-Team", "ops")
                .header("X-API-Key", "spoofed")
                .transport(new RecordingTransport(List.of()))
                .build();
        DrishtiWebSocketSession session = client.websocketBuilder().connector(connector).clock(clock).build();
        session.onOpen(reason -> session.stop());

        session.run(Duration.ofSeconds(60));

        assertThat(connector.calls.get(0).url()).isEqualTo("wss://api.test/v1/ws");
        assertThat(connector.calls.get(0).headers()).containsEntry("X-API-Key", "test-key").containsEntry("X-Team", "ops");
    }

    @Test
    void blankApiKeyIsRejected() {
        assertThatThrownBy(() -> DrishtiWebSocketSession.builder().apiKey("  ").build())
                .isInstanceOf(IllegalArgumentException.class);
    }

    // --- Java-specific behaviour ---------------------------------------------------------------

    @Test
    void unknownChannelIsRejected() {
        DrishtiWebSocketSession session = DrishtiWebSocketSession.builder().apiKey("k").build();
        assertThatThrownBy(() -> session.on("data", data -> { })).isInstanceOf(IllegalArgumentException.class)
                .hasMessageContaining("onData");
    }

    @Test
    void subscribingFromAnAckHandlerDoesNotBreakTheOuterSubscribe() {
        ScriptedConnector connector = new ScriptedConnector(clock, (n, c) -> c.onSend = ScriptedConnector.acknowledge());
        List<String> acked = new ArrayList<>();
        DrishtiWebSocketSession session = session(connector).build();
        session.onSubscribed(ack -> {
            acked.add(ack.product());
            if (ack.product().equals("news")) {
                session.subscribe("alerts", List.of("TCS"));
            }
        }).onOpen(reason -> session.stop());
        session.subscribe("news", List.of("TCS"));

        session.run(Duration.ofSeconds(60));

        assertThat(acked).containsExactly("news", "alerts");
        assertThat(connector.connections).hasSize(1);
        assertThat(logger.messages()).noneMatch(m -> m.contains("connection setup failed"));
    }

    @Test
    void handlerExceptionsAreLoggedAndDoNotDropTheConnection() {
        ScriptedConnector connector = new ScriptedConnector(clock, (n, c) -> c.push(
                message("channel", "news", "data", message("id", "n1")),
                message("channel", "news", "data", message("id", "n2"))));
        List<String> ids = new ArrayList<>();
        DrishtiWebSocketSession session = session(connector).build().onNews(data -> {
            ids.add(data.path("id").asText());
            throw new IllegalStateException("boom");
        });

        session.run(Duration.ofSeconds(5));

        assertThat(ids).containsExactly("n1", "n2");
        assertThat(connector.connections).hasSize(1);
        assertThat(logger.entries).anyMatch(e -> e.level() == System.Logger.Level.WARNING
                && e.message().contains("handler for 'news' threw"));
    }

    @Test
    void subscriptionsSnapshotShowsTheWireMessages() {
        DrishtiWebSocketSession session = DrishtiWebSocketSession.builder().apiKey("k").build()
                .subscribe("block_deals", List.of("reliance"))
                .subscribe("announcements", List.of(), List.of("Dividend"), false);

        assertThat(session.subscriptions()).containsOnlyKeys("block_deals", "announcements");
        assertThat(session.subscriptions().get("block_deals").path("product").asText()).isEqualTo("block-deals");
        assertThat(session.subscriptions().get("announcements").path("categories").get(0).asText()).isEqualTo("Dividend");
    }

    @Test
    void startRunsInTheBackgroundAndCloseStopsIt() throws Exception {
        CountDownLatch acked = new CountDownLatch(1);
        WebSocketConnector connector = (url, headers) -> new BlockingConnection(ScriptedConnector.acknowledge());
        DrishtiWebSocketSession session = DrishtiWebSocketSession.builder()
                .apiKey("k").connector(connector).logger(logger).build()
                .onSubscribed(ack -> acked.countDown());
        session.subscribe("news", List.of("TCS"));

        Thread thread = session.start();
        assertThat(acked.await(5, TimeUnit.SECONDS)).isTrue();
        assertThat(thread.isDaemon()).isTrue();
        assertThat(session.isConnected()).isTrue();
        assertThatThrownBy(session::start).isInstanceOf(IllegalStateException.class);

        session.close();

        assertThat(thread.isAlive()).isFalse();
        assertThat(session.isConnected()).isFalse();
    }

    @Test
    void subscribeFromAnotherThreadIsQueuedAndSentByTheSessionThread() throws Exception {
        CountDownLatch newsAcked = new CountDownLatch(1);
        AtomicReference<BlockingConnection> live = new AtomicReference<>();
        WebSocketConnector connector = (url, headers) -> {
            BlockingConnection connection = new BlockingConnection(ScriptedConnector.acknowledge());
            live.set(connection);
            return connection;
        };
        DrishtiWebSocketSession session = DrishtiWebSocketSession.builder()
                .apiKey("k").connector(connector).logger(logger).build()
                .onSubscribed(ack -> {
                    if (ack.product().equals("news")) {
                        newsAcked.countDown();
                    }
                });
        CountDownLatch opened = new CountDownLatch(1);
        session.onOpen(reason -> opened.countDown());

        try (session) {
            session.start();
            assertThat(opened.await(5, TimeUnit.SECONDS)).isTrue();
            session.subscribe("news", List.of("INFY"));
            assertThat(newsAcked.await(5, TimeUnit.SECONDS)).isTrue();
        }

        assertThat(live.get().sentProducts).containsExactly("news");
    }

    @Test
    void stopBeforeStartIsNotLostWhenCloseFollowsStartImmediately() throws Exception {
        WebSocketConnector connector = (url, headers) -> new BlockingConnection(ScriptedConnector.acknowledge());
        DrishtiWebSocketSession session = DrishtiWebSocketSession.builder().apiKey("k").connector(connector).logger(logger).build();

        Thread thread = session.start();
        session.close();
        thread.join(5_000);

        assertThat(thread.isAlive()).isFalse();
    }

    /** A real-time connection backed by a blocking queue, for threaded tests. */
    private static final class BlockingConnection implements WebSocketConnection {
        private final java.util.concurrent.LinkedBlockingQueue<String> inbox = new java.util.concurrent.LinkedBlockingQueue<>();
        private final java.util.function.BiConsumer<FakeConnection, ObjectNode> onSend;
        final List<String> sentProducts = new java.util.concurrent.CopyOnWriteArrayList<>();
        private volatile boolean closed;

        BlockingConnection(java.util.function.BiConsumer<FakeConnection, ObjectNode> onSend) {
            this.onSend = onSend;
        }

        @Override
        public void send(String text) {
            ObjectNode message;
            try {
                message = (ObjectNode) Json.parse(text);
            } catch (Exception e) {
                throw new AssertionError(e);
            }
            sentProducts.add(message.path("product").asText());
            FakeConnection scratch = new FakeConnection(new FakeClock());
            onSend.accept(scratch, message);
            String reply;
            while ((reply = scratch.receive(Duration.ZERO)) != null) {
                inbox.add(reply);
            }
        }

        @Override
        public String receive(Duration timeout) throws InterruptedException {
            if (closed) {
                throw new DrishtiWebSocketException("closed");
            }
            return inbox.poll(timeout.toMillis(), TimeUnit.MILLISECONDS);
        }

        @Override
        public void close() {
            closed = true;
        }
    }
}
