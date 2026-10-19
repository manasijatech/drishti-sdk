package in.manasija.drishti.params;

import java.util.Arrays;
import java.util.Collection;

/**
 * Filters for {@code GET /v1/concalls/upcoming}.
 *
 * <p>Setters are fluent and every unset filter is omitted from the request:
 *
 * <pre>{@code
 * new UpcomingConcallsParams().symbols("TCS")
 * }</pre>
 *
 * @see in.manasija.drishti.DrishtiClient#getUpcomingConcalls(UpcomingConcallsParams)
 * @see <a href="https://drishti.manasija.in/docs/api-reference/conference-calls/list-upcoming-conference-calls">API reference</a>
 * @since 1.0.0
 */
public final class UpcomingConcallsParams extends QueryParams<UpcomingConcallsParams> {

    /** Creates an empty filter set; every filter is optional. */
    public UpcomingConcallsParams() {
    }

    /**
     * Filters by NSE trading symbols, for example {@code RELIANCE}.
     *
     * <p>Sent as one comma-separated {@code symbols} value. An empty list sends no filter.
     *
     * @param values the trading symbols
     * @return this object, for chaining
     */
    public UpcomingConcallsParams symbols(String... values) {
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
    public UpcomingConcallsParams symbols(Collection<String> values) {
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
    public UpcomingConcallsParams scripCodes(String... values) {
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
    public UpcomingConcallsParams scripCodes(Collection<String> values) {
        return setList("scrip_codes", values);
    }

    /**
     * Chooses the detail shape ({@code true}) or the summary list shape ({@code false}).
     *
     * @param value {@code true} for the detail shape, or {@code null} to clear it
     * @return this object, for chaining
     */
    public UpcomingConcallsParams detailed(Boolean value) {
        return set("detailed", value);
    }

    /**
     * Selects the results page, starting at 1.
     *
     * @param value the page number, starting at 1, or {@code null} to clear it
     * @return this object, for chaining
     */
    public UpcomingConcallsParams page(Integer value) {
        return set("page", value);
    }

    /**
     * Sets the maximum number of items per page.
     *
     * @param value the page size, or {@code null} to clear it
     * @return this object, for chaining
     */
    public UpcomingConcallsParams limit(Integer value) {
        return set("limit", value);
    }
}
