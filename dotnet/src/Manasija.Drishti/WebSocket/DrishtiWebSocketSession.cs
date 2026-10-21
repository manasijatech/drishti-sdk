using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Manasija.Drishti.Exceptions;
using Manasija.Drishti.Internal;
using Manasija.Drishti.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Manasija.Drishti.WebSocket;

/// <summary>The default <see cref="IDrishtiWebSocketSession"/>. Create one with <see cref="IDrishtiClient.CreateWebSocketSession"/>.</summary>
public sealed class DrishtiWebSocketSession : IDrishtiWebSocketSession
{
    private const string MessageHandlerKey = "message";

    private readonly DrishtiWebSocketOptions _options;
    private readonly IWebSocketConnector _connector;
    private readonly ISessionClock _clock;
    private readonly ILogger _logger;
    private readonly Uri _url;
    private readonly Dictionary<string, string> _headers;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _subscribeLock = new(1, 1);
    private readonly Dictionary<string, SubscribeOptions> _subscriptions = new(StringComparer.Ordinal);
    private readonly List<string> _subscriptionOrder = [];
    private readonly Dictionary<string, List<Func<WebSocketEvent, Task>>> _eventHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Func<JsonElement, Task>>> _channelHandlers = new(StringComparer.Ordinal);
    private readonly List<Func<Task>> _openHandlers = [];
    private readonly List<Func<string, Task>> _closeHandlers = [];
    private readonly List<Func<int, TimeSpan, string, Task>> _reconnectAttemptHandlers = [];
    private readonly List<Func<int, string, Task>> _reconnectWarningHandlers = [];
    private readonly List<Channel<WebSocketEvent>> _readers = [];

    private IWebSocketConnection? _connection;
    private PendingSubscribe? _pending;
    private CancellationTokenSource? _lifetime;
    private Channel<WebSocketEvent>? _dispatch;
    private Task? _runTask;
    private Task? _dispatchTask;
    private long _lastMessageTicks;
    private long _lastHeartbeatTicks;

