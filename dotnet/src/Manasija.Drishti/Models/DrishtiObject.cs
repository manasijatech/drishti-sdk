using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Manasija.Drishti.Internal;

namespace Manasija.Drishti.Models;

/// <summary>
/// Base type for every response model. A model is a typed view over the JSON the API returned:
/// properties read from <see cref="Json"/> on demand, so new or unexpected fields never break
/// deserialization, and the full payload stays available.
/// </summary>
/// <remarks>
/// A property whose JSON value is missing, <c>null</c> or of an unexpected type returns
/// <see langword="null"/>. Serializing a model with <see cref="JsonSerializer"/> writes the
/// original JSON back unchanged.
/// </remarks>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public abstract class DrishtiObject
{
    private static readonly ConcurrentDictionary<Type, Func<JsonElement, object>> Factories = new();

    /// <summary>Wraps a JSON value.</summary>
    /// <param name="json">The JSON value; cloned so it outlives its document.</param>
    protected DrishtiObject(JsonElement json)
    {
        Json = json.Clone();
    }

    /// <summary>The underlying JSON, exactly as the API returned it (after SDK normalization).</summary>
    public JsonElement Json { get; }

    /// <summary>Deserializes the underlying JSON into your own type.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="options">Serializer options; defaults to snake_case property names.</param>
    /// <returns>The deserialized value.</returns>
    public T? Deserialize<T>(JsonSerializerOptions? options = null)
        => Json.Deserialize<T>(options ?? DrishtiJson.Options);

    /// <summary>Returns the raw JSON text.</summary>
    /// <returns>The JSON text.</returns>
    public override string ToString() => Json.GetRawText();

    /// <summary>Creates a model of type <typeparamref name="T"/> over a JSON value.</summary>
    /// <typeparam name="T">A model type with a public constructor taking a <see cref="JsonElement"/>.</typeparam>
    /// <param name="json">The JSON value.</param>
    /// <returns>The model.</returns>
    public static T From<T>(JsonElement json)
        where T : DrishtiObject
        => (T)Create(typeof(T), json);

    internal static object Create(Type type, JsonElement json)
    {
        var factory = Factories.GetOrAdd(type, static t =>
        {
            var ctor = t.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, [typeof(JsonElement)], null)
                ?? throw new InvalidOperationException(Messages.Format("ModelConstructorMissing", t.FullName));
            return element => ctor.Invoke([element]);
        });
        return factory(json);
    }

    /// <summary>Returns a property value, or <see langword="null"/> when missing or JSON <c>null</c>.</summary>
    /// <param name="name">The JSON property name.</param>
    /// <returns>The value.</returns>
    protected JsonElement? GetElement(string name)
    {
        if (Json.ValueKind == JsonValueKind.Object
            && Json.TryGetProperty(name, out var value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return value;
        }

        return null;
    }

    /// <summary>Reads a string. Numbers and booleans are returned as their JSON text.</summary>
    /// <param name="name">The JSON property name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    protected string? GetString(string name) => GetElement(name) switch
    {
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        { ValueKind: JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False } value => value.GetRawText(),
        _ => null,
    };

    /// <summary>Reads a number as a <see cref="double"/>.</summary>
    /// <param name="name">The JSON property name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    protected double? GetDouble(string name)
        => GetElement(name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out var result) ? result : null;

    /// <summary>Reads a whole number as a <see cref="long"/>.</summary>
    /// <param name="name">The JSON property name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    protected long? GetInt64(string name)
        => GetElement(name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var result) ? result : null;

    /// <summary>Reads a boolean.</summary>
    /// <param name="name">The JSON property name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    protected bool? GetBoolean(string name) => GetElement(name) switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => null,
    };

    /// <summary>Reads an array of strings; non-string items are skipped.</summary>
    /// <param name="name">The JSON property name.</param>
    /// <returns>The values; empty when missing.</returns>
    protected IReadOnlyList<string> GetStringList(string name)
    {
        if (GetElement(name) is not { ValueKind: JsonValueKind.Array } array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToList();
    }

    /// <summary>Reads an array of objects as models; non-object items are skipped.</summary>
    /// <typeparam name="T">The model type.</typeparam>
    /// <param name="name">The JSON property name.</param>
    /// <returns>The models; empty when missing.</returns>
    protected IReadOnlyList<T> GetList<T>(string name)
        where T : DrishtiObject
    {
        if (GetElement(name) is not { ValueKind: JsonValueKind.Array } array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(From<T>)
            .ToList();
    }

    /// <summary>Reads a nested object as a model.</summary>
    /// <typeparam name="T">The model type.</typeparam>
    /// <param name="name">The JSON property name.</param>
    /// <returns>The model, or <see langword="null"/> when missing or not an object.</returns>
    protected T? GetObject<T>(string name)
        where T : DrishtiObject
        => GetElement(name) is { ValueKind: JsonValueKind.Object } value ? From<T>(value) : null;
}

/// <summary>Serializes a <see cref="DrishtiObject"/> as its underlying JSON.</summary>
internal sealed class DrishtiObjectConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeof(DrishtiObject).IsAssignableFrom(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

    private sealed class Converter<T> : JsonConverter<T>
        where T : DrishtiObject
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return (T)DrishtiObject.Create(typeToConvert, document.RootElement);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => value.Json.WriteTo(writer);
    }
}
