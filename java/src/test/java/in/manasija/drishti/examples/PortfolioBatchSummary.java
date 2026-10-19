package in.manasija.drishti.examples;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.ContentRetention;
import in.manasija.drishti.DrishtiClient;
import in.manasija.drishti.Params;
import in.manasija.drishti.params.BatchJobRequest;
import java.time.Duration;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * Use case: nightly AI summaries for many clients' portfolios in one batch job, instead of one
 * {@code POST /v1/daily-summary/} call per client.
 *
 * <pre>
 * DRISHTI_API_KEY=... ./mvnw -q test-compile exec:java -Dexec.classpathScope=test \
 *     -Dexec.mainClass=in.manasija.drishti.examples.PortfolioBatchSummary
 * </pre>
 */
public final class PortfolioBatchSummary {

    private PortfolioBatchSummary() {
    }

    public static void main(String[] args) {
        DrishtiClient client = DrishtiClient.fromEnvironment();

        Map<String, List<Map<String, Object>>> portfolios = Map.of(
                "client-001", List.of(Map.of("symbol", "TCS", "exposure", 0.6), Map.of("symbol", "INFY", "exposure", 0.4)),
                "client-002", List.of(Map.of("symbol", "RELIANCE", "exposure", 1.0)));

        List<Map<String, Object>> lines = new ArrayList<>();
        portfolios.forEach((clientId, holdings) ->
                lines.add(Map.of("custom_id", clientId, "portfolio", holdings, "mode", "exposure")));

        JsonNode job = client.submitBatchJobAndWait(
                new BatchJobRequest("portfolios.jsonl", Params.buildBatchInputJsonl(lines))
                        .displayName("Nightly portfolio summaries")
                        .contentRetention(ContentRetention.NONE),
                Duration.ofSeconds(5),
                Duration.ofMinutes(30));

        System.out.printf("job %s finished: %s (%s)%n", job.path("id").asText(), job.path("status").asText(),
                job.path("request_counts"));
        for (ObjectNode result : client.getBatchJobsJobIdResultsParsed(job.path("id").asText())) {
            System.out.printf("%s -> %s%n", result.path("custom_id").asText(), result.path("status").asText());
        }
    }
}
