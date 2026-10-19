package in.manasija.drishti.examples;

import in.manasija.drishti.DrishtiClient;
import in.manasija.drishti.websocket.DrishtiWebSocketSession;
import in.manasija.drishti.websocket.Products;
import java.util.List;

/**
 * Use case: a long-running worker that reacts to filings the moment they are published. It
 * streams announcements, earnings and alerts for a watchlist; the session reconnects and
 * resubscribes on its own.
 *
 * <p>Needs a paid plan with the websocket add-on. Starter allows 100 symbols account-wide, and
 * only Scale may stream the whole market with an empty symbol list.
 *
 * <pre>
 * DRISHTI_API_KEY=... ./mvnw -q test-compile exec:java -Dexec.classpathScope=test \
 *     -Dexec.mainClass=in.manasija.drishti.examples.ListenAnnouncements -Dexec.args="RELIANCE TCS"
 * </pre>
 */
public final class ListenAnnouncements {

    private ListenAnnouncements() {
    }

    public static void main(String[] args) {
        List<String> watchlist = args.length > 0 ? List.of(args) : List.of("RELIANCE", "TCS");
        DrishtiClient client = DrishtiClient.fromEnvironment();

        DrishtiWebSocketSession ws = client.websocket()
                .onOpen(reason -> System.out.println("connected"))
                .onClose(reason -> System.out.println("disconnected: " + reason))
                .onSubscribed(ack -> System.out.printf("subscribed %s on tier %s (%d symbols)%n",
                        ack.product(), ack.tier(), ack.symbols().size()))
                .onError(error -> System.err.println("error: " + error.message()
                        + (error.code() == null ? "" : " [" + error.code() + "]")))
                .onAnnouncements(a -> System.out.printf("[announcement] %s %s: %s%n",
                        a.path("symbol").asText(), a.path("category").asText(), a.path("summary").asText()))
                .onEarnings(e -> System.out.printf("[earnings] %s %s%n", e.path("symbol").asText(), e.path("id").asText()))
                .onAlerts(a -> System.out.printf("[alert] %s %s%n", a.path("symbol").asText(), a.path("type").asText()));

        ws.subscribe(Products.ANNOUNCEMENTS, watchlist);
        ws.subscribe(Products.EARNINGS, watchlist);
        ws.subscribe(Products.ALERTS, watchlist);

        // Stop cleanly on Ctrl+C. run() blocks until stop() is called.
        Runtime.getRuntime().addShutdownHook(new Thread(ws::stop));
        ws.run();
    }
}
