using System.Text.Json;
using System.Text.Json.Serialization;

namespace Manasija.Drishti.Models;

/// <summary>A JSON object with no typed properties. Read fields through <see cref="DrishtiObject.Json"/>.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class JsonModel(JsonElement json) : DrishtiObject(json);

/// <summary>A page of results: <c>{"data": [...], "has_next": bool}</c>.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class Paginated<T>(JsonElement json) : DrishtiObject(json)
    where T : DrishtiObject
{
    /// <summary>The items on this page.</summary>
    public IReadOnlyList<T> Data => GetList<T>("data");

    /// <summary>Whether another page follows; request it with <c>Page + 1</c>.</summary>
    public bool HasNext => GetBoolean("has_next") ?? false;
}

/// <summary>A list response: <c>{"data": [...]}</c>.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class DataList<T>(JsonElement json) : DrishtiObject(json)
    where T : DrishtiObject
{
    /// <summary>The items.</summary>
    public IReadOnlyList<T> Data => GetList<T>("data");
}

/// <summary>A single-object response: <c>{"data": {...}}</c>.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class DataResponse<T>(JsonElement json) : DrishtiObject(json)
    where T : DrishtiObject
{
    /// <summary>The wrapped object, or <see langword="null"/> when absent.</summary>
    public T? Data => GetObject<T>("data");
}

/// <summary>A lookup result for an announcement or earnings attachment.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class AttachmentLookupItem(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The requested id.</summary>
    public string? Id => GetString("id");

    /// <summary><c>ready</c>, <c>not_found</c>, <c>invalid_id</c> or <c>no_attachment</c>.</summary>
    public string? Status => GetString("status");

    /// <summary>A pre-signed download URL when <see cref="Status"/> is <c>ready</c>.</summary>
    public string? Url => GetString("url");

    /// <summary>Seconds until <see cref="Url"/> expires.</summary>
    public long? ExpiresIn => GetInt64("expires_in");

    /// <summary>A human-readable explanation when no URL is available.</summary>
    public string? Message => GetString("message");
}

/// <summary>A lightweight index row for earnings or concalls.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class IndexItem(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The record id.</summary>
    public string? Id => GetString("id");

    /// <summary>The NSE symbol.</summary>
    public string? Symbol => GetString("symbol");

    /// <summary>The fiscal quarter, such as <c>q4_26</c>.</summary>
    public string? Quarter => GetString("quarter");

    /// <summary>The event date.</summary>
    public string? Date => GetString("date");
}
