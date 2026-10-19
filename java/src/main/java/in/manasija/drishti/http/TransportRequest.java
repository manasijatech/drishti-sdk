package in.manasija.drishti.http;

import java.net.URI;
import java.time.Duration;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Objects;

/**
 * An HTTP request ready to send, built by the client.
 *
 * @param method the HTTP method in upper case, for example {@code GET}
 * @param uri the absolute URI, with the path and query already percent-encoded
 * @param headers the request headers, in insertion order (immutable)
 * @param body the request body, or {@code null} when the request has no body
 * @param timeout the time to wait for the complete response
 * @since 1.0.0
 */
public record TransportRequest(String method, URI uri, Map<String, String> headers, byte[] body, Duration timeout) {

    /**
     * Validates the components and makes the headers immutable.
     *
     * @param method the HTTP method in upper case, for example {@code GET}
     * @param uri the absolute URI, with the path and query already percent-encoded
     * @param headers the request headers, in insertion order
     * @param body the request body, or {@code null} when the request has no body
     * @param timeout the time to wait for the complete response
     */
    public TransportRequest {
        Objects.requireNonNull(method, "method");
        Objects.requireNonNull(uri, "uri");
        Objects.requireNonNull(timeout, "timeout");
        headers = Collections.unmodifiableMap(new LinkedHashMap<>(headers == null ? Map.of() : headers));
    }

    /**
     * Returns a header value, matching the name without regard to case.
     *
     * @param name the header name
     * @return the header value, or {@code null} when the header is absent
     */
    public String header(String name) {
        for (Map.Entry<String, String> entry : headers.entrySet()) {
            if (entry.getKey().equalsIgnoreCase(name)) {
                return entry.getValue();
            }
        }
        return null;
    }
}
