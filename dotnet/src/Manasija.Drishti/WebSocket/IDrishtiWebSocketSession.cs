using System.Text.Json;
using Manasija.Drishti.Exceptions;
using Manasija.Drishti.Models;

namespace Manasija.Drishti.WebSocket;

/// <summary>
/// A live-feed session. It reconnects with backoff, re-sends every subscription after each
/// reconnect, and reconnects when the server goes silent. Handlers run one at a time, in order,
/// off the socket's receive loop, so a slow handler never stalls the connection.
/// </summary>
/// <example>
/// <code>
/// await using var session = client.CreateWebSocketSession();
/// session.OnNews(news => Console.WriteLine($"{news.Symbol}: {news.Title}"));
/// await session.SubscribeAsync(new SubscribeOptions("news") { Symbols = ["TCS", "INFY"] });
/// await session.RunAsync(cancellationToken);
/// </code>
/// </example>
public interface IDrishtiWebSocketSession : IAsyncDisposable
{
    /// <summary>Whether a connection is open.</summary>
    bool IsConnected { get; }

    /// <summary>When the last message of any kind arrived.</summary>
    DateTimeOffset? LastMessageReceivedAt { get; }

    /// <summary>When the last heartbeat arrived.</summary>
    DateTimeOffset? LastHeartbeatReceivedAt { get; }

    /// <summary>The current subscriptions, normalized, one per product.</summary>
    IReadOnlyList<SubscribeOptions> Subscriptions { get; }

    /// <summary>Starts connecting in the background and returns immediately.</summary>
    /// <param name="cancellationToken">Cancels the start only; use <see cref="StopAsync"/> to stop.</param>
    /// <returns>A completed task.</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Connects (if not started) and runs until <paramref name="cancellationToken"/> is cancelled or <see cref="StopAsync"/> is called.</summary>
    /// <param name="cancellationToken">Stops the session when cancelled.</param>
    /// <returns>A task that completes when the session stops.</returns>
    Task RunAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the connection and stops reconnecting. Pending <see cref="ReadEventsAsync"/> loops end.</summary>
    /// <returns>A task that completes when the session has stopped.</returns>
    Task StopAsync();

    /// <summary>
    /// Subscribes to a product, replacing any earlier subscription to it. When connected, waits
    /// for the server's acknowledgement; otherwise the subscription is sent once connected.
    /// </summary>
    /// <param name="options">What to subscribe to.</param>
    /// <param name="cancellationToken">Cancels waiting for the acknowledgement.</param>
    /// <returns>A task that completes when acknowledged, or immediately when not connected.</returns>
    /// <exception cref="ArgumentException">The product is unknown.</exception>
    /// <exception cref="DrishtiWebSocketException">The server rejected the subscription, or it was not acknowledged in time.</exception>
    Task SubscribeAsync(SubscribeOptions options, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to a product for the given symbols, with detail fields.</summary>
    /// <param name="product">One of <see cref="DrishtiConstants.WebSocketProducts"/>.</param>
    /// <param name="symbols">Symbols; null or empty means every symbol your tier allows.</param>
    /// <param name="cancellationToken">Cancels waiting for the acknowledgement.</param>
    /// <returns>A task that completes when acknowledged, or immediately when not connected.</returns>
    Task SubscribeAsync(string product, IEnumerable<string>? symbols = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams events as they arrive (heartbeats and subscribe acknowledgements excluded). Events
    /// are buffered from the moment enumeration starts until the session stops.
    /// </summary>
    /// <param name="cancellationToken">Ends the stream.</param>
    /// <returns>The events.</returns>
    IAsyncEnumerable<WebSocketEvent> ReadEventsAsync(CancellationToken cancellationToken = default);

    /// <summary>Handles data on a channel, as raw JSON.</summary>
    /// <param name="channel">A product name, such as <c>news</c>.</param>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession On(string channel, Func<JsonElement, Task> handler);

    /// <summary>Handles news.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnNews(Func<NewsItem, Task> handler);

    /// <summary>Handles news.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnNews(Action<NewsItem> handler);

    /// <summary>Handles block deals.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnBlockDeals(Func<BlockDealItem, Task> handler);

    /// <summary>Handles block deals.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnBlockDeals(Action<BlockDealItem> handler);

    /// <summary>Handles announcements.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnAnnouncements(Func<Announcement, Task> handler);

    /// <summary>Handles announcements.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnAnnouncements(Action<Announcement> handler);

    /// <summary>Handles earnings.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnEarnings(Func<Earnings, Task> handler);

    /// <summary>Handles earnings.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnEarnings(Action<Earnings> handler);

    /// <summary>Handles concalls.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnConcalls(Func<Concall, Task> handler);

    /// <summary>Handles concalls.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnConcalls(Action<Concall> handler);

    /// <summary>Handles alerts.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnAlerts(Func<Alert, Task> handler);

    /// <summary>Handles alerts.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnAlerts(Action<Alert> handler);

    /// <summary>Handles every data event, on any channel.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnData(Func<DataEvent, Task> handler);

    /// <summary>Handles subscribe acknowledgements.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnSubscribed(Func<SubscribedEvent, Task> handler);

    /// <summary>Handles server errors and unparseable messages.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnError(Func<ErrorEvent, Task> handler);

    /// <summary>Handles heartbeats.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnHeartbeat(Func<HeartbeatEvent, Task> handler);

    /// <summary>Handles messages the SDK does not recognise.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnRaw(Func<RawEvent, Task> handler);

    /// <summary>Handles every event.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnMessage(Func<WebSocketEvent, Task> handler);

    /// <summary>Runs after each successful connect, once subscriptions are re-sent.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnOpen(Func<Task> handler);

    /// <summary>Runs when an open connection drops, with the reason. Not called by <see cref="StopAsync"/>.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnClose(Func<string, Task> handler);

    /// <summary>Runs before each delayed reconnect attempt, with the attempt number, delay and reason.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnReconnectAttempt(Func<int, TimeSpan, string, Task> handler);

    /// <summary>Runs when reconnecting keeps failing at the maximum delay.</summary>
    /// <param name="handler">The handler, given the attempt count and last failure reason.</param>
    /// <returns>This session.</returns>
    IDrishtiWebSocketSession OnReconnectWarning(Func<int, string, Task> handler);
}
