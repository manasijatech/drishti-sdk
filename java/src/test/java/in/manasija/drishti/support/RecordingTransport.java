package in.manasija.drishti.support;

import com.fasterxml.jackson.databind.JsonNode;
import in.manasija.drishti.http.HttpTransport;
import in.manasija.drishti.http.TransportRequest;
import in.manasija.drishti.http.TransportResponse;
import in.manasija.drishti.internal.Json;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;

/** Records every request and replays scripted responses in order (conformance fixture format). */
public final class RecordingTransport implements HttpTransport {

    public final List<TransportRequest> requests = new ArrayList<>();
    private final List<JsonNode> responses;
    private int next;

    /** @param responses fixture responses: {status, json|text, content_type, network_error} */
    public RecordingTransport(List<JsonNode> responses) {
        this.responses = responses;
    }

    @Override
    public TransportResponse send(TransportRequest request) throws IOException {
        requests.add(request);
        if (next >= responses.size()) {
            throw new AssertionError("Unexpected request #" + (next + 1) + ": " + request.method() + " " + request.uri());
        }
        JsonNode response = responses.get(next++);
        if (response.path("network_error").asBoolean(false)) {
            throw new IOException("simulated network failure");
        }
        int status = response.path("status").asInt(200);
        String contentType = response.path("content_type").asText("application/json");
        byte[] body;
        if (response.has("json")) {
            body = Json.write(response.get("json")).getBytes(StandardCharsets.UTF_8);
        } else if (response.has("text")) {
            body = response.get("text").asText().getBytes(StandardCharsets.UTF_8);
        } else {
            body = new byte[0];
        }
        return new TransportResponse(status, contentType, body);
    }
}
