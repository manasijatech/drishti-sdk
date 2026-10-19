package in.manasija.drishti.websocket.event;

import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.internal.Json;
import java.util.Objects;

/**
 * A pushed record on a product channel, such as a new announcement or alert.
 *
 * <p>The payload mirrors the REST list or detail schema of the same product. A payload that is not
 * a JSON object is wrapped as {@code {"raw": <payload>}}.
 *
 * @param channel the SDK product name, for example {@code announcements} or {@code block_deals};
 *     channels this SDK version does not know are passed through unchanged
 * @param data the payload object
 * @since 1.0.0
 */
public record DataEvent(String channel, ObjectNode data) implements WebSocketEvent {

    /**
     * Validates the components.
     *
     * @param channel the SDK product name
     * @param data the payload object
     * @throws NullPointerException when either value is {@code null}
     */
    public DataEvent {
        Objects.requireNonNull(channel, "channel");
        Objects.requireNonNull(data, "data");
    }

    /**
     * Returns {@value WebSocketEvent#DATA}.
     *
     * @return the event kind
     */
    @Override
    public String kind() {
        return DATA;
    }

    /**
     * {@inheritDoc}
     *
     * @return {@code {"kind", "channel", "data"}}
     */
    @Override
    public ObjectNode toJson() {
        ObjectNode node = Json.object();
        node.put("kind", DATA);
        node.put("channel", channel);
        node.set("data", data.deepCopy());
        return node;
    }
}
