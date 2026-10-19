package in.manasija.drishti.websocket;

import in.manasija.drishti.exception.DrishtiWebSocketException;
import java.time.Duration;

/**
 * One open websocket connection, as seen by {@link DrishtiWebSocketSession}.
 *
 * <p>The default implementation comes from {@link JdkWebSocketConnector}. Tests can inject a
 * scripted fake. A session uses each connection from a single thread.
 *
 * @since 1.0.0
 */
public interface WebSocketConnection {

    /**
     * Sends one text frame.
     *
     * @param text the frame text
     * @throws DrishtiWebSocketException when the connection is closed or the send fails
     */
    void send(String text);

    /**
     * Waits for the next text frame.
     *
     * @param timeout the longest time to wait
     * @return the frame text, or {@code null} when nothing arrived in time
     * @throws DrishtiWebSocketException when the connection is closed or broken
     * @throws InterruptedException when the waiting thread is interrupted
     */
    String receive(Duration timeout) throws InterruptedException;

    /** Closes the connection. Must not throw, and may be called more than once. */
    void close();
}
