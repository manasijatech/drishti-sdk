using System.Net.Http.Headers;
using Manasija.Drishti.Internal;

namespace Manasija.Drishti.Http;

/// <summary>
/// The default transport, built on <see cref="HttpClient"/>.
/// </summary>
/// <remarks>
/// The client follows redirects itself so it can drop the API key on cross-origin hops, so the
/// <see cref="HttpClient"/> must not follow them. <see cref="CreateHandler"/> returns a handler
/// configured that way; the DI extensions and <see cref="DrishtiClient"/> use it automatically.
/// </remarks>
/// <param name="httpClient">The client used to send requests. Its lifetime is owned by the caller.</param>
public sealed class HttpClientTransport(HttpClient httpClient) : IDrishtiTransport
{
    private readonly HttpClient _httpClient = Guard.NotNull(httpClient, nameof(httpClient));

    /// <summary>Creates a handler that does not follow redirects and decompresses responses.</summary>
    /// <returns>A new handler; dispose it with the <see cref="HttpClient"/> that uses it.</returns>
    public static HttpMessageHandler CreateHandler() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
    };

    /// <inheritdoc />
    public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        Guard.NotNull(request, nameof(request));

        using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Uri);
        if (request.Body is not null)
        {
            message.Content = new ByteArrayContent(request.Body);
        }

        foreach (var header in request.Headers)
        {
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Content is not null)
                {
                    message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(header.Value);
                }

                continue;
            }

            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                message.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        try
        {
            using var response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseContentRead, timeout.Token)
                .ConfigureAwait(false);
#if NET8_0_OR_GREATER
            var body = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
#else
            var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
            return new TransportResponse((int)response.StatusCode, CollectHeaders(response), body);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(Messages.Format("RequestTimedOut", request.Method, request.Uri.AbsolutePath, request.Timeout), ex);
        }
    }

    private static Dictionary<string, string> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        if (response.Headers.Location is { } location)
        {
            headers["Location"] = location.OriginalString;
        }

        return headers;
    }
}
