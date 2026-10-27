package in.manasija.drishti.examples;

import com.fasterxml.jackson.databind.JsonNode;
import in.manasija.drishti.DrishtiClient;
import in.manasija.drishti.exception.DrishtiApiException;
import in.manasija.drishti.params.AlertsParams;
import in.manasija.drishti.params.AnnouncementsParams;
import in.manasija.drishti.params.NewsParams;
import in.manasija.drishti.params.UpcomingEarningsParams;
import java.time.LocalDate;
import java.util.List;

/**
 * Use case: a research desk's morning digest for a watchlist. It prints the past week's
 * announcements, recent news, market alerts and upcoming earnings dates.
 *
 * <pre>
 * DRISHTI_API_KEY=... ./mvnw -q test-compile exec:java -Dexec.classpathScope=test \
 *     -Dexec.mainClass=in.manasija.drishti.examples.WatchlistDigest -Dexec.args="RELIANCE TCS INFY"
 * </pre>
 */
public final class WatchlistDigest {

    private WatchlistDigest() {
    }

    public static void main(String[] args) {
        List<String> watchlist = args.length > 0 ? List.of(args) : List.of("RELIANCE", "TCS", "INFY");
        DrishtiClient client = DrishtiClient.fromEnvironment();
        LocalDate weekAgo = LocalDate.now().minusDays(7);

        try {
            section("Announcements since " + weekAgo);
            JsonNode announcements = client.getAnnouncements(new AnnouncementsParams()
                    .symbols(watchlist).from(weekAgo).detailed(false).limit(10));
            for (JsonNode item : announcements.path("data")) {
                System.out.printf("  %-10s %-20s %s%n", item.path("symbol").asText(),
                        item.path("category").asText(), item.path("summary").asText());
            }

            section("Latest news");
            JsonNode news = client.getNews(new NewsParams().symbols(watchlist).from(weekAgo).limit(10));
            for (JsonNode item : news.path("data")) {
                System.out.printf("  %-10s [%s] %s%n", item.path("symbol").asText(),
                        item.path("sentiment").asText("-"), item.path("title").asText());
            }

            section("Market alerts");
            JsonNode alerts = client.getAlerts(new AlertsParams().symbols(watchlist).from(weekAgo).limit(10));
            for (JsonNode alert : alerts.path("data")) {
                JsonNode price = alert.path("price");
                String move = price.isObject()
                        ? String.format(" (%.2f, %+.2f%%)", price.path("value").asDouble(), price.path("change_percent").asDouble())
                        : "";
                System.out.printf("  %-10s %-14s %s%s%n", alert.path("symbol").asText(),
                        alert.path("type").asText(), alert.path("reason").asText(), move);
            }

            section("Upcoming earnings");
            JsonNode upcoming = client.getUpcomingEarnings(new UpcomingEarningsParams().symbols(watchlist).limit(10));
            for (JsonNode item : upcoming.path("data")) {
                System.out.printf("  %-10s %-12s %s%n", item.path("symbol").asText(),
                        item.path("date").asText(), item.path("purpose").asText(item.path("title").asText()));
            }
        } catch (DrishtiApiException e) {
            System.err.println("Drishti API error " + e.getStatusCode() + ": " + e.getDetail());
            System.exit(1);
        }
    }

    private static void section(String title) {
        System.out.println();
        System.out.println("== " + title);
    }
}
