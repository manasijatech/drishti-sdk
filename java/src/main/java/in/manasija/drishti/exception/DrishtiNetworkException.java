package in.manasija.drishti.exception;

/**
 * Thrown when an HTTP request gets no response, for example because of a DNS failure, a refused
 * connection or a timeout.
 *
 * <p>Network failures are retried with backoff; this exception is thrown once retries are
 * exhausted. The original {@link java.io.IOException} is available from {@link #getCause()}.
 *
 * @since 1.0.0
 */
public final class DrishtiNetworkException extends DrishtiException {

    private static final long serialVersionUID = 1L;

    /**
     * Creates a network exception.
     *
     * @param message the detail message
     * @param cause the I/O failure that caused it
     */
    public DrishtiNetworkException(String message, Throwable cause) {
        super(message, cause);
    }
}
