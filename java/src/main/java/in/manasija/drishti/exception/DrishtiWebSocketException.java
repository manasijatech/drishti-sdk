package in.manasija.drishti.exception;

/**
 * Thrown when a websocket connection fails, closes unexpectedly, or the server rejects a
 * subscription.
 *
 * <p>When the server rejects a subscribe request, {@link #getErrorCode()} holds the server's error
 * code. The codes {@code forbidden}, {@code bad_request} and {@code unauthorized} are permanent: a
 * missing websocket add-on, a symbol cap that was exceeded, or a full-feed request without the
 * Scale plan.
 *
 * @see in.manasija.drishti.websocket.DrishtiWebSocketSession
 * @since 1.0.0
 */
public final class DrishtiWebSocketException extends DrishtiException {

    private static final long serialVersionUID = 1L;

    /** The server error code, or {@code null}. */
    private final String errorCode;

    /**
     * Creates a websocket exception without a server error code.
     *
     * @param message the detail message
     */
    public DrishtiWebSocketException(String message) {
        this(message, null, null);
    }

    /**
     * Creates a websocket exception with a server error code.
     *
     * @param message the detail message
     * @param errorCode the server error code, such as {@code forbidden}, or {@code null}
     */
    public DrishtiWebSocketException(String message, String errorCode) {
        this(message, errorCode, null);
    }

    /**
     * Creates a websocket exception with a server error code and an underlying cause.
     *
     * @param message the detail message
     * @param errorCode the server error code, such as {@code forbidden}, or {@code null}
     * @param cause the underlying cause, or {@code null}
     */
    public DrishtiWebSocketException(String message, String errorCode, Throwable cause) {
        super(message, cause);
        this.errorCode = errorCode;
    }

    /**
     * Returns the server error code, when the server sent one.
     *
     * @return the error code, such as {@code forbidden}, or {@code null}
     */
    public String getErrorCode() {
        return errorCode;
    }
}
