package in.manasija.drishti.http;

import java.io.IOException;
import java.io.InterruptedIOException;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Objects;

/**
 * Default {@link HttpTransport}, built on the JDK's {@link HttpClient}.
 *
 * <p>Needs no third-party HTTP library. It does not follow redirects itself: the client does, so it
 * can drop the API key before following a redirect to another host. The underlying
 * {@link HttpClient} is thread-safe, so one transport can serve many threads.
 *
 * @since 1.0.0
 */
public final class JdkHttpTransport implements HttpTransport {

    private final HttpClient httpClient;

    /**
     * Creates a transport with its own {@link HttpClient}.
     *
     * @param connectTimeout the time allowed to open a connection
     */
    public JdkHttpTransport(Duration connectTimeout) {
        this(HttpClient.newBuilder()
                .connectTimeout(Objects.requireNonNull(connectTimeout, "connectTimeout"))
                .followRedirects(HttpClient.Redirect.NEVER)
                .build());
    }

    /**
     * Creates a transport around an existing {@link HttpClient}, for example one with a proxy or a
     * custom SSL context.
     *
     * <p>Build it with {@link HttpClient.Redirect#NEVER}. A client that follows redirects itself
     * would resend the API key to every host it is redirected to.
     *
     * @param httpClient the client to send requests with
     */
    public JdkHttpTransport(HttpClient httpClient) {
        this.httpClient = Objects.requireNonNull(httpClient, "httpClient");
    }

    /**
     * Returns the underlying JDK client.
     *
     * @return the HTTP client this transport sends requests with
     */
    public HttpClient httpClient() {
        return httpClient;
    }

    /**
     * {@inheritDoc}
     *
     * <p>An interrupt while waiting is reported as an {@link InterruptedIOException}, and the
     * thread's interrupt flag is restored.
     */
    @Override
    public TransportResponse send(TransportRequest request) throws IOException {
        HttpRequest.Builder builder = HttpRequest.newBuilder(request.uri()).timeout(request.timeout());
        for (Map.Entry<String, String> header : request.headers().entrySet()) {
            builder.header(header.getKey(), header.getValue());
        }
        HttpRequest.BodyPublisher publisher = request.body() == null
                ? HttpRequest.BodyPublishers.noBody()
                : HttpRequest.BodyPublishers.ofByteArray(request.body());
        builder.method(request.method(), publisher);
        try {
            HttpResponse<byte[]> response = httpClient.send(builder.build(), HttpResponse.BodyHandlers.ofByteArray());
            Map<String, String> headers = new LinkedHashMap<>();
            response.headers().map().forEach((name, values) -> {
                if (!values.isEmpty()) {
                    headers.put(name, values.get(0));
                }
            });
            return new TransportResponse(response.statusCode(), headers, response.body());
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            InterruptedIOException interrupted = new InterruptedIOException("Interrupted while waiting for the Drishti API");
            interrupted.initCause(e);
            throw interrupted;
        }
    }
}
