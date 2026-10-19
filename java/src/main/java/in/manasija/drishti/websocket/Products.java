package in.manasija.drishti.websocket;

import java.util.List;

/**
 * Websocket product (channel) names, and the mapping to the names used on the wire.
 *
 * <p>The SDK uses {@code block_deals}, while the server's wire name is {@code block-deals}. The
 * session converts between them in both directions, so applications only see the SDK names.
 *
 * @see <a href="https://drishti.manasija.in/docs/guides/websockets#products">Websocket products</a>
 * @since 1.0.0
 */
public final class Products {

    /** News updates; mirrors {@code GET /v1/news}. */
    public static final String NEWS = "news";

    /** Block-deal prints; mirrors {@code GET /v1/block-deals}. */
    public static final String BLOCK_DEALS = "block_deals";

    /** Corporate announcements; mirrors {@code GET /v1/announcements}. */
    public static final String ANNOUNCEMENTS = "announcements";

    /** Earnings filings; mirrors {@code GET /v1/earnings}. */
    public static final String EARNINGS = "earnings";

    /** Conference-call updates; mirrors {@code GET /v1/concalls}. */
    public static final String CONCALLS = "concalls";

    /** Market alerts; mirrors {@code GET /v1/alerts}. */
    public static final String ALERTS = "alerts";

    /** Every product, in documentation order. */
    public static final List<String> ALL = List.of(NEWS, BLOCK_DEALS, ANNOUNCEMENTS, EARNINGS, CONCALLS, ALERTS);

    /**
     * Subscribe error codes that retrying on the same socket cannot fix: a missing add-on, a tier
     * or symbol limit, or invalid categories.
     *
     * <p>They fail fast instead of being retried in place. The subscription is kept and sent again
     * after the next reconnect, with backoff.
     */
    public static final List<String> PERMANENT_SUBSCRIBE_ERROR_CODES = List.of("forbidden", "bad_request", "unauthorized");

    private static final String BLOCK_DEALS_WIRE = "block-deals";

    private Products() {
    }

    /**
     * Converts a server wire name to the SDK product name ({@code block-deals} to
     * {@code block_deals}).
     *
     * @param product the wire name
     * @return the SDK product name; other names are returned unchanged
     */
    public static String normalize(String product) {
        return BLOCK_DEALS_WIRE.equals(product) ? BLOCK_DEALS : product;
    }

    /**
     * Converts an SDK product name to the server wire name ({@code block_deals} to
     * {@code block-deals}).
     *
     * @param product the SDK product name
     * @return the wire name; other names are returned unchanged
     */
    public static String toWire(String product) {
        return BLOCK_DEALS.equals(product) ? BLOCK_DEALS_WIRE : product;
    }

    /**
     * Reports whether a name is one of the products in {@link #ALL}.
     *
     * @param product the SDK product name
     * @return {@code true} for a known product
     */
    public static boolean isKnown(String product) {
        return ALL.contains(product);
    }
}
