using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Manasija.Drishti.WebSocket;

namespace Manasija.Drishti.Tests.Fakes;

/// <summary>A scripted server connection. Messages the client sends can be answered by <see cref="Responder"/>.</summary>
internal sealed class FakeConnection : IWebSocketConnection
{
    private readonly Channel<string?> _inbound = Channel.CreateUnbounded<string?>();

    public FakeConnection(Func<JsonElement, string?>? responder)
    {
        Responder = responder;
    }

    public Func<JsonElement, string?>? Responder { get; set; }

    public ConcurrentQueue<string> Sent { get; } = new();

    public string CloseReason { get; private set; } = "WebSocket closed";

    public bool Disposed { get; private set; }

    public void ServerSend(string message) => _inbound.Writer.TryWrite(message);

    public void ServerClose(string reason)
    {
        CloseReason = reason;
        _inbound.Writer.TryWrite(null);
    }

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        if (Disposed)
        {
            throw new ObjectDisposedException(nameof(FakeConnection));
        }

        Sent.Enqueue(message);
        using var document = JsonDocument.Parse(message);
        if (Responder?.Invoke(document.RootElement) is { } reply)
        {
            ServerSend(reply);
        }

        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (await _inbound.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false) && _inbound.Reader.TryRead(out var message))
        {
            return message;
        }

        return null;
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        _inbound.Writer.TryWrite(null);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Disposed = true;
        _inbound.Writer.TryComplete();
    }
}

/// <summary>Hands out connections (or failures) in order, then default connections.</summary>
internal sealed class ScriptedConnector : IWebSocketConnector
{
    private readonly ConcurrentQueue<Func<FakeConnection>> _script = new();

    public ScriptedConnector(Func<JsonElement, string?>? defaultResponder = null)
    {
        DefaultResponder = defaultResponder ?? AckEverySubscribe;
    }

    public Func<JsonElement, string?> DefaultResponder { get; set; }

    public ConcurrentQueue<FakeConnection> Connections { get; } = new();

    public ConcurrentQueue<(Uri Uri, IReadOnlyDictionary<string, string> Headers)> Attempts { get; } = new();

    public ScriptedConnector ThenFail(string message)
    {
        _script.Enqueue(() => throw new IOException(message));
        return this;
    }

    public ScriptedConnector ThenConnect(Func<JsonElement, string?>? responder = null)
    {
        _script.Enqueue(() => new FakeConnection(responder ?? DefaultResponder));
        return this;
    }

    public FakeConnection Latest => Connections.Last();

    public Task<IWebSocketConnection> ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Attempts.Enqueue((uri, new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)));
        var connection = _script.TryDequeue(out var next) ? next() : new FakeConnection(DefaultResponder);
        Connections.Enqueue(connection);
        return Task.FromResult<IWebSocketConnection>(connection);
    }

    public static string? AckEverySubscribe(JsonElement message)
    {
        if (message.GetProperty("op").GetString() != "subscribe")
        {
            return null;
        }

        var product = message.GetProperty("product").GetString();
        return $"{{\"status\":\"subscribed\",\"product\":\"{product}\",\"tier\":\"scale\",\"full_feed\":false,\"symbols\":{message.GetProperty("symbols").GetRawText()}}}";
    }
}

internal static class Eventually
{
    public static async Task True(Func<bool> condition, string because, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new AssertFailedException($"Timed out waiting until {because}");
            }

            await Task.Delay(10);
        }
    }
}
