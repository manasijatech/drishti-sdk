package in.manasija.drishti;

/**
 * Content retention preference sent as the {@code X-Alpha-Content-Retention} header.
 *
 * <p>Used by {@link DrishtiClient#postDailySummary(Object, ContentRetention)} and batch job
 * submission. Leave it unset to apply the account's default retention policy.
 *
 * @since 1.0.0
 */
public enum ContentRetention {

    /** Ask the API not to retain the generated content. */
    NONE("none");

    /** The HTTP header that carries the retention preference. */
    public static final String HEADER = "X-Alpha-Content-Retention";

    private final String headerValue;

    ContentRetention(String headerValue) {
        this.headerValue = headerValue;
    }

    /**
     * Returns the value sent in the {@value #HEADER} header.
     *
     * @return the header value, for example {@code none}
     */
    public String headerValue() {
        return headerValue;
    }
}
