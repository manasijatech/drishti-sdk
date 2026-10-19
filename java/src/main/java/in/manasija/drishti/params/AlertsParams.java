package in.manasija.drishti.params;

import java.time.LocalDate;
import java.util.Arrays;
import java.util.Collection;

/**
 * Filters for {@code GET /v1/alerts}.
 *
 * <p>Setters are fluent and every unset filter is omitted from the request:
 *
 * <pre>{@code
 * new AlertsParams().types("price_alert", "52w_high").important(true)
 * }</pre>
 *
 * @see in.manasija.drishti.DrishtiClient#getAlerts(AlertsParams)
 * @see <a href="https://drishti.manasija.in/docs/api-reference/alerts/list-market-alerts">API reference</a>
 * @since 1.0.0
 */
public final class AlertsParams extends QueryParams<AlertsParams> {

    /** Creates an empty filter set; every filter is optional. */
    public AlertsParams() {
    }

    /**
     * Filters by NSE trading symbols, for example {@code RELIANCE}.
     *
     * <p>Sent as one comma-separated {@code symbols} value. An empty list sends no filter.
     *
     * @param values the trading symbols
     * @return this object, for chaining
     */
    public AlertsParams symbols(String... values) {
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
    public AlertsParams symbols(Collection<String> values) {
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
    public AlertsParams scripCodes(String... values) {
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
    public AlertsParams scripCodes(Collection<String> values) {
        return setList("scrip_codes", values);
    }

    /**
     * Filters by alert type; see {@link in.manasija.drishti.Types#ALERT_TYPES} for known values.
     *
     * <p>Sent as one comma-separated {@code type} value. An empty list sends no filter.
     *
     * @param values the alert types
     * @return this object, for chaining
     */
    public AlertsParams types(String... values) {
        return setList("type", values == null ? null : Arrays.asList(values));
    }

    /**
     * Filters by alert type; see {@link in.manasija.drishti.Types#ALERT_TYPES} for known values.
     *
     * <p>Sent as one comma-separated {@code type} value. An empty list sends no filter.
     *
     * @param values the alert types, or {@code null} to clear the filter
     * @return this object, for chaining
     */
    public AlertsParams types(Collection<String> values) {
        return setList("type", values);
    }

    /**
     * Returns records on or after this date or timestamp.
     *
     * @param value the start, as an ISO date ({@code 2026-01-31}) or timestamp
     *     ({@code 2026-01-31T23:59:59Z}), or {@code null} to clear it
     * @return this object, for chaining
     */
    public AlertsParams from(String value) {
        return set("from", value);
    }

    /**
     * Returns records on or after this date or timestamp.
     *
     * @param value the start date, or {@code null} to clear it
     * @return this object, for chaining
     */
    public AlertsParams from(LocalDate value) {
        return set("from", value == null ? null : value.toString());
    }

    /**
     * Returns records on or before this date or timestamp.
     *
     * @param value the end, as an ISO date ({@code 2026-01-31}) or timestamp
     *     ({@code 2026-01-31T23:59:59Z}), or {@code null} to clear it
     * @return this object, for chaining
     */
    public AlertsParams to(String value) {
        return set("to", value);
    }

    /**
     * Returns records on or before this date or timestamp.
     *
     * @param value the end date, or {@code null} to clear it
     * @return this object, for chaining
     */
    public AlertsParams to(LocalDate value) {
        return set("to", value == null ? null : value.toString());
    }

    /**
     * Returns only items flagged important ({@code true}) or only other items ({@code false}).
     *
     * @param value the importance filter, or {@code null} to clear it
     * @return this object, for chaining
     */
    public AlertsParams important(Boolean value) {
        return set("important", value);
    }

    /**
     * Selects the results page, starting at 1.
     *
     * @param value the page number, starting at 1, or {@code null} to clear it
     * @return this object, for chaining
     */
    public AlertsParams page(Integer value) {
        return set("page", value);
    }

    /**
     * Sets the maximum number of items per page.
     *
     * @param value the page size, or {@code null} to clear it
     * @return this object, for chaining
     */
    public AlertsParams limit(Integer value) {
        return set("limit", value);
    }
}
