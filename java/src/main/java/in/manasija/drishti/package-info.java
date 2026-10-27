/**
 * The Drishti API client: {@link in.manasija.drishti.DrishtiClient} and its shared types.
 *
 * <pre>{@code
 * DrishtiClient client = DrishtiClient.fromEnvironment(); // reads DRISHTI_API_KEY
 * JsonNode announcements = client.getAnnouncements(new AnnouncementsParams()
 *         .symbols("RELIANCE")
 *         .categories("Board Meeting")
 *         .limit(5));
 * }</pre>
 *
 * @see <a href="https://drishti.manasija.in/docs">Drishti API documentation</a>
 * @since 1.0.0
 */
package in.manasija.drishti;
