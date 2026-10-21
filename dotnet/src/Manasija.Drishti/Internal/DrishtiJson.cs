using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Manasija.Drishti.Internal;

/// <summary>Shared JSON settings: snake_case names, nulls omitted so the API applies its own defaults.</summary>
internal static class DrishtiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static byte[] SerializeToUtf8(object? value)
        => value switch
        {
            null => Encoding.UTF8.GetBytes("null"),
            JsonElement element => Encoding.UTF8.GetBytes(element.GetRawText()),
            _ => JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), Options),
        };

    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static JsonElement Parse(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
