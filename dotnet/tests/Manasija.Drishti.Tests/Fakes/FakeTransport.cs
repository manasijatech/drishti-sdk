using System.Collections.Concurrent;
using System.Text;
using Manasija.Drishti.Http;

namespace Manasija.Drishti.Tests.Fakes;

/// <summary>Records requests and replays scripted responses. Unscripted requests get <c>200 {}</c>.</summary>
internal sealed class FakeTransport : IDrishtiTransport
{
    private readonly ConcurrentQueue<Func<TransportRequest, TransportResponse>> _responses = new();

    public List<TransportRequest> Requests { get; } = [];

    public FakeTransport Enqueue(TransportResponse response)
    {
        _responses.Enqueue(_ => response);
        return this;
    }

    public FakeTransport EnqueueJson(int status, string json, IReadOnlyDictionary<string, string>? headers = null)
    {
        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = "application/json" };
        foreach (var header in headers ?? new Dictionary<string, string>())
        {
            all[header.Key] = header.Value;
        }

        return Enqueue(new TransportResponse(status, all, Encoding.UTF8.GetBytes(json)));
    }

    public FakeTransport EnqueueNetworkError()
    {
        _responses.Enqueue(_ => throw new HttpRequestException("connection refused"));
        return this;
    }

    public FakeTransport EnqueueRedirect(int status, string location)
        => Enqueue(new TransportResponse(status, new Dictionary<string, string> { ["Location"] = location }, null));

    public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Requests)
        {
            Requests.Add(request);
        }

        if (_responses.TryDequeue(out var next))
        {
            return Task.FromResult(next(request));
        }

        return Task.FromResult(new TransportResponse(200, new Dictionary<string, string> { ["Content-Type"] = "application/json" }, Encoding.UTF8.GetBytes("{}")));
    }

    public static DrishtiClient CreateClient(FakeTransport transport, Action<DrishtiClientOptions>? configure = null)
    {
        var options = new DrishtiClientOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://api.test",
            InitialRetryDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero,
        };
        configure?.Invoke(options);
        return new DrishtiClient(options, transport);
    }
}
