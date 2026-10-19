package in.manasija.drishti.http;

import java.io.IOException;

/**
 * Sends one HTTP request and returns the raw response.
 *
 * <p>The client builds every request, including the API key header, and handles retries, status
 * checks and parsing. A transport only moves bytes. The default implementation is
 * {@link JdkHttpTransport}. Supply your own, for example to route through a corporate HTTP stack or
 * to record requests in tests:
 *
 * <pre>{@code
 * DrishtiClient client = DrishtiClient.builder()
 *         .apiKey(apiKey)
 *         .transport(request -> myHttpStack.execute(request))
 *         .build();
 * }</pre>
 *
 * <p>Implementations must be thread-safe if the client is shared between threads.
 *
 * @since 1.0.0
 */
@FunctionalInterface
public interface HttpTransport {

    /**
     * Sends a request and waits for the full response.
     *
     * <p>Return every response, including 4xx and 5xx statuses; do not throw for them.
     *
     * @param request the request to send
     * @return the response
     * @throws IOException when no response is received, for example on a connection failure or a
     *     timeout; the client retries these
     */
    TransportResponse send(TransportRequest request) throws IOException;
}
