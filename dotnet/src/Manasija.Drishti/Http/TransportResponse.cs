namespace Manasija.Drishti.Http;

/// <summary>A raw HTTP response returned by a <see cref="IDrishtiTransport"/>.</summary>
public sealed class TransportResponse
{
    /// <summary>Creates a response.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="headers">Response headers; names are matched case-insensitively.</param>
    /// <param name="body">The response body; empty when there is none.</param>
    public TransportResponse(int statusCode, IReadOnlyDictionary<string, string>? headers, byte[]? body)
    {
        StatusCode = statusCode;
        Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers is not null)
        {
            foreach (var pair in headers)
            {
                ((Dictionary<string, string>)Headers)[pair.Key] = pair.Value;
            }
        }

        Body = body ?? Array.Empty<byte>();
    }

    /// <summary>The HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>Response headers, keyed case-insensitively.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>The response body bytes; empty when there is none.</summary>
    public byte[] Body { get; }

    /// <summary>The <c>Content-Type</c> header, or an empty string.</summary>
    public string ContentType => Header("Content-Type") ?? string.Empty;

    /// <summary>Returns a header value.</summary>
    /// <param name="name">The header name, in any case.</param>
    /// <returns>The value, or <see langword="null"/> when absent.</returns>
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}
