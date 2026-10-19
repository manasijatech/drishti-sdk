package in.manasija.drishti.websocket;

import in.manasija.drishti.exception.DrishtiWebSocketException;
import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.WebSocket;
import java.net.http.WebSocketHandshakeException;
import java.nio.ByteBuffer;
import java.time.Duration;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;

/**
 * Default {@link WebSocketConnector}, built on the JDK's {@link java.net.http.WebSocket}.
 *
 * <p>Needs no third-party websocket library. Incoming text frames are buffered in a queue until the
 * session reads them, and the JDK answers server pings automatically.
 *
 * @since 1.0.0
 */
public final class JdkWebSocketConnector implements WebSocketConnector {

    private static final Duration SEND_TIMEOUT = Duration.ofSeconds(10);
    private static final Duration CLOSE_TIMEOUT = Duration.ofSeconds(2);

    private final HttpClient httpClient;
    private final Duration connectTimeout;

    /** Creates a connector with its own HTTP client and a 30-second connect timeout. */
    public JdkWebSocketConnector() {
        this(HttpClient.newHttpClient(), Duration.ofSeconds(30));
    }

    /**
     * Creates a connector around an existing HTTP client, for example one with a proxy.
     *
     * @param httpClient the client that opens websocket connections
     * @param connectTimeout the time allowed for the connection and handshake
     */
    public JdkWebSocketConnector(HttpClient httpClient, Duration connectTimeout) {
        this.httpClient = Objects.requireNonNull(httpClient, "httpClient");
        this.connectTimeout = Objects.requireNonNull(connectTimeout, "connectTimeout");
    }

    /**
     * {@inheritDoc}
     *
     * <p>A rejected handshake, such as {@code 401} for an invalid key, is reported as an
     * {@link IOException} that names the HTTP status.
     */
    @Override
    public WebSocketConnection connect(String url, Map<String, String> headers) throws IOException, InterruptedException {
        JdkConnection connection = new JdkConnection();
        WebSocket.Builder builder = httpClient.newWebSocketBuilder().connectTimeout(connectTimeout);
        headers.forEach(builder::header);
        try {
            connection.socket = builder.buildAsync(URI.create(url), connection)
                    .get(connectTimeout.toMillis() + 1_000, TimeUnit.MILLISECONDS);
            return connection;
        } catch (ExecutionException e) {
            Throwable cause = e.getCause() == null ? e : e.getCause();
            if (cause instanceof WebSocketHandshakeException handshake) {
                throw new IOException("WebSocket handshake rejected with HTTP " + handshake.getResponse().statusCode(), cause);
            }
            throw new IOException("WebSocket connection failed: " + cause.getMessage(), cause);
        } catch (TimeoutException e) {
            throw new IOException("WebSocket connection timed out after " + connectTimeout, e);
        }
    }

    /** A connection whose listener feeds received frames into a queue. */
    private static final class JdkConnection implements WebSocketConnection, WebSocket.Listener {

        private static final Frame END = new Frame(null, null);

        private final LinkedBlockingQueue<Frame> inbox = new LinkedBlockingQueue<>();
        private final StringBuilder partial = new StringBuilder();
        private volatile WebSocket socket;
        private volatile String closedReason;

        @Override
        public void onOpen(WebSocket webSocket) {
            webSocket.request(1);
        }

        @Override
        public CompletionStage<?> onText(WebSocket webSocket, CharSequence data, boolean last) {
            partial.append(data);
            if (last) {
                inbox.add(new Frame(partial.toString(), null));
                partial.setLength(0);
            }
            webSocket.request(1);
            return null;
        }

        @Override
        public CompletionStage<?> onBinary(WebSocket webSocket, ByteBuffer data, boolean last) {
            webSocket.request(1);
            return null;
        }

        @Override
        public CompletionStage<?> onClose(WebSocket webSocket, int statusCode, String reason) {
            String detail = reason == null || reason.isEmpty() ? "" : " " + reason;
            inbox.add(new Frame(null, "connection closed by server (code " + statusCode + detail + ")"));
            return null;
        }

        @Override
        public void onError(WebSocket webSocket, Throwable error) {
            inbox.add(new Frame(null, "connection error: " + error));
        }

        @Override
        public void send(String text) {
            WebSocket ws = socket;
            if (ws == null || closedReason != null || ws.isOutputClosed()) {
                throw new DrishtiWebSocketException("WebSocket is not open");
            }
            try {
                ws.sendText(text, true).get(SEND_TIMEOUT.toMillis(), TimeUnit.MILLISECONDS);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new DrishtiWebSocketException("Interrupted while sending", null, e);
            } catch (ExecutionException | TimeoutException e) {
                throw new DrishtiWebSocketException("WebSocket send failed", null, e);
            }
        }

        @Override
        public String receive(Duration timeout) throws InterruptedException {
            if (closedReason != null && inbox.isEmpty()) {
                throw new DrishtiWebSocketException(closedReason);
            }
            Frame frame = inbox.poll(Math.max(0, timeout.toNanos()), TimeUnit.NANOSECONDS);
            if (frame == null) {
                return null;
            }
            if (frame.text() == null) {
                closedReason = frame.closedReason() == null ? "connection closed" : frame.closedReason();
                throw new DrishtiWebSocketException(closedReason);
            }
            return frame.text();
        }

        @Override
        public void close() {
            if (closedReason == null) {
                closedReason = "connection closed";
            }
            inbox.add(END);
            WebSocket ws = socket;
            if (ws == null) {
                return;
            }
            try {
                if (!ws.isOutputClosed()) {
                    ws.sendClose(WebSocket.NORMAL_CLOSURE, "").get(CLOSE_TIMEOUT.toMillis(), TimeUnit.MILLISECONDS);
                }
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
            } catch (ExecutionException | TimeoutException | RuntimeException e) {
                // Best effort: fall through to abort.
            } finally {
                ws.abort();
            }
        }
    }

    /** A received text frame, or a close marker when {@code text} is null. */
    private record Frame(String text, String closedReason) {
    }
}
