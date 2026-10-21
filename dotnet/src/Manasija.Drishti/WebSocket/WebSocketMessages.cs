using System.Text.Json;
using System.Text.Json.Nodes;
using Manasija.Drishti.Internal;

namespace Manasija.Drishti.WebSocket;

/// <summary>What to subscribe to on the live feed.</summary>
/// <param name="Product">One of <see cref="DrishtiConstants.WebSocketProducts"/>.</param>
public sealed record SubscribeOptions(string Product)
{
    /// <summary>Symbols to receive; empty means every symbol your tier allows. Trimmed, upper-cased and de-duplicated.</summary>
    public IReadOnlyList<string>? Symbols { get; init; }

    /// <summary>Announcement categories to receive. Trimmed and de-duplicated; omitted when empty.</summary>
    public IReadOnlyList<string>? Categories { get; init; }

    /// <summary>Whether payloads include detail fields. Default <see langword="true"/>.</summary>
    public bool Detailed { get; init; } = true;
}

/// <summary>Builds and parses live-feed wire messages. Shared by every Drishti SDK.</summary>
public static class WebSocketMessages
{
    /// <summary>
    /// Converts an HTTP base URL into the WebSocket URL: <c>https</c> becomes <c>wss</c>,
    /// <c>http</c> becomes <c>ws</c>, and <c>/v1/ws</c> is appended after any path prefix.
    /// </summary>
    /// <param name="baseUrl">The REST base URL.</param>
    /// <param name="apiKey">When set, added as the <c>api_key</c> query parameter.</param>
    /// <returns>The WebSocket URL.</returns>
    public static string BuildUrl(string baseUrl, string? apiKey = null)
    {
        Guard.NotNull(baseUrl, nameof(baseUrl));
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "wss:" + trimmed.Substring("https:".Length);
        }
        else if (trimmed.StartsWith("http:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "ws:" + trimmed.Substring("http:".Length);
        }

        var url = trimmed + "/v1/ws";
        return string.IsNullOrEmpty(apiKey) ? url : url + "?api_key=" + UrlEncoding.EncodeForm(apiKey!);
    }

    /// <summary>Builds the JSON <c>subscribe</c> message for <paramref name="options"/>.</summary>
    /// <param name="options">What to subscribe to.</param>
    /// <returns>The message object.</returns>
    public static JsonObject BuildSubscribeMessage(SubscribeOptions options)
    {
        Guard.NotNull(options, nameof(options));
        var message = new JsonObject
        {
            ["op"] = "subscribe",
            ["product"] = ToWireProduct(options.Product),
            ["symbols"] = new JsonArray(NormalizeSymbols(options.Symbols).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            ["detailed"] = options.Detailed,
        };
        var categories = NormalizeCategories(options.Categories);
        if (categories.Count > 0)
        {
            message["categories"] = new JsonArray(categories.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray());
        }

        return message;
    }

    /// <summary>Parses one server message. Never throws: unparseable input becomes an <see cref="ErrorEvent"/>.</summary>
    /// <param name="raw">The message text.</param>
    /// <returns>The parsed event.</returns>
    public static WebSocketEvent Parse(string raw)
    {
        JsonElement payload;
        try
        {
            payload = DrishtiJson.Parse(raw ?? string.Empty);
        }
        catch (JsonException)
        {
            return new ErrorEvent("Invalid JSON", null);
        }

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return new ErrorEvent("Expected a JSON object", null);
        }

        if (payload.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "heartbeat")
        {
            return new HeartbeatEvent(AsString(payload, "sent_at") ?? string.Empty);
        }

        if (payload.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "subscribed")
        {
            return new SubscribedEvent(
                NormalizeProduct(AsString(payload, "product") ?? string.Empty),
                AsString(payload, "tier") ?? string.Empty,
                payload.TryGetProperty("full_feed", out var fullFeed) && IsTruthy(fullFeed),
                AsStringList(payload, "symbols"),
                AsStringList(payload, "categories"),
                !(payload.TryGetProperty("detailed", out var detailed) && detailed.ValueKind == JsonValueKind.False));
        }

        if (payload.TryGetProperty("error", out var error))
        {
            var message = IsTruthy(error) ? (error.ValueKind == JsonValueKind.String ? error.GetString()! : error.GetRawText()) : "Unknown error";
            return new ErrorEvent(message, AsString(payload, "code"));
        }

        if (payload.TryGetProperty("channel", out var channel))
        {
            var name = NormalizeProduct(channel.ValueKind == JsonValueKind.String ? channel.GetString()! : channel.GetRawText());
            var hasData = payload.TryGetProperty("data", out var data);
            if (hasData && data.ValueKind == JsonValueKind.Object)
            {
                return new DataEvent(name, data.Clone());
            }

            var wrapped = new JsonObject { ["raw"] = hasData ? JsonNode.Parse(data.GetRawText()) : null };
            return new DataEvent(name, DrishtiJson.Parse(wrapped.ToJsonString()));
        }

        return new RawEvent(payload);
    }

    /// <summary>Maps the wire product name to the SDK name (<c>block-deals</c> to <c>block_deals</c>).</summary>
    /// <param name="product">The wire name.</param>
    /// <returns>The SDK name.</returns>
    public static string NormalizeProduct(string product) => product == "block-deals" ? "block_deals" : product;

    /// <summary>Maps the SDK product name to the wire name (<c>block_deals</c> to <c>block-deals</c>).</summary>
    /// <param name="product">The SDK name.</param>
    /// <returns>The wire name.</returns>
    public static string ToWireProduct(string product) => product == "block_deals" ? "block-deals" : product;

    internal static IReadOnlyList<string> NormalizeSymbols(IEnumerable<string>? symbols)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var symbol in symbols ?? [])
        {
            var token = (symbol ?? string.Empty).Trim().ToUpperInvariant();
            if (token.Length > 0 && seen.Add(token))
            {
                result.Add(token);
            }
        }

        return result;
    }

    internal static IReadOnlyList<string> NormalizeCategories(IEnumerable<string>? categories)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var category in categories ?? [])
        {
            var token = (category ?? string.Empty).Trim();
            if (token.Length > 0 && seen.Add(token))
            {
                result.Add(token);
            }
        }

        return result;
    }

    private static bool IsTruthy(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.String => value.GetString()!.Length > 0,
        JsonValueKind.Number => value.GetDouble() != 0,
        JsonValueKind.Object or JsonValueKind.Array => true,
        _ => false,
    };

    private static string? AsString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText(),
        };
    }

    private static IReadOnlyList<string> AsStringList(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetRawText())
            .ToList();
    }
}
