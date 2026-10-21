using System.Text.Json;
using System.Text.Json.Serialization;

namespace Manasija.Drishti.Models;

/// <summary>Your account, its balance and entitlements.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class Account(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The account id.</summary>
    public string? AccountId => GetString("account_id");

    /// <summary>The account name.</summary>
    public string? Name => GetString("name");

    /// <summary>The account status.</summary>
    public string? Status => GetString("status");

    /// <summary>The credit balance.</summary>
    public double? Balance => GetDouble("balance");

    /// <summary>Per-product REST entitlements.</summary>
    public IReadOnlyList<ProductEntitlement> Products => GetList<ProductEntitlement>("products");

    /// <summary>WebSocket add-ons.</summary>
    public IReadOnlyList<WebSocketAddon> WebSocketAddons => GetList<WebSocketAddon>("websocket_addons");

    /// <summary>Live-feed entitlement details.</summary>
    public JsonElement? LiveEntitlement => GetElement("live_entitlement");

    /// <summary>The content retention policy.</summary>
    public JsonElement? RetentionPolicy => GetElement("retention_policy");

    /// <summary>Free-form account metadata.</summary>
    public JsonElement? Metadata => GetElement("metadata");

    /// <summary>When the account was created.</summary>
    public string? CreatedAt => GetString("created_at");

    /// <summary>When the account was last updated.</summary>
    public string? UpdatedAt => GetString("updated_at");
}

/// <summary>A REST product entitlement.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class ProductEntitlement(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The product name.</summary>
    public string? Product => GetString("product");

    /// <summary>Whether the product is enabled.</summary>
    public bool Enabled => GetBoolean("enabled") ?? false;

    /// <summary>Requests per minute allowed.</summary>
    public long? Rpm => GetInt64("rpm");

    /// <summary>Requests per day allowed.</summary>
    public long? Daily => GetInt64("daily");
}

/// <summary>A WebSocket add-on entitlement.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class WebSocketAddon(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The product name.</summary>
    public string? Product => GetString("product");

    /// <summary>Whether the add-on is enabled.</summary>
    public bool Enabled => GetBoolean("enabled") ?? false;

    /// <summary>The add-on tier.</summary>
    public string? Tier => GetString("tier");
}

/// <summary>Today's usage and current balance.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class AccountUsage(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The account id.</summary>
    public string? AccountId => GetString("account_id");

    /// <summary>The credit balance.</summary>
    public double? Balance => GetDouble("balance");

    /// <summary>Credits debited today.</summary>
    public double? DebitedToday => GetDouble("debited_today");

    /// <summary>Credits reserved by in-flight work.</summary>
    public double? Reserved => GetDouble("reserved");

    /// <summary>Live-feed usage counters, by product.</summary>
    public JsonElement? LiveUsage => GetElement("live_usage");

    /// <summary>Rate-limit counters, by product.</summary>
    public JsonElement? RateLimits => GetElement("rate_limits");

    /// <summary>Live-feed entitlement details.</summary>
    public JsonElement? LiveEntitlement => GetElement("live_entitlement");
}

/// <summary>The rate limits that apply to your key.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class AccountLimits(JsonElement json) : DrishtiObject(json)
{
    /// <summary>REST limits by product.</summary>
    public JsonElement? Rest => GetElement("rest");

    /// <summary>Account-wide limits.</summary>
    public JsonElement? Account => GetElement("account");

    /// <summary>WebSocket limits.</summary>
    public JsonElement? WebSocket => GetElement("websocket");
}

/// <summary>A credit ledger entry.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class LedgerEntry(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The entry id.</summary>
    public string? EntryId => GetString("entry_id");

    /// <summary>The account id.</summary>
    public string? AccountId => GetString("account_id");

    /// <summary>The masked API key that incurred the entry.</summary>
    public string? ApiKey => GetString("api_key");

    /// <summary>The entry type, such as <c>debit</c>.</summary>
    public string? EntryType => GetString("entry_type");

    /// <summary>The amount.</summary>
    public double? Amount => GetDouble("amount");

    /// <summary>The balance after the entry.</summary>
    public double? BalanceAfter => GetDouble("balance_after");

    /// <summary>A reference id, such as a request id.</summary>
    public string? ReferenceId => GetString("reference_id");

    /// <summary>The route that incurred the entry.</summary>
    public string? RouteId => GetString("route_id");

    /// <summary>Free-form metadata.</summary>
    public JsonElement? Metadata => GetElement("metadata");

    /// <summary>When the entry was created.</summary>
    public string? CreatedAt => GetString("created_at");
}
