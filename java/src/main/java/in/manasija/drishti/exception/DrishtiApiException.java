package in.manasija.drishti.exception;

import com.fasterxml.jackson.databind.JsonNode;

/**
 * Thrown when the Drishti API answers with an HTTP status of 400 or above.
 *
 * <p>Retryable statuses (such as {@code 429} or {@code 503}) are retried first; this exception is
 * thrown once retries are exhausted. Common statuses:
 *
 * <ul>
 *   <li>{@code 401}: missing or invalid API key.</li>
 *   <li>{@code 402}: insufficient credits.</li>
 *   <li>{@code 403}: the product, add-on or tier is not enabled for this key.</li>
 *   <li>{@code 429}: rate or quota limit exceeded.</li>
 * </ul>
 *
 * <pre>{@code
 * try {
 *     client.getNews(new NewsParams().symbols("TCS"));
 * } catch (DrishtiApiException e) {
 *     if (e.getStatusCode() == 402) {
 *         // top up credits
 *     }
 *     System.err.println(e.getBody());
 * }
 * }</pre>
 *
 * @see <a href="https://drishti.manasija.in/docs/guides/authentication">Authentication and error codes</a>
 * @since 1.0.0
 */
public final class DrishtiApiException extends DrishtiException {

    private static final long serialVersionUID = 1L;

    /** The HTTP status code of the error response. */
    private final int statusCode;

    /** The parsed error body; transient because a {@link JsonNode} is not guaranteed serialisable. */
    private final transient Object body;

    /**
     * Creates an API exception for an error response.
     *
     * @param statusCode the HTTP status code
     * @param body the parsed body: a {@link JsonNode} for JSON responses, a {@link String} for other
     *     content types, or {@code null} when the body was empty
     */
    public DrishtiApiException(int statusCode, Object body) {
        super("Drishti API error " + statusCode + ": " + render(body));
        this.statusCode = statusCode;
        this.body = body;
    }

    /**
     * Returns the HTTP status code of the error response.
     *
     * @return the HTTP status code, for example {@code 404}
     */
    public int getStatusCode() {
        return statusCode;
    }

    /**
     * Returns the parsed error body.
     *
     * @return a {@link JsonNode} for JSON error bodies, a {@link String} for text bodies, or
     *     {@code null} when the response had no body
     */
    public Object getBody() {
        return body;
    }

    /**
     * Returns the error body as JSON, when the API sent JSON.
     *
     * @return the JSON body, or {@code null} when the body was empty or not JSON
     */
    public JsonNode getBodyJson() {
        return body instanceof JsonNode node ? node : null;
    }

    /**
     * Returns a human-readable message from the error body.
     *
     * <p>Reads {@code error.message} from the API's error envelope
     * ({@code {"error": {"code": "unauthorized", "message": "Invalid API key"}}}), then a top-level
     * {@code detail}, and falls back to the raw body.
     *
     * @return the error message, or {@code null} when the body was empty
     */
    public String getDetail() {
        if (body instanceof JsonNode node) {
            JsonNode message = node.path("error").path("message");
            if (message.isTextual()) {
                return message.asText();
            }
            JsonNode detail = node.get("detail");
            if (detail != null && detail.isTextual()) {
                return detail.asText();
            }
            return node.toString();
        }
        return body == null ? null : body.toString();
    }

    /**
     * Returns the machine-readable error code from the API's error envelope.
     *
     * @return the code, for example {@code unauthorized} or {@code insufficient_credits}, or
     *     {@code null} when the body has none
     */
    public String getErrorCode() {
        if (body instanceof JsonNode node) {
            JsonNode code = node.path("error").path("code");
            if (code.isTextual()) {
                return code.asText();
            }
        }
        return null;
    }

    private static String render(Object body) {
        return body == null ? "null" : body.toString();
    }
}
