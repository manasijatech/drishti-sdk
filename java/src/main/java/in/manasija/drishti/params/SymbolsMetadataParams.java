package in.manasija.drishti.params;

import java.util.Arrays;
import java.util.Collection;

/**
 * Filters for {@code GET /v1/symbols/metadata}.
 *
 * <p>Setters are fluent and every unset filter is omitted from the request:
 *
 * <pre>{@code
 * new SymbolsMetadataParams().symbols("TCS", "INFY")
 * }</pre>
 *
 * @see in.manasija.drishti.DrishtiClient#getSymbolsMetadata(SymbolsMetadataParams)
 * @see <a href="https://drishti.manasija.in/docs/api-reference/symbols/get-symbol-metadata">API reference</a>
 * @since 1.0.0
 */
public final class SymbolsMetadataParams extends QueryParams<SymbolsMetadataParams> {

    /** Creates an empty filter set; every filter is optional. */
    public SymbolsMetadataParams() {
    }

    /**
     * Filters by NSE trading symbols, for example {@code RELIANCE}.
     *
     * <p>Sent as one comma-separated {@code symbols} value. An empty list sends no filter.
     *
     * @param values the trading symbols
     * @return this object, for chaining
     */
    public SymbolsMetadataParams symbols(String... values) {
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
    public SymbolsMetadataParams symbols(Collection<String> values) {
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
    public SymbolsMetadataParams scripCodes(String... values) {
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
    public SymbolsMetadataParams scripCodes(Collection<String> values) {
        return setList("scrip_codes", values);
    }
}
