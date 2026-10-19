package in.manasija.drishti;

import static org.assertj.core.api.Assertions.assertThat;

import com.fasterxml.jackson.databind.JsonNode;
import in.manasija.drishti.params.NewsParams;
import in.manasija.drishti.websocket.DrishtiWebSocketSession;
import in.manasija.drishti.websocket.Products;
import in.manasija.drishti.websocket.event.ErrorEvent;
import in.manasija.drishti.websocket.event.SubscribedEvent;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.Timeout;
import org.junit.jupiter.api.condition.EnabledIfEnvironmentVariable;

/**
 * Smoke test against the real Drishti API. It is skipped unless {@code DRISHTI_API_KEY} is set,
 * and uses a handful of credits at most: two news items. Run it with:
 *
 * <pre>
 * DRISHTI_API_KEY=... ./mvnw test -Dtest=LiveSmokeTest
 * </pre>
 */
@EnabledIfEnvironmentVariable(named = "DRISHTI_API_KEY", matches = ".+")
@Timeout(120)
class LiveSmokeTest {

    private final DrishtiClient client = DrishtiClient.fromEnvironment();

    @Test
    void accountEndpointsRespond() {
        JsonNode account = client.getAccount();
        assertThat(account.path("data").path("account_id").asText()).isNotBlank();
        assertThat(client.getAccountUsage()).isNotNull();
        assertThat(client.getAccountLimits()).isNotNull();
    }

    @Test
    void referenceAndFeedEndpointsRespond() {
        JsonNode categories = client.getAnnouncementsCategories().path("data");
        assertThat(categories.path("important").isArray()).isTrue();
        assertThat(categories.path("not_important").isArray()).isTrue();
        JsonNode news = client.getNews(new NewsParams().symbols("RELIANCE").limit(2));
        assertThat(news.path("data").isArray()).isTrue();
        assertThat(news.path("data").size()).isLessThanOrEqualTo(2);
    }

    @Test
    void websocketSubscribeIsAcknowledgedOrRejectedWithAReason() throws InterruptedException {
        boolean entitled = false;
        for (JsonNode addon : client.getAccount().path("data").path("websocket_addons")) {
            if (addon.path("product").asText().equals(Products.ANNOUNCEMENTS) && addon.path("enabled").asBoolean()) {
                entitled = true;
            }
        }
        CountDownLatch answered = new CountDownLatch(1);
        AtomicReference<SubscribedEvent> ack = new AtomicReference<>();
        AtomicReference<ErrorEvent> rejection = new AtomicReference<>();
        DrishtiWebSocketSession ws = client.websocketBuilder().subscribeMaxAttempts(1).build()
                .onSubscribed(event -> {
                    ack.set(event);
                    answered.countDown();
                })
                .onError(event -> {
                    rejection.set(event);
                    answered.countDown();
                });
        ws.subscribe(Products.ANNOUNCEMENTS, List.of("RELIANCE", "TCS"));

        try (ws) {
            ws.start();
            assertThat(answered.await(30, TimeUnit.SECONDS)).as("server answered the subscribe").isTrue();
        }

        if (entitled) {
            assertThat(ack.get()).as("subscribe acknowledged").isNotNull();
            assertThat(ack.get().tier()).isNotBlank();
            assertThat(ack.get().symbols()).containsExactlyInAnyOrder("RELIANCE", "TCS");
        } else {
            assertThat(ack.get() != null || rejection.get() != null).isTrue();
        }
    }
}
