package in.manasija.drishti.websocket;

import java.io.IOException;
import java.util.Map;

/**
 * Opens websocket connections for {@link DrishtiWebSocketSession}.
 *
 * <p>The default is {@link JdkWebSocketConnector}. Supply your own to use a different websocket
 * library, or a fake in tests.
 *
 * @since 1.0.0
 */
@FunctionalInterface
public interface WebSocketConnector {

    /**
     * Opens a connection and completes the handshake.
     *
     * @param url the websocket URL, for example {@code wss://developers.manasija.in/v1/ws}
     * @param headers the handshake headers, including {@code X-API-Key}
     * @return the open connection
     * @throws IOException when the connection or the handshake fails
     * @throws InterruptedException when the connecting thread is interrupted
     */
    WebSocketConnection connect(String url, Map<String, String> headers) throws IOException, InterruptedException;
}
