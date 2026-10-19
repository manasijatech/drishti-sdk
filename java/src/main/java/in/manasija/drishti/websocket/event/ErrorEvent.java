package in.manasija.drishti.websocket.event;

import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.internal.Json;
import java.util.Objects;

/**
 * An error from the server, such as a rejected subscription, or a frame the SDK could not parse.
 *
 * @param message the error message
 * @param code the server error code, such as {@code forbidden}; {@code null} when the server sent
 *     none, or for parse errors
 * @since 1.0.0
 */
public record ErrorEvent(String message, String code) implements WebSocketEvent {

    /**
     * Validates the components.
     *
     * @param message the error message
     * @param code the server error code, or {@code null}
     * @throws NullPointerException when {@code message} is {@code null}
     */
    public ErrorEvent {
        Objects.requireNonNull(message, "message");
    }

    /**
     * Returns {@value WebSocketEvent#ERROR}.
     *
     * @return the event kind
     */
    @Override
    public String kind() {
        return ERROR;
    }

    /**
     * {@inheritDoc}
     *
     * @return {@code {"kind", "message", "code"}}, where {@code code} may be JSON {@code null}
     */
    @Override
    public ObjectNode toJson() {
        ObjectNode node = Json.object();
        node.put("kind", ERROR);
        node.put("message", message);
        node.put("code", code);
        return node;
    }
}
