package in.manasija.drishti.websocket.event;

import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.internal.Json;
import java.util.Objects;

/**
 * A keep-alive message from the server.
 *
 * <p>The session uses heartbeats to detect stalled connections. Most applications can ignore them.
 *
 * @param sentAt the server timestamp, for example {@code 2026-01-01T00:00:00Z}; empty when absent
 * @since 1.0.0
 */
public record HeartbeatEvent(String sentAt) implements WebSocketEvent {

    /**
     * Validates the component.
     *
     * @param sentAt the server timestamp, or an empty string
     * @throws NullPointerException when {@code sentAt} is {@code null}
     */
    public HeartbeatEvent {
        Objects.requireNonNull(sentAt, "sentAt");
    }

    /**
     * Returns {@value WebSocketEvent#HEARTBEAT}.
     *
     * @return the event kind
     */
    @Override
    public String kind() {
        return HEARTBEAT;
    }

    /**
     * {@inheritDoc}
     *
     * @return {@code {"kind", "sent_at"}}
     */
    @Override
    public ObjectNode toJson() {
        ObjectNode node = Json.object();
        node.put("kind", HEARTBEAT);
        node.put("sent_at", sentAt);
        return node;
    }
}
