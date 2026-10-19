package in.manasija.drishti.internal;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.JsonNodeFactory;
import com.fasterxml.jackson.databind.node.ObjectNode;
import java.io.UncheckedIOException;

/**
 * Shared Jackson helpers used inside the SDK.
 *
 * <p><strong>Internal API.</strong> This class is public only so the SDK's own packages can share
 * it. It may change without notice; do not use it from application code.
 *
 * @since 1.0.0
 */
public final class Json {

    /** The SDK's shared, thread-safe object mapper. */
    public static final ObjectMapper MAPPER = new ObjectMapper();

    private Json() {
    }

    /**
     * Parses JSON text into a tree.
     *
     * @param text the JSON text
     * @return the parsed tree
     * @throws JsonProcessingException when the text is not valid JSON
     */
    public static JsonNode parse(String text) throws JsonProcessingException {
        return MAPPER.readTree(text);
    }

    /**
     * Serialises a value to compact JSON text.
     *
     * @param value a {@link JsonNode}, a {@link java.util.Map}, a {@link java.util.List}, or any
     *     object Jackson can serialise
     * @return the JSON text
     * @throws UncheckedIOException when the value cannot be serialised
     */
    public static String write(Object value) {
        try {
            return MAPPER.writeValueAsString(value);
        } catch (JsonProcessingException e) {
            throw new UncheckedIOException("Could not serialise value to JSON", e);
        }
    }

    /**
     * Converts a value to a JSON tree.
     *
     * @param value a {@link JsonNode}, a {@link java.util.Map}, a {@link java.util.List}, or any
     *     object Jackson can serialise
     * @return the JSON tree, never {@code null}
     */
    public static JsonNode toTree(Object value) {
        if (value instanceof JsonNode node) {
            return node;
        }
        JsonNode tree = MAPPER.valueToTree(value);
        return tree == null ? JsonNodeFactory.instance.nullNode() : tree;
    }

    /**
     * Creates an empty JSON object.
     *
     * @return a new, empty object node
     */
    public static ObjectNode object() {
        return JsonNodeFactory.instance.objectNode();
    }
}
