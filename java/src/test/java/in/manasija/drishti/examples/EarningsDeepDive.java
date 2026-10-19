package in.manasija.drishti.examples;

import com.fasterxml.jackson.databind.JsonNode;
import in.manasija.drishti.DrishtiClient;
import in.manasija.drishti.exception.DrishtiApiException;
import in.manasija.drishti.params.DetailParams;
import in.manasija.drishti.params.TranscriptParams;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * Use case: an analyst opens one company's quarter. It shows the earnings filing, the extracted
 * results table, the concall transcript links and the source PDF, saved locally for citation.
 *
 * <pre>
 * DRISHTI_API_KEY=... ./mvnw -q test-compile exec:java -Dexec.classpathScope=test \
 *     -Dexec.mainClass=in.manasija.drishti.examples.EarningsDeepDive -Dexec.args="TCS q1_27"
 * </pre>
 */
public final class EarningsDeepDive {

    private EarningsDeepDive() {
    }

    public static void main(String[] args) throws IOException {
        String symbol = args.length > 0 ? args[0] : "TCS";
        String quarter = args.length > 1 ? args[1] : "q1_27";
        DrishtiClient client = DrishtiClient.fromEnvironment();

        JsonNode data = client.getEarningsDetail(new DetailParams(quarter).symbol(symbol).detailed(true));
        System.out.println("Filing " + data.path("id").asText() + " for " + symbol + " " + quarter);
        // The SDK guarantees earnings_table when only earnings_table_extraction was returned.
        System.out.println("Results table: " + data.path("earnings_table"));

        try {
            JsonNode transcript = client.getConcallsTranscript(new TranscriptParams(quarter).symbol(symbol));
            System.out.println("Concall transcript: " + transcript.path("transcript_url").asText("-")
                    + " (audio: " + transcript.path("audio_url").asText("-") + ", link valid "
                    + transcript.path("expires_in").asText("?") + "s)");
        } catch (DrishtiApiException e) {
            System.out.println("No concall transcript yet (" + e.getStatusCode() + ")");
        }

        String earningsId = data.path("id").asText();
        if (!earningsId.isEmpty()) {
            byte[] pdf = client.getEarningsCitationPdf(earningsId);
            Path out = Files.write(Path.of(symbol + "-" + quarter + ".pdf"), pdf);
            System.out.println("Saved source PDF (" + pdf.length + " bytes) to " + out.toAbsolutePath());
        }
    }
}
