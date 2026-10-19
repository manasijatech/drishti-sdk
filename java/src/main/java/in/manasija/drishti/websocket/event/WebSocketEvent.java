package in.manasija.drishti.websocket.event;

import com.fasterxml.jackson.databind.node.ObjectNode;

/**
 * A message received from the Drishti websocket, parsed by
 * {@link in.manasija.drishti.websocket.WebSocketMessages#parse(String)}.
 *
 * <p>The set of event types is closed, so a {@code switch} can cover every case:
 *
 * <pre>{@code
 * switch (event) {
 *     case SubscribedEvent ack -> log.info("subscribed " + ack.product() + " on " + ack.tier());
 *     case DataEvent data -> handle(data.channel(), data.data());
 *     case ErrorEvent error -> log.warn(error.message());
 *     case HeartbeatEvent beat -> { }
 *     case RawEvent raw -> log.debug(raw.payload().toString());
 * }
 * }</pre>
 *
 * (Pattern matching for {@code switch} needs Java 21; on Java 17 use {@code instanceof}.)
 *
 * @since 1.0.0
 */
public sealed interface WebSocketEvent permits SubscribedEvent, DataEvent, ErrorEvent, HeartbeatEvent, RawEvent {

    /** Kind of a {@link SubscribedEvent}. */
    String SUBSCRIBED = "subscribed";

    /** Kind of a {@link DataEvent}. */
    String DATA = "data";

    /** Kind of an {@link ErrorEvent}. */
    String ERROR = "error";

    /** Kind of a {@link HeartbeatEvent}. */
    String HEARTBEAT = "heartbeat";

    /** Kind of a {@link RawEvent}. */
    String RAW = "raw";

    /**
     * Returns the event kind, the same value the other Drishti SDKs use.
     *
     * @return one of {@link #SUBSCRIBED}, {@link #DATA}, {@link #ERROR}, {@link #HEARTBEAT} or
     *     {@link #RAW}
     */
    String kind();

    /**
     * Returns the event as a JSON object with snake_case fields and a {@code kind} field, the same
     * shape the other Drishti SDKs produce.
     *
     * @return a new JSON object
     */
    ObjectNode toJson();
}
