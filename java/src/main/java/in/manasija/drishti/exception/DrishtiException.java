package in.manasija.drishti.exception;

/**
 * Base class for every exception thrown by the Drishti SDK.
 *
 * <p>All SDK exceptions are unchecked. Catch this type to handle any SDK failure in one place, or
 * catch a subclass for a specific failure:
 *
 * <ul>
 *   <li>{@link DrishtiApiException}: the API answered with an HTTP error status.</li>
 *   <li>{@link DrishtiNetworkException}: the request never got a response, after all retries.</li>
 *   <li>{@link DrishtiWebSocketException}: a websocket connection or subscription failed.</li>
 * </ul>
 *
 * <p>Invalid arguments, such as a blank API key, raise {@link IllegalArgumentException} instead.
 *
 * @since 1.0.0
 */
public class DrishtiException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates an exception with a message.
     *
     * @param message the detail message
     */
    public DrishtiException(String message) {
        super(message);
    }

    /**
     * Creates an exception with a message and the underlying cause.
     *
     * @param message the detail message
     * @param cause the underlying cause, or {@code null} if unknown
     */
    public DrishtiException(String message, Throwable cause) {
        super(message, cause);
    }
}
