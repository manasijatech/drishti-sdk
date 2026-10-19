package in.manasija.drishti;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.fasterxml.jackson.databind.JsonNode;
import in.manasija.drishti.exception.DrishtiException;
import in.manasija.drishti.http.TransportRequest;
import in.manasija.drishti.http.TransportResponse;
import in.manasija.drishti.params.DetailParams;
import in.manasija.drishti.params.NewsParams;
import java.nio.charset.StandardCharsets;
import java.time.LocalDate;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;

/** Java-specific client behaviour that the shared conformance fixtures do not cover. */
class DrishtiClientTest {

    private final List<TransportRequest> requests = new ArrayList<>();

    private DrishtiClient client(List<TransportResponse> responses) {
        return DrishtiClient.builder()
                .apiKey("k")
                .baseUrl("https://api.test")
                .transport(request -> {
                    requests.add(request);
                    return responses.get(requests.size() - 1);
                })
                .build();
    }

    private static TransportResponse json(String body) {
        return new TransportResponse(200, "application/json", body.getBytes(StandardCharsets.UTF_8));
    }

    private static TransportResponse redirect(int status, String location) {
        return new TransportResponse(status, Map.of("Location", location), null);
    }

    @Test
    void sameOriginRedirectKeepsTheApiKey() {
        DrishtiClient client = client(List.of(redirect(308, "/v1/news/"), json("{\"data\":[]}")));

        client.getNews();

        assertThat(requests).hasSize(2);
        assertThat(requests.get(1).uri().toString()).isEqualTo("https://api.test/v1/news/");
        assertThat(requests.get(1).header("X-API-Key")).isEqualTo("k");
    }

    @Test
    void crossOriginRedirectDropsTheApiKey() {
        DrishtiClient client = client(List.of(redirect(302, "https://files.example/obj.pdf"),
                new TransportResponse(200, "application/pdf", new byte[] {1, 2, 3})));

        byte[] pdf = client.getAnnouncementCitationPdf("A1");

        assertThat(pdf).containsExactly(1, 2, 3);
        assertThat(requests.get(1).header("X-API-Key")).isNull();
    }

    @Test
    void httpsToHttpRedirectIsRefused() {
        DrishtiClient client = client(List.of(redirect(302, "http://files.example/obj.pdf")));

        assertThatThrownBy(() -> client.getEarningsCitationPdf("E1"))
                .isInstanceOf(DrishtiException.class)
                .hasMessageContaining("Refusing to follow a redirect from HTTPS");
    }

    @Test
    void seeOtherRedirectSwitchesPostToGetWithoutBody() {
        DrishtiClient client = client(List.of(redirect(303, "/v1/batch/jobs/job_1"), json("{\"id\":\"job_1\"}")));

        JsonNode result = client.postDailySummary(Map.of("symbols", List.of("TCS")));

        assertThat(result.path("id").asText()).isEqualTo("job_1");
        assertThat(requests.get(1).method()).isEqualTo("GET");
        assertThat(requests.get(1).body()).isNull();
        assertThat(requests.get(1).header("Content-Type")).isNull();
    }

    @Test
    void paramsAcceptLocalDatesAndCollections() {
        DrishtiClient client = client(List.of(json("{\"data\":[]}")));

        client.getNews(new NewsParams().symbols(List.of("TCS", "INFY")).from(LocalDate.of(2026, 1, 2)).limit(5));

        assertThat(requests.get(0).uri().getRawQuery()).isEqualTo("symbols=TCS%2CINFY&from=2026-01-02&limit=5");
    }

    @Test
    void jsonRouteReturningTextFailsClearly() {
        DrishtiClient client = client(List.of(new TransportResponse(200, "text/plain", "oops".getBytes(StandardCharsets.UTF_8))));

        assertThatThrownBy(client::getAccount).isInstanceOf(DrishtiException.class).hasMessageContaining("Expected a JSON response");
    }

    @Test
    void requestV1AddsThePrefixOnce() {
        DrishtiClient client = client(List.of(json("{}"), json("{}")));

        client.requestV1("GET", "/news", Map.of("limit", 1), null);
        client.requestV1("GET", "/v1/alerts", null, null);

        assertThat(requests.get(0).uri().toString()).isEqualTo("https://api.test/v1/news?limit=1");
        assertThat(requests.get(1).uri().toString()).isEqualTo("https://api.test/v1/alerts");
    }

    @Test
    void detailParamsRequireAQuarter() {
        assertThatThrownBy(() -> new DetailParams(null)).isInstanceOf(NullPointerException.class);
    }

    @Test
    void builderValidatesItsInputs() {
        assertThatThrownBy(() -> DrishtiClient.builder().build()).isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> DrishtiClient.builder().retryMultiplier(0.5)).isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> DrishtiClient.builder().timeout(java.time.Duration.ZERO))
                .isInstanceOf(IllegalArgumentException.class);
    }
}
