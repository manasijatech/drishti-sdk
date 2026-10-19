package in.manasija.drishti;

import com.fasterxml.jackson.databind.JsonNode;
import in.manasija.drishti.exception.DrishtiException;
import java.nio.charset.StandardCharsets;

/**
 * A successful response from the low-level request methods of {@link DrishtiClient}, such as
 * {@link DrishtiClient#get(String)}.
 *
 * <p>The typed endpoint methods return parsed values directly; use this class when calling routes
 * the SDK has no helper for. Parsing follows the same rules in every Drishti SDK:
 *
 * <ul>
 *   <li>a {@code 204} response or an empty body has no value ({@link #isEmpty()});</li>
 *   <li>a JSON content type is parsed into a {@link JsonNode} ({@link #json()});</li>
 *   <li>any other content type (CSV, PDF, JSONL, HTML) is kept as raw bytes ({@link #text()},
 *       {@link #bytes()}).</li>
 * </ul>
 *
 * @since 1.0.0
 */
public final class DrishtiResponse {

    private final int statusCode;
    private final String contentType;
    private final byte[] body;
    private final JsonNode json;

    DrishtiResponse(int statusCode, String contentType, byte[] body, JsonNode json) {
        this.statusCode = statusCode;
        this.contentType = contentType;
        this.body = body;
        this.json = json;
    }

    /**
     * Returns the HTTP status code.
     *
     * @return the status code, in the 2xx or 3xx range
     */
    public int statusCode() {
        return statusCode;
    }

    /**
     * Returns the {@code Content-Type} header.
     *
     * @return the content type, or an empty string when absent
     */
    public String contentType() {
        return contentType;
    }

    /**
     * Reports whether the response had no value: a {@code 204} status or an empty body.
     *
     * @return {@code true} when there is no value
     */
    public boolean isEmpty() {
        return statusCode == 204 || body.length == 0;
    }

    /**
     * Reports whether the response body was parsed as JSON.
     *
     * @return {@code true} for a non-empty JSON response
     */
    public boolean isJson() {
        return json != null;
    }

    /**
     * Returns the parsed JSON body.
     *
     * @return the JSON tree, or {@code null} when the response was empty
     * @throws DrishtiException when the response was not JSON
     */
    public JsonNode json() {
        if (json == null && !isEmpty()) {
            throw new DrishtiException("Expected a JSON response from the Drishti API but got '" + contentType + "'");
        }
        return json;
    }

    /**
     * Returns the body decoded as UTF-8 text.
     *
     * @return the text, or {@code null} when the response was empty
     */
    public String text() {
        return isEmpty() ? null : new String(body, StandardCharsets.UTF_8);
    }

    /**
     * Returns a copy of the raw body bytes, for binary content such as PDFs.
     *
     * @return the body bytes, or {@code null} when the response was empty
     */
    public byte[] bytes() {
        return isEmpty() ? null : body.clone();
    }

    /**
     * Returns the value the way the Python, TypeScript and PHP SDKs do.
     *
     * @return a {@link JsonNode} for JSON, a {@link String} for other content, or {@code null} when
     *     empty
     */
    public Object value() {
        if (isEmpty()) {
            return null;
        }
        return json != null ? json : text();
    }

    /**
     * Returns a short description of the response, for logging.
     *
     * @return the status, content type and body size
     */
    @Override
    public String toString() {
        return "DrishtiResponse{status=" + statusCode + ", contentType='" + contentType + "', bytes=" + body.length + "}";
    }
}
