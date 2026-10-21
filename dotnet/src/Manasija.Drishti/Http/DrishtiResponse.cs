using System.Text;
using System.Text.Json;

namespace Manasija.Drishti.Http;

/// <summary>A successful response from <see cref="IDrishtiClient.RequestAsync"/>.</summary>
public sealed class DrishtiResponse
{
    internal DrishtiResponse(int statusCode, string contentType, byte[] body, JsonElement? json)
    {
        StatusCode = statusCode;
        ContentType = contentType;
        Body = body;
        Json = json;
    }

    /// <summary>The HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>The <c>Content-Type</c> header, or an empty string.</summary>
    public string ContentType { get; }

    /// <summary>The raw body bytes; empty when there is none.</summary>
    public byte[] Body { get; }

    /// <summary>
    /// The parsed body when the response was JSON and not empty; otherwise <see langword="null"/>.
    /// </summary>
    public JsonElement? Json { get; }

    /// <summary>Whether the body is empty (including <c>204 No Content</c>).</summary>
    public bool IsEmpty => Body.Length == 0;

    /// <summary>The body decoded as UTF-8 text.</summary>
    public string Text => Encoding.UTF8.GetString(Body);
}
