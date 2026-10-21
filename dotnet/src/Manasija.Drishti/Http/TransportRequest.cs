namespace Manasija.Drishti.Http;

/// <summary>A fully built HTTP request, ready for a <see cref="IDrishtiTransport"/>.</summary>
/// <param name="Method">The upper-case HTTP method, such as <c>GET</c>.</param>
/// <param name="Uri">The absolute request URI, already percent-encoded.</param>
/// <param name="Headers">Request headers, including <c>Content-Type</c> when there is a body.</param>
/// <param name="Body">The request body, or <see langword="null"/> for none.</param>
/// <param name="Timeout">How long to wait for the response.</param>
public sealed record TransportRequest(
    string Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    byte[]? Body,
    TimeSpan Timeout)
{
    /// <summary>Returns a header value, matching the name case-insensitively.</summary>
    /// <param name="name">The header name.</param>
    /// <returns>The value, or <see langword="null"/> when the header is absent.</returns>
    public string? Header(string name)
    {
        foreach (var pair in Headers)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
