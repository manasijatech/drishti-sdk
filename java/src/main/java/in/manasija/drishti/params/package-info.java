/**
 * Fluent request parameters for the endpoints of {@link in.manasija.drishti.DrishtiClient}.
 *
 * <p>Each class matches one endpoint, or a group of endpoints that take the same filters, and has
 * one setter per filter. Unset filters are not sent:
 *
 * <pre>{@code
 * JsonNode news = client.getNews(new NewsParams()
 *         .symbols("RELIANCE", "TCS")
 *         .from(LocalDate.of(2026, 1, 1))
 *         .limit(10));
 * }</pre>
 *
 * @since 1.0.0
 */
package in.manasija.drishti.params;
