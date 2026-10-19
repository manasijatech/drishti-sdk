package in.manasija.drishti.websocket.event;

import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.internal.Json;
import java.util.Objects;

/**
 * A JSON object from the server that matches no known message type.
 *
 * <p>Raw events let newer server messages reach the application before the SDK knows about them.
 *
 * @param payload the whole message
 * @since 1.0.0
 */
public record RawEvent(ObjectNode payload) implements WebSocketEvent {

    /**
     * Validates the component.
     *
     * @param payload the whole message
     * @throws NullPointerException when {@code payload} is {@code null}
     */
    public RawEvent {
        Objects.requireNonNull(payload, "payload");
    }

    /**
     * Returns {@value WebSocketEvent#RAW}.
     *
     * @return the event kind
     */
    @Override
    public String kind() {
        return RAW;
    }

    /**
     * {@inheritDoc}
     *
     * @return {@code {"kind", "payload"}}
     */
    @Override
    public ObjectNode toJson() {
        ObjectNode node = Json.object();
        node.put("kind", RAW);
        node.set("payload", payload.deepCopy());
        return node;
    }
}
