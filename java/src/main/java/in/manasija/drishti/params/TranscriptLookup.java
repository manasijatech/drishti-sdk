package in.manasija.drishti.params;

import com.fasterxml.jackson.annotation.JsonProperty;
import java.util.Objects;

/**
 * One company and quarter to look up in {@code POST /v1/concalls/transcripts}.
 *
 * <pre>{@code
 * client.postConcallsTranscripts(List.of(
 *         new TranscriptLookup("TCS", "q4_26"),
 *         new TranscriptLookup("INFY", "q3_26")));
 * }</pre>
 *
 * @param symbol the NSE trading symbol, for example {@code TCS}
 * @param quarter the fiscal quarter, for example {@code q4_26}
 * @see in.manasija.drishti.DrishtiClient#postConcallsTranscripts(java.util.List)
 * @since 1.0.0
 */
public record TranscriptLookup(@JsonProperty("symbol") String symbol, @JsonProperty("quarter") String quarter) {

    /**
     * Validates that both fields are present.
     *
     * @param symbol the NSE trading symbol, for example {@code TCS}
     * @param quarter the fiscal quarter, for example {@code q4_26}
     * @throws NullPointerException when either value is {@code null}
     */
    public TranscriptLookup {
        Objects.requireNonNull(symbol, "symbol");
        Objects.requireNonNull(quarter, "quarter");
    }
}
