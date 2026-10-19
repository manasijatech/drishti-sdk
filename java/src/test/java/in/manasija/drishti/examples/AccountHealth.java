package in.manasija.drishti.examples;

import com.fasterxml.jackson.databind.JsonNode;
import in.manasija.drishti.DrishtiClient;

/**
 * Use case: an operations check before a large run. It confirms the key works and shows which
 * products and websocket tiers are enabled, plus the remaining credits and rate limits. None of
 * these calls uses credits.
 *
 * <pre>
 * DRISHTI_API_KEY=... ./mvnw -q test-compile exec:java -Dexec.classpathScope=test \
 *     -Dexec.mainClass=in.manasija.drishti.examples.AccountHealth
 * </pre>
 */
public final class AccountHealth {

    private AccountHealth() {
    }

    public static void main(String[] args) {
        DrishtiClient client = DrishtiClient.fromEnvironment();

        JsonNode account = client.getAccount().path("data");
        System.out.printf("Account %s (%s), status %s%n", account.path("name").asText(),
                account.path("account_id").asText(), account.path("status").asText());

        System.out.println("REST products:");
        for (JsonNode product : account.path("products")) {
            System.out.printf("  %-16s enabled=%-5s rpm=%s daily=%s%n", product.path("product").asText(),
                    product.path("enabled").asBoolean(), product.path("rpm").asText("-"), product.path("daily").asText("-"));
        }
        System.out.println("Websocket add-ons:");
        for (JsonNode addon : account.path("websocket_addons")) {
            System.out.printf("  %-16s enabled=%-5s tier=%s%n", addon.path("product").asText(),
                    addon.path("enabled").asBoolean(), addon.path("tier").asText());
        }

        JsonNode usage = client.getAccountUsage();
        System.out.println("Usage: " + usage.toPrettyString());
    }
}
