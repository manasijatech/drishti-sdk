package in.manasija.drishti;

import java.util.List;

/**
 * Shared enumerations of the Drishti API ({@code /v1}).
 *
 * <p>The lists are immutable and keep the API's order. The API may add values over time, so treat
 * them as known values rather than a closed set.
 *
 * @see <a href="https://drishti.manasija.in/docs">Drishti API documentation</a>
 * @since 1.0.0
 */
public final class Types {

    /**
     * Known alert types for the {@code type} filter of {@code GET /v1/alerts}.
     *
     * @see DrishtiClient#getAlerts(in.manasija.drishti.params.AlertsParams)
     */
    public static final List<String> ALERT_TYPES = List.of(
            "52w_high",
            "52w_low",
            "earnings",
            "high_growth_concalls",
            "price_alert",
            "rvol_alert",
            "volume_alert");

    /** Every status a batch job can report, in lifecycle order. */
    public static final List<String> BATCH_JOB_STATUSES =
            List.of("pending", "running", "succeeded", "partial", "failed", "cancelled");

    /**
     * Batch job statuses that end polling in
     * {@link DrishtiClient#waitForBatchJobCompletion(String)}.
     */
    public static final List<String> BATCH_JOB_TERMINAL_STATUSES =
            List.of("succeeded", "partial", "failed", "cancelled");

    /** News sentiment filter value for positive items. */
    public static final String SENTIMENT_POSITIVE = "positive";

    /** News sentiment filter value for negative items. */
    public static final String SENTIMENT_NEGATIVE = "negative";

    /** News sentiment filter value for neutral items. */
    public static final String SENTIMENT_NEUTRAL = "neutral";

    private Types() {
    }
}
