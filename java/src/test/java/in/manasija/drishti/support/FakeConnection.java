package in.manasija.drishti.support;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.exception.DrishtiWebSocketException;
import in.manasija.drishti.internal.Json;
import in.manasija.drishti.websocket.WebSocketConnection;
import java.time.Duration;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.List;
import java.util.function.BiConsumer;

/** Scripted socket: replies are queued by the test or by an onSend hook. */
public final class FakeConnection implements WebSocketConnection {

    public final List<ObjectNode> sent = new ArrayList<>();
    public boolean closed;
    public BiConsumer<FakeConnection, ObjectNode> onSend;

    private final FakeClock clock;
    private final Deque<String> inbox = new ArrayDeque<>();
    private boolean serverClosed;

    public FakeConnection(FakeClock clock) {
        this.clock = clock;
    }

    /** Queue server messages; each argument is a JSON object (Map, ObjectNode, or JSON text). */
    public void push(Object... messages) {
        for (Object message : messages) {
            inbox.add(message instanceof String text ? text : Json.write(message));
        }
    }

    public void closeFromServer() {
        serverClosed = true;
    }

    @Override
    public void send(String text) {
        if (closed || serverClosed) {
            throw new DrishtiWebSocketException("send on closed socket");
        }
        ObjectNode message;
        try {
            message = (ObjectNode) Json.parse(text);
        } catch (Exception e) {
            throw new AssertionError("SDK sent invalid JSON: " + text, e);
        }
        sent.add(message);
        if (onSend != null) {
            onSend.accept(this, message);
        }
    }

    @Override
    public String receive(Duration timeout) {
        if (!inbox.isEmpty()) {
            return inbox.poll();
        }
        if (closed || serverClosed) {
            throw new DrishtiWebSocketException("connection closed by server");
        }
        clock.advance(timeout);
        return null;
    }

    @Override
    public void close() {
        closed = true;
    }

    /** Products of the subscribe messages sent on this socket. */
    public List<String> subscribedProducts() {
        List<String> out = new ArrayList<>();
        for (JsonNode message : sent) {
            if ("subscribe".equals(message.path("op").asText())) {
                out.add(message.path("product").asText());
            }
        }
        return out;
    }
}
