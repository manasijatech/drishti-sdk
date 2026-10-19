package in.manasija.drishti;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.exception.DrishtiException;
import in.manasija.drishti.internal.Json;
import java.util.ArrayList;
import java.util.Collection;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.stream.Collectors;

/**
 * Serialisation helpers for query strings and batch JSONL files.
 *
 * <p>Batch jobs take a JSONL file: one {@code POST /v1/daily-summary/} body per line, each with a
 * {@code custom_id}. Build one with {@link #buildBatchInputJsonl(List)}, and read results with
 * {@link #parseBatchResultJsonl(String)}:
 *
 * <pre>{@code
 * String jsonl = Params.buildBatchInputJsonl(List.of(
 *         Map.of("custom_id", "tcs", "symbols", List.of("TCS"), "mode", "news_context"),
 *         Map.of("custom_id", "infy", "symbols", List.of("INFY"))));
 * }</pre>
 *
 * @since 1.0.0
 */
public final class Params {

    private Params() {
    }

    /**
     * Normalises query parameters the way the API expects them.
     *
     * <p>{@code null} values and empty collections are dropped. Collections and arrays are joined
     * with commas, the API's list-filter convention. Booleans become {@code true} or
     * {@code false}. Other values use {@link String#valueOf(Object)}.
     *
     * @param params the raw parameters; iteration order is kept
     * @return the encoded-ready parameters, never {@code null}
     */
    public static Map<String, String> query(Map<String, ?> params) {
        Map<String, String> out = new LinkedHashMap<>();
        if (params == null) {
            return out;
        }
        for (Map.Entry<String, ?> entry : params.entrySet()) {
            Object value = entry.getValue();
            if (value == null) {
                continue;
            }
            if (value instanceof Object[] array) {
                value = List.of(array);
            }
            if (value instanceof Collection<?> collection) {
                if (collection.isEmpty()) {
                    continue;
                }
                out.put(entry.getKey(), collection.stream().map(String::valueOf).collect(Collectors.joining(",")));
                continue;
            }
            out.put(entry.getKey(), String.valueOf(value));
        }
        return out;
    }

    /**
     * Serialises batch input lines to JSONL: one compact JSON object per line, with a trailing
     * newline.
     *
     * @param lines the request bodies, as {@link Map}s, {@link JsonNode}s or serialisable objects
     * @return the JSONL text
     * @throws IllegalArgumentException when {@code lines} is {@code null} or empty
     */
    public static String buildBatchInputJsonl(List<?> lines) {
        if (lines == null || lines.isEmpty()) {
            throw new IllegalArgumentException("At least one batch input line is required");
        }
        StringBuilder out = new StringBuilder();
        for (Object line : lines) {
            out.append(Json.write(line)).append('\n');
        }
        return out.toString();
    }

    /**
     * Parses JSONL text from {@code GET /v1/batch/jobs/{job_id}/results}.
     *
     * <p>Blank lines and lines that are not JSON objects are skipped.
     *
     * @param content the JSONL text
     * @return one object per result line, in file order
     * @throws DrishtiException when a non-blank line is not valid JSON
     */
    public static List<ObjectNode> parseBatchResultJsonl(String content) {
        List<ObjectNode> results = new ArrayList<>();
        if (content == null) {
            return results;
        }
        for (String line : content.split("\r?\n")) {
            if (line.isBlank()) {
                continue;
            }
            JsonNode parsed;
            try {
                parsed = Json.parse(line);
            } catch (JsonProcessingException e) {
                throw new DrishtiException("Invalid JSON line in batch results: " + line, e);
            }
            if (parsed instanceof ObjectNode object) {
                results.add(object);
            }
        }
        return results;
    }
}
