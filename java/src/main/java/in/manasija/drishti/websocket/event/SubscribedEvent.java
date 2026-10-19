package in.manasija.drishti.websocket.event;

import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.internal.Json;
import java.util.List;

/**
 * The server's acknowledgement of a subscribe request.
 *
 * <p>It reports what the server accepted: the plan tier, whether the full feed is streamed, and the
 * normalised symbols and categories.
 *
 * @param product the SDK product name, for example {@code block_deals}
 * @param tier the websocket tier, for example {@code starter_100}, {@code pro_1000} or
 *     {@code scale}; empty when the server did not say
 * @param fullFeed whether the whole market is streamed (Scale plan only)
 * @param symbols the accepted symbols; empty for the full feed
 * @param categories the accepted announcement categories
 * @param detailed whether the detail payload shape is delivered
 * @since 1.0.0
 */
public record SubscribedEvent(
        String product, String tier, boolean fullFeed, List<String> symbols, List<String> categories, boolean detailed)
        implements WebSocketEvent {

    /**
     * Copies the lists so the event is immutable.
     *
     * @param product the SDK product name, for example {@code block_deals}
     * @param tier the websocket tier, or an empty string
     * @param fullFeed whether the whole market is streamed
     * @param symbols the accepted symbols
     * @param categories the accepted announcement categories
     * @param detailed whether the detail payload shape is delivered
     */
    public SubscribedEvent {
        symbols = List.copyOf(symbols);
        categories = List.copyOf(categories);
    }

    /**
     * Returns {@value WebSocketEvent#SUBSCRIBED}.
     *
     * @return the event kind
     */
    @Override
    public String kind() {
        return SUBSCRIBED;
    }

    /**
     * {@inheritDoc}
     *
     * @return {@code {"kind", "product", "tier", "full_feed", "symbols", "categories", "detailed"}}
     */
    @Override
    public ObjectNode toJson() {
        ObjectNode node = Json.object();
        node.put("kind", SUBSCRIBED);
        node.put("product", product);
        node.put("tier", tier);
        node.put("full_feed", fullFeed);
        symbols.forEach(node.putArray("symbols")::add);
        categories.forEach(node.putArray("categories")::add);
        node.put("detailed", detailed);
        return node;
    }
}
