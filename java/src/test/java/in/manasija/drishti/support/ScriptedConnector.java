package in.manasija.drishti.support;

import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.internal.Json;
import in.manasija.drishti.websocket.WebSocketConnection;
import in.manasija.drishti.websocket.WebSocketConnector;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.function.BiConsumer;

/** Hands out FakeConnections; {@code configure} scripts each one (by 1-based connection number). */
public final class ScriptedConnector implements WebSocketConnector {

    /** Configures connection number {@code n}; may throw to simulate a failed connect. */
    @FunctionalInterface
    public interface Configure {
        void apply(int n, FakeConnection connection);
    }

    public record Call(String url, Map<String, String> headers) {
    }

    public final List<FakeConnection> connections = new ArrayList<>();
    public final List<Call> calls = new ArrayList<>();
    private final FakeClock clock;
    private final Configure configure;

    public ScriptedConnector(FakeClock clock, Configure configure) {
        this.clock = clock;
        this.configure = configure;
    }

    @Override
    public WebSocketConnection connect(String url, Map<String, String> headers) {
        calls.add(new Call(url, new LinkedHashMap<>(headers)));
        FakeConnection connection = new FakeConnection(clock);
        connections.add(connection);
        configure.apply(connections.size(), connection);
        return connection;
    }

    /** onSend hook that acknowledges every subscribe. */
    public static BiConsumer<FakeConnection, ObjectNode> acknowledge(String tier) {
        return (connection, message) -> {
            ObjectNode ack = Json.object();
            ack.put("status", "subscribed");
            ack.put("product", message.path("product").asText(""));
            ack.put("tier", tier);
            ack.put("full_feed", false);
            ack.set("symbols", message.has("symbols") ? message.get("symbols") : Json.MAPPER.createArrayNode());
            ack.put("detailed", message.path("detailed").asBoolean(true));
            connection.push(ack);
        };
    }

    public static BiConsumer<FakeConnection, ObjectNode> acknowledge() {
        return acknowledge("pro");
    }

    /** onSend hook that rejects every subscribe with a server error. */
    public static BiConsumer<FakeConnection, ObjectNode> reject(String message, String code) {
        return (connection, sent) -> connection.push(Map.of("error", message, "code", code));
    }
}
