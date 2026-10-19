package in.manasija.drishti.params;

import java.util.Objects;

/**
 * Lookup for {@code GET /v1/concalls/transcript}: one company's transcript and recording URLs for a
 * quarter.
 *
 * <p>The fiscal quarter is required; identify the company with {@link #symbol(String)} or
 * {@link #scripCode(String)}.
 *
 * <p>Setters are fluent and every unset filter is omitted from the request:
 *
 * <pre>{@code
 * new TranscriptParams("q4_26").symbol("TCS")
 * }</pre>
 *
 * @see in.manasija.drishti.DrishtiClient#getConcallsTranscript(TranscriptParams)
 * @see <a href="https://drishti.manasija.in/docs/api-reference/conference-calls/get-transcript-and-recording-urls">API reference</a>
 * @since 1.0.0
 */
public final class TranscriptParams extends QueryParams<TranscriptParams> {

    /**
     * Creates the lookup for a fiscal quarter.
     *
     * @param quarter the fiscal quarter, written {@code q<quarter>_<yy>}, for example {@code q4_26}
     * @throws NullPointerException when {@code quarter} is {@code null}
     */
    public TranscriptParams(String quarter) {
        set("quarter", Objects.requireNonNull(quarter, "quarter"));
    }

    /**
     * Selects the company by NSE trading symbol.
     *
     * @param value the trading symbol, or {@code null} to clear it
     * @return this object, for chaining
     */
    public TranscriptParams symbol(String value) {
        return set("symbol", value);
    }

    /**
     * Selects the company by BSE scrip code.
     *
     * @param value the BSE scrip code, or {@code null} to clear it
     * @return this object, for chaining
     */
    public TranscriptParams scripCode(String value) {
        return set("scrip_code", value);
    }
}
