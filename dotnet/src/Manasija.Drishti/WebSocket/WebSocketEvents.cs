using System.Text.Json;
using System.Text.Json.Nodes;

namespace Manasija.Drishti.WebSocket;

/// <summary>A message received on the live feed. Switch on the concrete type or on <see cref="Kind"/>.</summary>
public abstract record WebSocketEvent
{
    /// <summary><c>heartbeat</c>, <c>subscribed</c>, <c>error</c>, <c>data</c> or <c>raw</c>.</summary>
    public abstract string Kind { get; }

    /// <summary>Returns the event as snake_case JSON, the shape every Drishti SDK shares.</summary>
    /// <returns>The JSON object.</returns>
    public abstract JsonObject ToJson();
}

/// <summary>A keep-alive sent by the server.</summary>
/// <param name="SentAt">The server timestamp; empty when absent.</param>
public sealed record HeartbeatEvent(string SentAt) : WebSocketEvent
{
    /// <inheritdoc />
    public override string Kind => "heartbeat";

    /// <inheritdoc />
    public override JsonObject ToJson() => new() { ["kind"] = Kind, ["sent_at"] = SentAt };
}

/// <summary>The server accepted a subscription.</summary>
/// <param name="Product">The product, using the SDK name (<c>block_deals</c>, not <c>block-deals</c>).</param>
/// <param name="Tier">Your add-on tier; empty when absent.</param>
/// <param name="FullFeed">Whether you receive every symbol.</param>
/// <param name="Symbols">The subscribed symbols.</param>
/// <param name="Categories">The subscribed announcement categories.</param>
/// <param name="Detailed">Whether payloads include detail fields.</param>
public sealed record SubscribedEvent(
    string Product,
    string Tier,
    bool FullFeed,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<string> Categories,
    bool Detailed) : WebSocketEvent
{
    /// <inheritdoc />
    public override string Kind => "subscribed";

    /// <inheritdoc />
    public override JsonObject ToJson() => new()
    {
        ["kind"] = Kind,
        ["product"] = Product,
        ["tier"] = Tier,
        ["full_feed"] = FullFeed,
        ["symbols"] = new JsonArray(Symbols.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
        ["categories"] = new JsonArray(Categories.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
        ["detailed"] = Detailed,
    };
}

/// <summary>An error sent by the server, or a message the SDK could not parse.</summary>
/// <param name="Message">The error message.</param>
/// <param name="Code">The error code, such as <c>forbidden</c>; null when absent.</param>
public sealed record ErrorEvent(string Message, string? Code) : WebSocketEvent
{
    /// <inheritdoc />
    public override string Kind => "error";

    /// <inheritdoc />
    public override JsonObject ToJson() => new() { ["kind"] = Kind, ["message"] = Message, ["code"] = Code };
}

/// <summary>A data payload on a product channel.</summary>
/// <param name="Channel">The channel, using the SDK name (<c>block_deals</c>). Unknown channels are passed through.</param>
/// <param name="Data">The payload object. A non-object payload is wrapped as <c>{"raw": ...}</c>.</param>
public sealed record DataEvent(string Channel, JsonElement Data) : WebSocketEvent
{
    /// <inheritdoc />
    public override string Kind => "data";

    /// <inheritdoc />
    public override JsonObject ToJson() => new() { ["kind"] = Kind, ["channel"] = Channel, ["data"] = JsonNode.Parse(Data.GetRawText()) };
}

/// <summary>A JSON object the SDK does not recognise.</summary>
/// <param name="Payload">The object.</param>
public sealed record RawEvent(JsonElement Payload) : WebSocketEvent
{
    /// <inheritdoc />
    public override string Kind => "raw";

    /// <inheritdoc />
    public override JsonObject ToJson() => new() { ["kind"] = Kind, ["payload"] = JsonNode.Parse(Payload.GetRawText()) };
}