    /// <summary>Creates a session. It does not connect until <see cref="StartAsync"/> or <see cref="RunAsync"/>.</summary>
    /// <param name="options">Session settings.</param>
    /// <param name="connector">Opens connections; default <see cref="ClientWebSocketConnector"/>.</param>
    /// <param name="clock">Time and jitter; default <see cref="SystemSessionClock"/>.</param>
    /// <param name="logger">Logs connection lifecycle events.</param>
    /// <exception cref="ArgumentException">An option is invalid, such as a blank API key.</exception>
    public DrishtiWebSocketSession(
        DrishtiWebSocketOptions options,
        IWebSocketConnector? connector = null,
        ISessionClock? clock = null,
        ILogger<DrishtiWebSocketSession>? logger = null)
    {
        Guard.NotNull(options, nameof(options));
        if (options.ApiKey is null || options.ApiKey.Trim().Length == 0)
        {
            throw new ArgumentException(Messages.Get("ApiKeyRequired"), nameof(options));
        }

        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true))
        {
            throw new ArgumentException(string.Join(" ", results.Select(r => r.ErrorMessage)), nameof(options));
        }

        _options = options;
        _connector = connector ?? new ClientWebSocketConnector();
        _clock = clock ?? SystemSessionClock.Instance;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _url = new Uri(WebSocketMessages.BuildUrl(options.BaseUrl, options.ApiKey));
        _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in options.Headers ?? new Dictionary<string, string>())
        {
            _headers[header.Key] = header.Value;
        }

        _headers[DrishtiConstants.ApiKeyHeader] = options.ApiKey;
    }

    /// <inheritdoc />
    public bool IsConnected => Volatile.Read(ref _connection) is not null;

    /// <inheritdoc />
    public DateTimeOffset? LastMessageReceivedAt => FromTicks(Interlocked.Read(ref _lastMessageTicks));

    /// <inheritdoc />
    public DateTimeOffset? LastHeartbeatReceivedAt => FromTicks(Interlocked.Read(ref _lastHeartbeatTicks));

    /// <inheritdoc />
    public IReadOnlyList<SubscribeOptions> Subscriptions
    {
        get
        {
            lock (_gate)
            {
                return _subscriptionOrder.Select(product => _subscriptions[product]).ToList();
            }
        }
    }

    // --- Lifecycle ------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_runTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            _lifetime?.Dispose();
            _lifetime = new CancellationTokenSource();
            _dispatch = Channel.CreateUnbounded<WebSocketEvent>(new UnboundedChannelOptions { SingleReader = true });
            var token = _lifetime.Token;
            var dispatch = _dispatch;
            _dispatchTask = Task.Run(() => DispatchLoopAsync(dispatch.Reader), CancellationToken.None);
            _runTask = Task.Run(() => RunLoopAsync(token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await StartAsync(CancellationToken.None).ConfigureAwait(false);
        Task runTask;
        lock (_gate)
        {
            runTask = _runTask!;
        }

        using (cancellationToken.Register(() => _ = StopAsync()))
        {
            await runTask.ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        Task? runTask;
        Task? dispatchTask;
        IWebSocketConnection? connection;
        lock (_gate)
        {
            runTask = _runTask;
            dispatchTask = _dispatchTask;
            connection = _connection;
            _lifetime?.Cancel();
        }

        FailPending(new DrishtiWebSocketException(Messages.Get("WebSocketClosed")));
        if (connection is not null)
        {
            using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await connection.CloseAsync(closeTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or System.Net.WebSockets.WebSocketException or ObjectDisposedException)
            {
                // Best effort: the connection is torn down below either way.
            }
        }

        if (runTask is not null)
        {
            await runTask.ConfigureAwait(false);
        }

        lock (_gate)
        {
            _dispatch?.Writer.TryComplete();
            foreach (var reader in _readers)
            {
                reader.Writer.TryComplete();
            }
        }

        if (dispatchTask is not null)
        {
            await dispatchTask.ConfigureAwait(false);
        }
    }

    /// <summary>Stops the session and releases its resources.</summary>
    /// <returns>A task that completes when disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _lifetime?.Dispose();
            _lifetime = null;
        }

        _subscribeLock.Dispose();
    }

    // --- Subscriptions --------------------------------------------------------------------------

    /// <inheritdoc />
    public Task SubscribeAsync(string product, IEnumerable<string>? symbols = null, CancellationToken cancellationToken = default)
        => SubscribeAsync(new SubscribeOptions(product) { Symbols = symbols?.ToList() }, cancellationToken);

    /// <inheritdoc />
    public async Task SubscribeAsync(SubscribeOptions options, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(options, nameof(options));
        var product = WebSocketMessages.NormalizeProduct(Guard.NotBlank(options.Product, nameof(options.Product)).Trim());
        if (!DrishtiConstants.WebSocketProducts.Contains(product))
        {
            throw new ArgumentException(
                Messages.Format("UnknownWebSocketProduct", product, string.Join(", ", DrishtiConstants.WebSocketProducts)),
                nameof(options));
        }

        var categories = WebSocketMessages.NormalizeCategories(options.Categories);
        var normalized = options with
        {
            Product = product,
            Symbols = WebSocketMessages.NormalizeSymbols(options.Symbols),
            Categories = categories.Count > 0 ? categories : null,
        };

        lock (_gate)
        {
            if (!_subscriptions.ContainsKey(product))
            {
                _subscriptionOrder.Add(product);
            }

            _subscriptions[product] = normalized;
        }

        if (!IsConnected)
        {
            return;
        }

        await _subscribeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SendSubscribeWithRetryAsync(normalized, "subscribe", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _subscribeLock.Release();
        }
    }

    // --- Events ---------------------------------------------------------------------------------

    /// <inheritdoc />
    public async IAsyncEnumerable<WebSocketEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<WebSocketEvent>(new UnboundedChannelOptions { SingleReader = true });
        lock (_gate)
        {
            _readers.Add(channel);
        }

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _readers.Remove(channel);
            }
        }
    }

    /// <inheritdoc />
    public IDrishtiWebSocketSession On(string channel, Func<JsonElement, Task> handler)
    {
        var name = WebSocketMessages.NormalizeProduct(Guard.NotBlank(channel, nameof(channel)));
        Guard.NotNull(handler, nameof(handler));
        lock (_gate)
        {
            if (!_channelHandlers.TryGetValue(name, out var list))
            {
                _channelHandlers[name] = list = [];
            }

            list.Add(handler);
        }

        return this;
    }

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnNews(Func<NewsItem, Task> handler) => OnTyped("news", handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnNews(Action<NewsItem> handler) => OnTyped("news", Sync(handler));

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnBlockDeals(Func<BlockDealItem, Task> handler) => OnTyped("block_deals", handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnBlockDeals(Action<BlockDealItem> handler) => OnTyped("block_deals", Sync(handler));

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnAnnouncements(Func<Announcement, Task> handler) => OnTyped("announcements", handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnAnnouncements(Action<Announcement> handler) => OnTyped("announcements", Sync(handler));

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnEarnings(Func<Earnings, Task> handler) => OnTyped("earnings", handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnEarnings(Action<Earnings> handler) => OnTyped("earnings", Sync(handler));

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnConcalls(Func<Concall, Task> handler) => OnTyped("concalls", handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnConcalls(Action<Concall> handler) => OnTyped("concalls", Sync(handler));

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnAlerts(Func<Alert, Task> handler) => OnTyped("alerts", handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnAlerts(Action<Alert> handler) => OnTyped("alerts", Sync(handler));

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnData(Func<DataEvent, Task> handler) => OnEvent("data", handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnSubscribed(Func<SubscribedEvent, Task> handler) => OnEvent("subscribed", handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnError(Func<ErrorEvent, Task> handler) => OnEvent("error", handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnHeartbeat(Func<HeartbeatEvent, Task> handler) => OnEvent("heartbeat", handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnRaw(Func<RawEvent, Task> handler) => OnEvent("raw", handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnMessage(Func<WebSocketEvent, Task> handler) => OnEvent(MessageHandlerKey, handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnOpen(Func<Task> handler) => AddHandler(_openHandlers, handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnClose(Func<string, Task> handler) => AddHandler(_closeHandlers, handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnReconnectAttempt(Func<int, TimeSpan, string, Task> handler) => AddHandler(_reconnectAttemptHandlers, handler);

    /// <inheritdoc />
    public IDrishtiWebSocketSession OnReconnectWarning(Func<int, string, Task> handler) => AddHandler(_reconnectWarningHandlers, handler);

    // --- Connection loop ------------------------------------------------------------------------

    private async Task RunLoopAsync(CancellationToken token)
    {
        var attempt = 0;
        var delay = _options.ReconnectInitialDelay;
        var reason = "initial";
        while (!token.IsCancellationRequested)
        {
            attempt++;
            if (attempt > 1)
            {
                await InvokeAllAsync(_reconnectAttemptHandlers, h => h(attempt - 1, delay, reason)).ConfigureAwait(false);
                try
                {
                    await _clock.Delay(WithJitter(delay), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            else if (reason != "initial")
            {
                _logger.LogInformation(LogMessages.WebSocketReconnecting, reason);
            }

            var opened = false;
            try
            {
                reason = await RunConnectionAsync(() => opened = true, token).ConfigureAwait(false);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                _logger.LogWarning(ex, LogMessages.WebSocketConnectFailed, ex.Message);
            }

            if (token.IsCancellationRequested)
            {
                break;
            }

            if (opened)
            {
                await InvokeAllAsync(_closeHandlers, h => h(reason)).ConfigureAwait(false);
                attempt = 0;
                delay = _options.ReconnectInitialDelay;
                continue;
            }

            if (delay >= _options.ReconnectMaxDelay && attempt >= _options.ReconnectWarnAfterAttempts)
            {
                _logger.LogWarning(LogMessages.WebSocketReconnectWarning, attempt, reason);
                await InvokeAllAsync(_reconnectWarningHandlers, h => h(attempt, reason)).ConfigureAwait(false);
                attempt = 0;
            }

            delay = Min(Multiply(delay, _options.ReconnectBackoffMultiplier), _options.ReconnectMaxDelay);
        }
    }

    /// <summary>Connects, re-subscribes and waits for the connection to end. Returns why it ended.</summary>
    private async Task<string> RunConnectionAsync(Action onOpened, CancellationToken token)
    {
        var connection = await _connector.ConnectAsync(_url, _headers, token).ConfigureAwait(false);
        using var connectionScope = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<string>? receiveTask = null;
        var reason = Messages.Get("WebSocketClosed");
        try
        {
            lock (_gate)
            {
                _connection = connection;
            }

            Touch(ref _lastMessageTicks);
            receiveTask = ReceiveLoopAsync(connection, connectionScope.Token);
            _logger.LogInformation(LogMessages.WebSocketConnected, Subscriptions.Count);

            await _subscribeLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                foreach (var subscription in Subscriptions)
                {
                    await SendSubscribeWithRetryAsync(subscription, "reconnect", token).ConfigureAwait(false);
                }
            }
            finally
            {
                _subscribeLock.Release();
            }

            await InvokeAllAsync(_openHandlers, h => h()).ConfigureAwait(false);
            onOpened();

            var watchdog = WatchdogAsync(connectionScope.Token);
            var finished = await Task.WhenAny(receiveTask, watchdog).ConfigureAwait(false);
            reason = await finished.ConfigureAwait(false);
            if (finished == watchdog)
            {
                _logger.LogWarning(LogMessages.WebSocketForcingReconnect, reason);
            }

            return reason;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_connection, connection))
                {
                    _connection = null;
                }
            }

            Interlocked.Exchange(ref _lastMessageTicks, 0);
            FailPending(new DrishtiWebSocketException(reason));
            connectionScope.Cancel();
            connection.Dispose();
            if (receiveTask is not null)
            {
                await receiveTask.ConfigureAwait(false);
            }
        }
    }

    private async Task<string> ReceiveLoopAsync(IWebSocketConnection connection, CancellationToken token)
    {
        await Task.Yield();
        try
        {
            while (true)
            {
                var raw = await connection.ReceiveAsync(token).ConfigureAwait(false);
                if (raw is null)
                {
                    var reason = connection.CloseReason;
                    FailPending(new DrishtiWebSocketException(reason));
                    return reason;
                }

                Touch(ref _lastMessageTicks);
                HandleIncoming(raw);
            }
        }
        catch (OperationCanceledException)
        {
            return Messages.Get("WebSocketClosed");
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or System.Net.WebSockets.WebSocketException)
        {
            var reason = Messages.Format("WebSocketConnectionLost", ex.Message);
            FailPending(new DrishtiWebSocketException(reason, null, ex));
            return reason;
        }
    }

    private async Task<string> WatchdogAsync(CancellationToken token)
    {
        if (_options.HeartbeatTimeout <= TimeSpan.Zero)
        {
            await WaitForCancellationAsync(token).ConfigureAwait(false);
            return Messages.Get("WebSocketClosed");
        }

        try
        {
            while (true)
            {
                await _clock.Delay(_options.HeartbeatCheckInterval, token).ConfigureAwait(false);
                if (LastMessageReceivedAt is not { } last)
                {
                    continue;
                }

                var silentFor = _clock.UtcNow - last;
                if (silentFor >= _options.HeartbeatTimeout)
                {
                    return Messages.Format("HeartbeatTimeout", (long)silentFor.TotalMilliseconds);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return Messages.Get("WebSocketClosed");
        }
    }

    private void HandleIncoming(string raw)
    {
        var message = WebSocketMessages.Parse(raw);
        if (message is HeartbeatEvent)
        {
            Touch(ref _lastHeartbeatTicks);
            Dispatch(message, toReaders: false);
            return;
        }

        var resolvedPending = ResolvePending(message);
        Dispatch(message, toReaders: !resolvedPending);
    }

    private void Dispatch(WebSocketEvent message, bool toReaders)
    {
        lock (_gate)
        {
            _dispatch?.Writer.TryWrite(message);
            if (toReaders)
            {
                foreach (var reader in _readers)
                {
                    reader.Writer.TryWrite(message);
                }
            }
        }
    }

    private async Task DispatchLoopAsync(ChannelReader<WebSocketEvent> reader)
    {
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var message))
            {
                List<Func<WebSocketEvent, Task>> handlers;
                List<Func<JsonElement, Task>> channelHandlers;
                lock (_gate)
                {
                    handlers = [.. Handlers(message.Kind), .. Handlers(MessageHandlerKey)];
                    channelHandlers = message is DataEvent data && _channelHandlers.TryGetValue(data.Channel, out var list) ? [.. list] : [];
                }

                foreach (var handler in handlers)
                {
                    await InvokeSafelyAsync(() => handler(message), message.Kind).ConfigureAwait(false);
                }

                if (message is DataEvent dataEvent)
                {
                    foreach (var handler in channelHandlers)
                    {
                        await InvokeSafelyAsync(() => handler(dataEvent.Data), dataEvent.Channel).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    // --- Subscribe acknowledgements -------------------------------------------------------------

    private async Task SendSubscribeWithRetryAsync(SubscribeOptions options, string reason, CancellationToken token)
    {
        var attempt = 0;
        var delay = _options.SubscribeRetryInitialDelay;
        while (true)
        {
            var connection = Volatile.Read(ref _connection)
                ?? throw new DrishtiWebSocketException(Messages.Get("WebSocketNotConnected"));
            attempt++;
            try
            {
                var ack = await SendSubscribeAndWaitForAckAsync(connection, options, token).ConfigureAwait(false);
                _logger.LogInformation(LogMessages.WebSocketSubscribed, options.Product, ack.Tier, ack.FullFeed, reason, attempt);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
            {
                var error = ex as DrishtiWebSocketException ?? new DrishtiWebSocketException(ex.Message, null, ex);
                _logger.LogWarning(LogMessages.WebSocketSubscribeFailed, options.Product, attempt, _options.SubscribeMaxAttempts, reason, error.Message, error.Code);
                if (!IsConnected || attempt >= _options.SubscribeMaxAttempts || IsPermanent(error))
                {
                    throw error;
                }

                await _clock.Delay(WithJitter(delay), token).ConfigureAwait(false);
                delay = Min(Multiply(delay, _options.SubscribeRetryBackoffMultiplier), _options.SubscribeRetryMaxDelay);
            }
        }
    }

    private async Task<SubscribedEvent> SendSubscribeAndWaitForAckAsync(IWebSocketConnection connection, SubscribeOptions options, CancellationToken token)
    {
        var pending = new PendingSubscribe(options.Product);
        lock (_gate)
        {
            _pending = pending;
        }

        try
        {
            await connection.SendAsync(WebSocketMessages.BuildSubscribeMessage(options).ToJsonString(), token).ConfigureAwait(false);
            using var timeoutScope = CancellationTokenSource.CreateLinkedTokenSource(token);
            var timeout = _clock.Delay(_options.SubscribeAckTimeout, timeoutScope.Token);
            var finished = await Task.WhenAny(pending.Completion.Task, timeout).ConfigureAwait(false);
            if (finished == pending.Completion.Task)
            {
                timeoutScope.Cancel();
                return await pending.Completion.Task.ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            throw new DrishtiWebSocketException(Messages.Format("SubscribeAckTimedOut", options.Product));
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pending, pending))
                {
                    _pending = null;
                }
            }
        }
    }

    private bool ResolvePending(WebSocketEvent message)
    {
        PendingSubscribe? pending;
        lock (_gate)
        {
            pending = _pending;
        }

        switch (message)
        {
            case SubscribedEvent subscribed when pending is not null && subscribed.Product == pending.Product:
                pending.Completion.TrySetResult(subscribed);
                return true;
            case ErrorEvent error when pending is not null:
                pending.Completion.TrySetException(new DrishtiWebSocketException(error.Message, error.Code));
                return true;
            default:
                return false;
        }
    }

    private void FailPending(DrishtiWebSocketException error)
    {
        PendingSubscribe? pending;
        lock (_gate)
        {
            pending = _pending;
            _pending = null;
        }

        pending?.Completion.TrySetException(error);
    }

    private static bool IsPermanent(DrishtiWebSocketException error)
        => error.Code is { } code && DrishtiConstants.PermanentSubscribeErrorCodes.Contains(code);

    // --- Helpers --------------------------------------------------------------------------------

    private IDrishtiWebSocketSession OnTyped<T>(string channel, Func<T, Task> handler)
        where T : DrishtiObject
    {
        Guard.NotNull(handler, nameof(handler));
        return On(channel, json => handler(DrishtiObject.From<T>(json)));
    }

    private IDrishtiWebSocketSession OnEvent<T>(string kind, Func<T, Task> handler)
        where T : WebSocketEvent
    {
        Guard.NotNull(handler, nameof(handler));
        lock (_gate)
        {
            if (!_eventHandlers.TryGetValue(kind, out var list))
            {
                _eventHandlers[kind] = list = [];
            }

            list.Add(message => message is T typed ? handler(typed) : Task.CompletedTask);
        }

        return this;
    }

    private IDrishtiWebSocketSession AddHandler<T>(List<T> handlers, T handler)
        where T : Delegate
    {
        Guard.NotNull(handler, nameof(handler));
        lock (_gate)
        {
            handlers.Add(handler);
        }

        return this;
    }

    private static Func<T, Task> Sync<T>(Action<T> handler)
    {
        Guard.NotNull(handler, nameof(handler));
        return value =>
        {
            handler(value);
            return Task.CompletedTask;
        };
    }

    private IEnumerable<Func<WebSocketEvent, Task>> Handlers(string key)
        => _eventHandlers.TryGetValue(key, out var list) ? list : [];

    private async Task InvokeAllAsync<T>(List<T> handlers, Func<T, Task> invoke)
        where T : Delegate
    {
        List<T> snapshot;
        lock (_gate)
        {
            snapshot = [.. handlers];
        }

        foreach (var handler in snapshot)
        {
            await InvokeSafelyAsync(() => invoke(handler), "lifecycle").ConfigureAwait(false);
        }
    }

    private async Task InvokeSafelyAsync(Func<Task> invoke, string source)
    {
        try
        {
            await invoke().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, LogMessages.WebSocketHandlerFailed, source);
        }
    }

    private TimeSpan WithJitter(TimeSpan delay)
    {
        var ratio = _options.ReconnectJitterRatio;
        if (ratio <= 0)
        {
            return delay;
        }

        var millis = (long)delay.TotalMilliseconds;
        var spread = (long)Math.Floor(millis * ratio);
        var offset = (long)Math.Floor(_clock.NextDouble() * ((spread * 2) + 1)) - spread;
        return TimeSpan.FromMilliseconds(Math.Max(0, millis + offset));
    }

    private static TimeSpan Multiply(TimeSpan value, double factor) => TimeSpan.FromMilliseconds(Math.Floor(value.TotalMilliseconds * factor));

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private void Touch(ref long field) => Interlocked.Exchange(ref field, _clock.UtcNow.UtcTicks);

    private static DateTimeOffset? FromTicks(long ticks) => ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);

    private static async Task WaitForCancellationAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (token.Register(() => completion.TrySetResult(true)))
        {
            await completion.Task.ConfigureAwait(false);
        }
    }

    private sealed class PendingSubscribe(string product)
    {
        public string Product { get; } = product;

        public TaskCompletionSource<SubscribedEvent> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
