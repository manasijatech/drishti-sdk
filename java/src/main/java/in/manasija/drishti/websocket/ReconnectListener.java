package in.manasija.drishti.websocket;

import java.time.Duration;

/**
 * Callback for websocket reconnect attempts.
 *
 * <p>Register it with {@link DrishtiWebSocketSession#onReconnectAttempt(ReconnectListener)}. When
 * none is registered, the session logs each attempt instead.
 *
 * @since 1.0.0
 */
@FunctionalInterface
public interface ReconnectListener {

    /**
     * Called before the session waits and then reconnects.
     *
     * @param attempt the number of consecutive failed attempts so far, starting at 1
     * @param delay the backoff delay before the next attempt, before jitter is applied
     * @param reason why the session is reconnecting, for example {@code connection closed}
     */
    void onReconnectAttempt(int attempt, Duration delay, String reason);
}
