package in.manasija.drishti.http;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;

/**
 * A raw HTTP response returned by an {@link HttpTransport}.
 *
 * @param statusCode the HTTP status code
 * @param headers the response headers, one value per name, with lower-case names (immutable)
 * @param body the response body; an empty array when the response has no body
 * @since 1.0.0
 */
public record TransportResponse(int statusCode, Map<String, String> headers, byte[] body) {

    /**
     * Normalises header names to lower case and absent values to empty ones.
     *
     * @param statusCode the HTTP status code
     * @param headers the response headers, or {@code null}
     * @param body the response body, or {@code null}
     */
    public TransportResponse {
        Map<String, String> normalized = new LinkedHashMap<>();
        if (headers != null) {
            headers.forEach((name, value) -> {
                if (name != null && value != null) {
                    normalized.putIfAbsent(name.toLowerCase(Locale.ROOT), value);
                }
            });
        }
        headers = Collections.unmodifiableMap(normalized);
        body = body == null ? new byte[0] : body;
    }

    /**
     * Creates a response that only carries a content type, for transports and tests that need no
     * other headers.
     *
     * @param statusCode the HTTP status code
     * @param contentType the {@code Content-Type} header value, or {@code null}
     * @param body the response body, or {@code null}
     */
    public TransportResponse(int statusCode, String contentType, byte[] body) {
        this(statusCode, contentType == null ? Map.of() : Map.of("content-type", contentType), body);
    }

    /**
     * Returns a header value, matching the name without regard to case.
     *
     * @param name the header name
     * @return the header value, or {@code null} when the header is absent
     */
    public String header(String name) {
        return headers.get(name.toLowerCase(Locale.ROOT));
    }

    /**
     * Returns the {@code Content-Type} header.
     *
     * @return the content type, or an empty string when absent
     */
    public String contentType() {
        String value = header("Content-Type");
        return value == null ? "" : value;
    }
}
