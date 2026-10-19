package in.manasija.drishti.params;

import java.time.LocalDate;
import java.util.Arrays;
import java.util.Collection;

/**
 * Filters for {@code GET /v1/block-deals}.
 *
 * <p>Setters are fluent and every unset filter is omitted from the request:
 *
 * <pre>{@code
 * new BlockDealsParams().exchange("NSE").from(LocalDate.now().minusDays(7))
 * }</pre>
 *
 * @see in.manasija.drishti.DrishtiClient#getBlockDeals(BlockDealsParams)
 * @see <a href="https://drishti.manasija.in/docs/api-reference/block-deals/list-block-deals">API reference</a>
 * @since 1.0.0
 */
public final class BlockDealsParams extends QueryParams<BlockDealsParams> {

    /** Creates an empty filter set; every filter is optional. */
    public BlockDealsParams() {
    }

    /**
     * Filters by NSE trading symbols, for example {@code RELIANCE}.
     *
     * <p>Sent as one comma-separated {@code symbols} value. An empty list sends no filter.
     *
     * @param values the trading symbols
     * @return this object, for chaining
     */
    public BlockDealsParams symbols(String... values) {
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
    public BlockDealsParams symbols(Collection<String> values) {
        return setList("symbols", values);
    }

    /**
     * Filters by exchange, for example {@code NSE} or {@code BSE}.
     *
     * @param value the exchange code, or {@code null} to clear it
     * @return this object, for chaining
     */
    public BlockDealsParams exchange(String value) {
        return set("exchange", value);
    }

    /**
     * Returns records on or after this date or timestamp.
     *
     * @param value the start, as an ISO date ({@code 2026-01-31}) or timestamp
     *     ({@code 2026-01-31T23:59:59Z}), or {@code null} to clear it
     * @return this object, for chaining
     */
    public BlockDealsParams from(String value) {
        return set("from", value);
    }

    /**
     * Returns records on or after this date or timestamp.
     *
     * @param value the start date, or {@code null} to clear it
     * @return this object, for chaining
     */
    public BlockDealsParams from(LocalDate value) {
        return set("from", value == null ? null : value.toString());
    }

    /**
     * Returns records on or before this date or timestamp.
     *
     * @param value the end, as an ISO date ({@code 2026-01-31}) or timestamp
     *     ({@code 2026-01-31T23:59:59Z}), or {@code null} to clear it
     * @return this object, for chaining
     */
    public BlockDealsParams to(String value) {
        return set("to", value);
    }

    /**
     * Returns records on or before this date or timestamp.
     *
     * @param value the end date, or {@code null} to clear it
     * @return this object, for chaining
     */
    public BlockDealsParams to(LocalDate value) {
        return set("to", value == null ? null : value.toString());
    }

    /**
     * Selects the results page, starting at 1.
     *
     * @param value the page number, starting at 1, or {@code null} to clear it
     * @return this object, for chaining
     */
    public BlockDealsParams page(Integer value) {
        return set("page", value);
    }

    /**
     * Sets the maximum number of items per page.
     *
     * @param value the page size, or {@code null} to clear it
     * @return this object, for chaining
     */
    public BlockDealsParams limit(Integer value) {
        return set("limit", value);
    }
}
