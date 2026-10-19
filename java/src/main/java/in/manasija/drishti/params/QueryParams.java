package in.manasija.drishti.params;

import in.manasija.drishti.Params;
import java.util.Collection;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Base class for the fluent query-parameter objects of each endpoint.
 *
 * <p>Subclasses expose one setter per filter the endpoint accepts. Values left unset, or set to
 * {@code null} or an empty list, are not sent. Instances are not thread-safe; build one per call.
 *
 * @param <S> the concrete subclass, returned by setters for chaining
 * @since 1.0.0
 */
public abstract class QueryParams<S extends QueryParams<S>> {

    private final Map<String, Object> values = new LinkedHashMap<>();

    /** Creates an empty parameter set. */
    protected QueryParams() {
    }

    /**
     * Stores a scalar parameter.
     *
     * @param key the query parameter name, as sent on the wire
     * @param value the value, or {@code null} to remove the parameter
     * @return this object, for chaining
     */
    protected final S set(String key, Object value) {
        values.put(key, value);
        return self();
    }

    /**
     * Stores a list parameter, sent as one comma-separated value.
     *
     * @param key the query parameter name, as sent on the wire
     * @param value the values, or {@code null} to remove the parameter
     * @return this object, for chaining
     * @throws NullPointerException when the collection contains {@code null}
     */
    protected final S setList(String key, Collection<String> value) {
        values.put(key, value == null ? null : List.copyOf(value));
        return self();
    }

    /**
     * Returns the query parameters to send, already normalised by {@link Params#query(Map)}.
     *
     * @return the parameters by wire name, without unset values
     */
    public final Map<String, String> toQuery() {
        return Params.query(values);
    }

    /**
     * Returns the query parameters as a readable string, for logging.
     *
     * @return the normalised parameters
     */
    @Override
    public String toString() {
        return getClass().getSimpleName() + toQuery();
    }

    @SuppressWarnings("unchecked")
    private S self() {
        return (S) this;
    }
}
