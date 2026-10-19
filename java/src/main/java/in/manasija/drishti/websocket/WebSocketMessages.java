package in.manasija.drishti.websocket;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ArrayNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.internal.Json;
import in.manasija.drishti.websocket.event.DataEvent;
import in.manasija.drishti.websocket.event.ErrorEvent;
import in.manasija.drishti.websocket.event.HeartbeatEvent;
import in.manasija.drishti.websocket.event.RawEvent;
import in.manasija.drishti.websocket.event.SubscribedEvent;
import in.manasija.drishti.websocket.event.WebSocketEvent;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collection;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.regex.Pattern;

/**
 * Pure helpers for the Drishti websocket wire protocol ({@code /v1/ws}).
 *
 * <p>{@link DrishtiWebSocketSession} uses these helpers. They are public so that applications
 * which manage their own sockets can share the SDK's exact message format.
 *
 * @see <a href="https://drishti.manasija.in/docs/guides/websockets">Websocket protocol reference</a>
 * @since 1.0.0
 */
public final class WebSocketMessages {

    private static final Pattern HTTPS = Pattern.compile("^https:", Pattern.CASE_INSENSITIVE);
    private static final Pattern HTTP = Pattern.compile("^http:", Pattern.CASE_INSENSITIVE);

    private WebSocketMessages() {
    }

    /**
     * Builds the websocket URL for an HTTP base URL: {@code https} becomes {@code wss}, and
     * {@code http} becomes {@code ws}.
     *
     * @param baseUrl the API base URL, for example {@code https://developers.manasija.in}
     * @return the websocket URL, for example {@code wss://developers.manasija.in/v1/ws}
     */
    public static String buildWebSocketUrl(String baseUrl) {
        return buildWebSocketUrl(baseUrl, null);
    }

    /**
     * Builds the websocket URL, with the API key in the query string.
     *
     * <p>Use this only where handshake headers cannot be set. The SDK itself sends the key in the
     * {@code X-API-Key} header.
     *
     * @param baseUrl the API base URL
     * @param apiKey the API key to add as {@code ?api_key=}, or {@code null} to omit it
     * @return the websocket URL
     */
    public static String buildWebSocketUrl(String baseUrl, String apiKey) {
        String base = baseUrl.strip();
        while (base.endsWith("/")) {
            base = base.substring(0, base.length() - 1);
        }
        base = HTTPS.matcher(base).replaceFirst("wss:");
        base = HTTP.matcher(base).replaceFirst("ws:");
        String url = base + "/v1/ws";
        if (apiKey != null && !apiKey.isEmpty()) {
            url += "?api_key=" + URLEncoder.encode(apiKey, StandardCharsets.UTF_8);
        }
        return url;
    }

    /**
     * Builds a {@code subscribe} message.
     *
     * <p>Symbols are trimmed, upper-cased and de-duplicated. Categories are trimmed and
     * de-duplicated, and sent only when at least one remains.
     *
     * <pre>{@code
     * WebSocketMessages.buildSubscribeMessage("block_deals", List.of(" reliance "), List.of(), true);
     * // {"op":"subscribe","product":"block-deals","symbols":["RELIANCE"],"detailed":true}
     * }</pre>
     *
     * @param product the SDK product name, for example {@code announcements}
     * @param symbols the symbols to watch; empty for the full feed (Scale plan only)
     * @param categories announcement categories to filter on; ignored by other products
     * @param detailed {@code true} for the detail payload shape, {@code false} for the summary
     * @return the message, ready to serialise
     */
    public static ObjectNode buildSubscribeMessage(
            String product, Collection<String> symbols, Collection<String> categories, boolean detailed) {
        ObjectNode message = Json.object();
        message.put("op", "subscribe");
        message.put("product", Products.toWire(product));
        ArrayNode symbolArray = message.putArray("symbols");
        normalizeSymbols(symbols).forEach(symbolArray::add);
        message.put("detailed", detailed);
        List<String> normalizedCategories = normalizeCategories(categories);
        if (!normalizedCategories.isEmpty()) {
            ArrayNode categoryArray = message.putArray("categories");
            normalizedCategories.forEach(categoryArray::add);
        }
        return message;
    }

