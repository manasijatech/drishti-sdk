package in.manasija.drishti.websocket;

/**
 * Callback for a websocket session that keeps failing to connect.
 *
 * <p>Register it with {@link DrishtiWebSocketSession#onReconnectWarning(ReconnectWarningListener)}.
 * It fires once the backoff delay has reached its maximum and
 * {@link DrishtiWebSocketSession.Builder#reconnectWarnAfterAttempts(int)} attempts in a row have
 * failed. Use it to raise an operational alert. When none is registered, the session logs a
 * warning instead.
 *
 * @since 1.0.0
 */
@FunctionalInterface
public interface ReconnectWarningListener {

    /**
     * Called after repeated connection failures.
     *
     * @param attempt the number of consecutive failed attempts
     * @param reason why the session was reconnecting
     */
    void onReconnectWarning(int attempt, String reason);
}
