namespace Manasija.Drishti.Http;

/// <summary>
/// Sends one HTTP request and returns the raw response. The client handles retries, redirects,
/// headers and response parsing, so a transport only moves bytes.
/// </summary>
/// <remarks>
/// The default is <see cref="HttpClientTransport"/>. Supply your own to route requests through
/// a proxy, or to record and replay requests in tests.
/// </remarks>
public interface IDrishtiTransport
{
    /// <summary>Sends a request once, without following redirects.</summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response, whatever its status code.</returns>
    /// <exception cref="HttpRequestException">The request failed without a response; the client retries it.</exception>
    /// <exception cref="IOException">The connection failed; the client retries it.</exception>
    /// <exception cref="TimeoutException">The request timed out; the client retries it.</exception>
    Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken);
}