    /**
     * Parses one text frame into a typed event. It never throws.
     *
     * <p>Fields are checked in this order: {@code type} (heartbeat), {@code status} (subscribe
     * acknowledgement), {@code error}, then {@code channel} (data). Any other JSON object becomes a
     * {@link RawEvent}. Text that is not a JSON object becomes an {@link ErrorEvent}.
     *
     * @param raw the frame text
     * @return the parsed event
     */
    public static WebSocketEvent parse(String raw) {
        JsonNode parsed;
        try {
            parsed = raw == null ? null : Json.parse(raw);
        } catch (JsonProcessingException e) {
            return new ErrorEvent("Invalid JSON", null);
        }
        if (parsed == null || parsed.isMissingNode()) {
            return new ErrorEvent("Invalid JSON", null);
        }
        if (!(parsed instanceof ObjectNode payload)) {
            return new ErrorEvent("Expected a JSON object", null);
        }

        if ("heartbeat".equals(textOrNull(payload.get("type")))) {
            return new HeartbeatEvent(stringOrEmpty(payload.get("sent_at")));
        }
        if ("subscribed".equals(textOrNull(payload.get("status")))) {
            JsonNode detailed = payload.get("detailed");
            return new SubscribedEvent(
                    Products.normalize(stringOrEmpty(payload.get("product"))),
                    stringOrEmpty(payload.get("tier")),
                    truthy(payload.get("full_feed")),
                    stringList(payload.get("symbols")),
                    stringList(payload.get("categories")),
                    detailed == null || !(detailed.isBoolean() && !detailed.booleanValue()));
        }
        if (payload.has("error")) {
            JsonNode error = payload.get("error");
            JsonNode code = payload.get("code");
            String message = isEmptyValue(error) ? "Unknown error" : stringify(error);
            return new ErrorEvent(message, code == null || code.isNull() ? null : stringify(code));
        }
        JsonNode channel = payload.get("channel");
        if (channel != null && !channel.isNull()) {
            String name = Products.normalize(stringify(channel));
            JsonNode data = payload.get("data");
            if (data instanceof ObjectNode object) {
                return new DataEvent(name, object);
            }
            ObjectNode wrapped = Json.object();
            wrapped.set("raw", data == null ? Json.MAPPER.nullNode() : data);
            return new DataEvent(name, wrapped);
        }
        return new RawEvent(payload);
    }

    /**
     * Normalises symbols for a subscribe message: trimmed, upper-cased, de-duplicated, without
     * blanks.
     *
     * @param symbols the raw symbols, or {@code null}
     * @return the normalised symbols, in first-seen order
     */
    public static List<String> normalizeSymbols(Collection<String> symbols) {
        Set<String> out = new LinkedHashSet<>();
        if (symbols != null) {
            for (String symbol : symbols) {
                String token = symbol == null ? "" : symbol.strip().toUpperCase(Locale.ROOT);
                if (!token.isEmpty()) {
                    out.add(token);
                }
            }
        }
        return List.copyOf(out);
    }

    /**
     * Normalises announcement categories: trimmed, de-duplicated, without blanks. Case is kept.
     *
     * @param categories the raw categories, or {@code null}
     * @return the normalised categories, in first-seen order
     */
    public static List<String> normalizeCategories(Collection<String> categories) {
        Set<String> out = new LinkedHashSet<>();
        if (categories != null) {
            for (String category : categories) {
                String token = category == null ? "" : category.strip();
                if (!token.isEmpty()) {
                    out.add(token);
                }
            }
        }
        return List.copyOf(out);
    }

    private static String textOrNull(JsonNode node) {
        return node != null && node.isTextual() ? node.asText() : null;
    }

    private static String stringOrEmpty(JsonNode node) {
        if (node == null || node.isNull() || (node.isBoolean() && !node.booleanValue())) {
            return "";
        }
        return stringify(node);
    }

    private static String stringify(JsonNode node) {
        if (node.isTextual() || node.isNumber() || node.isBoolean()) {
            return node.asText();
        }
        return node.toString();
    }

    /** PHP-style truthiness, so every SDK reads loosely typed flags the same way. */
    private static boolean truthy(JsonNode node) {
        if (node == null || node.isNull()) {
            return false;
        }
        if (node.isBoolean()) {
            return node.booleanValue();
        }
        if (node.isNumber()) {
            return node.doubleValue() != 0.0;
        }
        if (node.isTextual()) {
            String text = node.asText();
            return !text.isEmpty() && !text.equals("0");
        }
        return node.size() > 0;
    }

    private static boolean isEmptyValue(JsonNode node) {
        if (node == null || node.isNull()) {
            return true;
        }
        if (node.isTextual()) {
            return node.asText().isEmpty();
        }
        if (node.isBoolean()) {
            return !node.booleanValue();
        }
        if (node.isNumber()) {
            return node.doubleValue() == 0.0;
        }
        return false;
    }

    private static List<String> stringList(JsonNode node) {
        List<String> out = new ArrayList<>();
        if (node instanceof ArrayNode array) {
            array.forEach(item -> out.add(stringify(item)));
        }
        return out;
    }
}
