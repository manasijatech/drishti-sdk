using System.Net.WebSockets;
using System.Text;
using Manasija.Drishti.Internal;

namespace Manasija.Drishti.WebSocket;

/// <summary>Opens WebSocket connections. Replace it to use a proxy or to script a server in tests.</summary>
public interface IWebSocketConnector
{
    /// <summary>Opens a connection and completes once it is open.</summary>
    /// <param name="uri">The WebSocket URL.</param>
    /// <param name="headers">Headers for the opening handshake.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>The open connection.</returns>
    Task<IWebSocketConnection> ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);
}

/// <summary>An open WebSocket connection carrying text messages.</summary>
public interface IWebSocketConnection : IDisposable
{
    /// <summary>Sends one text message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes when the message is sent.</returns>
    Task SendAsync(string message, CancellationToken cancellationToken);

    /// <summary>Waits for the next text message.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The message, or <see langword="null"/> when the connection closed.</returns>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>Why the connection closed, once <see cref="ReceiveAsync"/> has returned null.</summary>
    string CloseReason { get; }

    /// <summary>Closes the connection gracefully.</summary>
    /// <param name="cancellationToken">Cancels the close handshake.</param>
    /// <returns>A task that completes when closed.</returns>
    Task CloseAsync(CancellationToken cancellationToken);
}

/// <summary>The default connector, built on <see cref="ClientWebSocket"/>.</summary>
public sealed class ClientWebSocketConnector : IWebSocketConnector
{
    /// <inheritdoc />
    public async Task<IWebSocketConnection> ConnectAsync(Uri uri, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        Guard.NotNull(uri, nameof(uri));
        var socket = new ClientWebSocket();
        try
        {
            foreach (var header in headers)
            {
                socket.Options.SetRequestHeader(header.Key, header.Value);
            }

            await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
            return new ClientWebSocketConnection(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private sealed class ClientWebSocketConnection(ClientWebSocket socket) : IWebSocketConnection
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private string _closeReason = "WebSocket closed";

        public string CloseReason => _closeReason;

        public async Task SendAsync(string message, CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            using var message = new MemoryStream();
            while (true)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                }
                catch (WebSocketException ex)
                {
                    _closeReason = Messages.Format("WebSocketConnectionLost", ex.Message);
                    return null;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    var code = (int?)result.CloseStatus ?? 0;
                    _closeReason = string.IsNullOrEmpty(result.CloseStatusDescription)
                        ? Messages.Format("WebSocketClosedWithCode", code)
                        : Messages.Format("WebSocketClosedWithReason", code, result.CloseStatusDescription);
                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(message.ToArray());
                }
            }
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closed", cancellationToken).ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                    // The peer is already gone; nothing left to close.
                }
            }
        }

        public void Dispose()
        {
            socket.Abort();
            socket.Dispose();
            _sendLock.Dispose();
        }
    }
}

/// <summary>Time and randomness for the session. Replace it in tests to control delays and jitter.</summary>
public interface ISessionClock
{
    /// <summary>The current time.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Waits for <paramref name="delay"/>.</summary>
    /// <param name="delay">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes after the delay.</returns>
    Task Delay(TimeSpan delay, CancellationToken cancellationToken);

    /// <summary>Returns a random number in [0, 1), used for reconnect jitter.</summary>
    /// <returns>The number.</returns>
    double NextDouble();
}

/// <summary>The real clock.</summary>
public sealed class SystemSessionClock : ISessionClock
{
    private readonly Random _random = new();
    private readonly object _gate = new();

    /// <summary>A shared instance.</summary>
    public static SystemSessionClock Instance { get; } = new();

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        => delay > TimeSpan.Zero ? Task.Delay(delay, cancellationToken) : Task.CompletedTask;

    /// <inheritdoc />
    public double NextDouble()
    {
        lock (_gate)
        {
            return _random.NextDouble();
        }
    }
}
