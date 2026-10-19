package in.manasija.drishti.params;

import java.util.Arrays;
import java.util.Collection;

/**
 * Filters for {@code GET /v1/earnings/upcoming}.
 *
 * <p>Setters are fluent and every unset filter is omitted from the request:
 *
 * <pre>{@code
 * new UpcomingEarningsParams().symbols("TCS", "INFY")
 * }</pre>
 *
 * @see in.manasija.drishti.DrishtiClient#getUpcomingEarnings(UpcomingEarningsParams)
 * @see <a href="https://drishti.manasija.in/docs/api-reference/earnings/list-upcoming-earnings">API reference</a>
 * @since 1.0.0
 */
public final class UpcomingEarningsParams extends QueryParams<UpcomingEarningsParams> {

    /** Creates an empty filter set; every filter is optional. */
    public UpcomingEarningsParams() {
    }

    /**
     * Filters by NSE trading symbols, for example {@code RELIANCE}.
     *
     * <p>Sent as one comma-separated {@code symbols} value. An empty list sends no filter.
     *
     * @param values the trading symbols
     * @return this object, for chaining
     */
    public UpcomingEarningsParams symbols(String... values) {
        return setList("symbols", values == null ? null : Arrays.asList(values));
    }

    /**
     * Filters by NSE trading symbols, for example {@code RELIANCE}.
     *
     * <p>Sent as one comma-separated {@code symbols} value. An empty list sends no filter.
     *
     * @param values the trading symbols, or {@code null} to clear the filter
     * @return this object, for chaining
     */
    public UpcomingEarningsParams symbols(Collection<String> values) {
        return setList("symbols", values);
    }

    /**
     * Filters by BSE scrip codes, for example {@code 500325}.
     *
     * <p>Sent as one comma-separated {@code scrip_codes} value. An empty list sends no filter.
     *
     * @param values the BSE scrip codes
     * @return this object, for chaining
     */
    public UpcomingEarningsParams scripCodes(String... values) {
        return setList("scrip_codes", values == null ? null : Arrays.asList(values));
    }

    /**
     * Filters by BSE scrip codes, for example {@code 500325}.
     *
     * <p>Sent as one comma-separated {@code scrip_codes} value. An empty list sends no filter.
     *
     * @param values the BSE scrip codes, or {@code null} to clear the filter
     * @return this object, for chaining
     */
    public UpcomingEarningsParams scripCodes(Collection<String> values) {
        return setList("scrip_codes", values);
    }

    /**
     * Selects the results page, starting at 1.
     *
     * @param value the page number, starting at 1, or {@code null} to clear it
     * @return this object, for chaining
     */
    public UpcomingEarningsParams page(Integer value) {
        return set("page", value);
    }

    /**
     * Sets the maximum number of items per page.
     *
     * @param value the page size, or {@code null} to clear it
     * @return this object, for chaining
     */
    public UpcomingEarningsParams limit(Integer value) {
        return set("limit", value);
    }
}
