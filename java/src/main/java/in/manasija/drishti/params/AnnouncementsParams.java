package in.manasija.drishti.params;

import java.time.LocalDate;
import java.util.Arrays;
import java.util.Collection;

/**
 * Filters for {@code GET /v1/announcements}.
 *
 * <p>Setters are fluent and every unset filter is omitted from the request:
 *
 * <pre>{@code
 * new AnnouncementsParams().symbols("RELIANCE").categories("Board Meeting").detailed(true)
 * }</pre>
 *
 * @see in.manasija.drishti.DrishtiClient#getAnnouncements(AnnouncementsParams)
 * @see <a href="https://drishti.manasija.in/docs/api-reference/announcements/list-announcements">API reference</a>
 * @since 1.0.0
 */
public final class AnnouncementsParams extends QueryParams<AnnouncementsParams> {

    /** Creates an empty filter set; every filter is optional. */
    public AnnouncementsParams() {
    }

    /**
     * Filters by NSE trading symbols, for example {@code RELIANCE}.
     *
     * <p>Sent as one comma-separated {@code symbols} value. An empty list sends no filter.
     *
     * @param values the trading symbols
     * @return this object, for chaining
     */
    public AnnouncementsParams symbols(String... values) {
        return setList("symbols", values == null ? null : Arrays.asList(values));
    }

    /**
     * Filters by NSE trading symbols, for example {@code RELIANCE}.
     *
     * <p>Sent as one comma-separated {@code symbols} value. An empty list sends no filter.
     *
     * @param values the trading symbols, or {@code null} to clear the filter
     * @return this object, for chaining
     */
    public AnnouncementsParams symbols(Collection<String> values) {
        return setList("symbols", values);
    }

    /**
     * Filters by BSE scrip codes, for example {@code 500325}.
     *
     * <p>Sent as one comma-separated {@code scrip_codes} value. An empty list sends no filter.
     *
     * @param values the BSE scrip codes
     * @return this object, for chaining
     */
    public AnnouncementsParams scripCodes(String... values) {
        return setList("scrip_codes", values == null ? null : Arrays.asList(values));
    }

    /**
     * Filters by BSE scrip codes, for example {@code 500325}.
     *
     * <p>Sent as one comma-separated {@code scrip_codes} value. An empty list sends no filter.
     *
     * @param values the BSE scrip codes, or {@code null} to clear the filter
     * @return this object, for chaining
     */
    public AnnouncementsParams scripCodes(Collection<String> values) {
        return setList("scrip_codes", values);
    }

    /**
     * Filters by announcement categories, as listed by {@link in.manasija.drishti.DrishtiClient#getAnnouncementsCategories()}.
     *
     * <p>Sent as one comma-separated {@code categories} value. An empty list sends no filter.
     *
     * @param values the category names
     * @return this object, for chaining
     */
    public AnnouncementsParams categories(String... values) {
        return setList("categories", values == null ? null : Arrays.asList(values));
    }

    /**
     * Filters by announcement categories, as listed by {@link in.manasija.drishti.DrishtiClient#getAnnouncementsCategories()}.
     *
     * <p>Sent as one comma-separated {@code categories} value. An empty list sends no filter.
     *
     * @param values the category names, or {@code null} to clear the filter
     * @return this object, for chaining
     */
    public AnnouncementsParams categories(Collection<String> values) {
        return setList("categories", values);
    }

    /**
     * Returns records on or after this date or timestamp.
     *
     * @param value the start, as an ISO date ({@code 2026-01-31}) or timestamp
     *     ({@code 2026-01-31T23:59:59Z}), or {@code null} to clear it
     * @return this object, for chaining
     */
    public AnnouncementsParams from(String value) {
        return set("from", value);
    }

    /**
     * Returns records on or after this date or timestamp.
     *
     * @param value the start date, or {@code null} to clear it
     * @return this object, for chaining
     */
    public AnnouncementsParams from(LocalDate value) {
        return set("from", value == null ? null : value.toString());
    }

    /**
     * Returns records on or before this date or timestamp.
     *
     * @param value the end, as an ISO date ({@code 2026-01-31}) or timestamp
     *     ({@code 2026-01-31T23:59:59Z}), or {@code null} to clear it
     * @return this object, for chaining
     */
    public AnnouncementsParams to(String value) {
        return set("to", value);
    }

    /**
     * Returns records on or before this date or timestamp.
     *
     * @param value the end date, or {@code null} to clear it
     * @return this object, for chaining
     */
    public AnnouncementsParams to(LocalDate value) {
        return set("to", value == null ? null : value.toString());
    }

    /**
     * Chooses the detail shape ({@code true}) or the summary list shape ({@code false}).
     *
     * @param value {@code true} for the detail shape, or {@code null} to clear it
     * @return this object, for chaining
     */
    public AnnouncementsParams detailed(Boolean value) {
        return set("detailed", value);
    }

    /**
     * Selects the results page, starting at 1.
     *
     * @param value the page number, starting at 1, or {@code null} to clear it
     * @return this object, for chaining
     */
    public AnnouncementsParams page(Integer value) {
        return set("page", value);
    }

    /**
     * Sets the maximum number of items per page.
     *
     * @param value the page size, or {@code null} to clear it
     * @return this object, for chaining
     */
    public AnnouncementsParams limit(Integer value) {
        return set("limit", value);
    }
}
