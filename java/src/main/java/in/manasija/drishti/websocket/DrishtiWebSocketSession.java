package in.manasija.drishti.websocket;

import com.fasterxml.jackson.databind.node.ObjectNode;
import in.manasija.drishti.DrishtiClient;
import in.manasija.drishti.exception.DrishtiWebSocketException;
import in.manasija.drishti.internal.Json;
import in.manasija.drishti.websocket.event.DataEvent;
import in.manasija.drishti.websocket.event.ErrorEvent;
import in.manasija.drishti.websocket.event.HeartbeatEvent;
import in.manasija.drishti.websocket.event.RawEvent;
import in.manasija.drishti.websocket.event.SubscribedEvent;
import in.manasija.drishti.websocket.event.WebSocketEvent;
import java.lang.System.Logger.Level;
import java.time.Duration;
import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Objects;
import java.util.OptionalLong;
import java.util.Queue;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.concurrent.ThreadLocalRandom;
import java.util.function.Consumer;

/**
 * A managed connection to the Drishti websocket ({@code wss://developers.manasija.in/v1/ws}) that
 * pushes market events as they happen.
 *
 * <p>Register handlers and subscriptions, then call {@link #run()}. The session connects, sends
 * every subscription and dispatches events to handlers. It reconnects with exponential backoff
 * when:
 *
 * <ul>
 *   <li>the connection drops;</li>
 *   <li>a subscribe request fails;</li>
 *   <li>no message arrives within the heartbeat timeout.</li>
 * </ul>
 *
 * <p>Subscriptions are kept across reconnects, including ones the server rejected, and sent again
 * on every new connection. Call {@link #stop()}, from a handler or another thread, to end the
 * session.
 *
 * <pre>{@code
 * DrishtiClient client = DrishtiClient.fromEnvironment();
 * DrishtiWebSocketSession ws = client.websocket()
 *         .onAnnouncements(a -> System.out.println(a.path("symbol").asText() + " " + a.path("summary").asText()))
 *         .onError(e -> System.err.println("error: " + e.message()));
 * ws.subscribe("announcements", List.of("RELIANCE", "TCS"));
 * ws.run(); // blocks until stop() is called
 * }</pre>
 *
 * <p>To keep the calling thread free, for example in a web application, run the session in the
 * background and close it on shutdown:
 *
 * <pre>{@code
 * DrishtiWebSocketSession ws = client.websocket().onNews(this::handleNews);
 * ws.subscribe("news", List.of("INFY"));
 * ws.start();   // runs on a daemon thread
 * // ...
 * ws.close();   // stops and waits for the thread
 * }</pre>
 *
 * <h2>Threading</h2>
 * Handlers run on the session thread, one event at a time; keep them fast and hand heavy work to
 * an executor. {@link #subscribe(String, Collection, Collection, boolean)}, {@link #stop()} and the
 * handler registration methods are thread-safe.
 *
 * <h2>Plans and limits</h2>
 * Websocket streams need a paid plan:
 *
 * <ul>
 *   <li>Starter streams up to 100 symbols account-wide, and Pro up to 1,000;</li>
 *   <li>only Scale may subscribe with an empty symbol list to receive the whole market;</li>
 *   <li>delivered events do not use REST credits.</li>
 * </ul>
 *
 * @see <a href="https://drishti.manasija.in/docs/guides/websockets">Websocket streams reference</a>
 * @since 1.0.0
 */
public final class DrishtiWebSocketSession implements AutoCloseable {

    /** Kind name that receives every event, whatever its kind. */
    public static final String MESSAGE = "message";

    private static final List<String> EVENT_KINDS = List.of(
            WebSocketEvent.SUBSCRIBED, WebSocketEvent.DATA, WebSocketEvent.ERROR,
            WebSocketEvent.HEARTBEAT, WebSocketEvent.RAW, MESSAGE);
    private static final double POLL_INTERVAL_SECONDS = 1.0;
    private static final String LOG_PREFIX = "[drishti-sdk] ";

    private final String apiKey;
    private final String baseUrl;
    private final Map<String, String> headers;
    private final WebSocketConnector connector;
    private final System.Logger logger;
    private final boolean lifecycleLogging;
    private final SessionClock clock;
    private final double reconnectInitialDelay;
    private final double reconnectMaxDelay;
    private final double reconnectBackoffMultiplier;
    private final double reconnectJitterRatio;
    private final int reconnectWarnAfterAttempts;
    private final double subscribeAckTimeout;
    private final int subscribeMaxAttempts;
    private final double subscribeRetryInitialDelay;
    private final double subscribeRetryMaxDelay;
    private final double subscribeRetryBackoffMultiplier;
    private final double heartbeatTimeout;

    private final Map<String, List<Consumer<?>>> handlers = new ConcurrentHashMap<>();
    private final Map<String, Subscription> subscriptions = Collections.synchronizedMap(new LinkedHashMap<>());
    private final Queue<Subscription> queuedSubscriptions = new ConcurrentLinkedQueue<>();

    private volatile Consumer<String> onOpen;
    private volatile Consumer<String> onClose;
    private volatile ReconnectListener onReconnectAttempt;
    private volatile ReconnectWarningListener onReconnectWarning;

    private volatile WebSocketConnection connection;
    private volatile boolean stopped;
    private volatile Thread loopThread;
    private volatile Thread backgroundThread;
    private volatile long lastHeartbeatNanos = Long.MIN_VALUE;
    private Double deadline;
    private double lastMessageAt;
    private PendingSubscribe pendingSubscribe;

    private DrishtiWebSocketSession(Builder builder) {
        if (builder.apiKey == null || builder.apiKey.isBlank()) {
            throw new IllegalArgumentException("DrishtiWebSocketSession requires a non-empty apiKey");
        }
        this.apiKey = builder.apiKey;
        String base = builder.baseUrl.strip();
        while (base.endsWith("/")) {
            base = base.substring(0, base.length() - 1);
        }
        this.baseUrl = base;
        Map<String, String> handshake = new LinkedHashMap<>();
        builder.headers.forEach((name, value) -> {
            if (!"X-API-Key".equalsIgnoreCase(name)) {
                handshake.put(name, value);
            }
        });
        this.headers = Collections.unmodifiableMap(handshake);
        this.connector = builder.connector != null ? builder.connector : new JdkWebSocketConnector();
        this.logger = builder.logger != null ? builder.logger : System.getLogger(DrishtiWebSocketSession.class.getName());
        this.lifecycleLogging = builder.lifecycleLogging;
        this.clock = builder.clock != null ? builder.clock : SessionClock.system();
        this.reconnectInitialDelay = Math.max(0.1, seconds(builder.reconnectInitialDelay));
        this.reconnectMaxDelay = Math.max(reconnectInitialDelay, seconds(builder.reconnectMaxDelay));
        this.reconnectBackoffMultiplier = Math.max(1.0, builder.reconnectBackoffMultiplier);
        this.reconnectJitterRatio = Math.min(1.0, Math.max(0.0, builder.reconnectJitterRatio));
        this.reconnectWarnAfterAttempts = Math.max(1, builder.reconnectWarnAfterAttempts);
        this.subscribeAckTimeout = Math.max(1.0, seconds(builder.subscribeAckTimeout));
        this.subscribeMaxAttempts = Math.max(1, builder.subscribeMaxAttempts);
        this.subscribeRetryInitialDelay = Math.max(0.1, seconds(builder.subscribeRetryInitialDelay));
        this.subscribeRetryMaxDelay = Math.max(subscribeRetryInitialDelay, seconds(builder.subscribeRetryMaxDelay));
        this.subscribeRetryBackoffMultiplier = Math.max(1.0, builder.subscribeRetryBackoffMultiplier);
        this.heartbeatTimeout = Math.max(0.0, seconds(builder.heartbeatTimeout));
    }

    /**
     * Starts building a session. Most applications use {@link DrishtiClient#websocket()} or
     * {@link DrishtiClient#websocketBuilder()} instead, which copy the client's settings.
     *
     * @return a new builder
     */
    public static Builder builder() {
        return new Builder();
    }

    // --- Handlers ----------------------------------------------------------------------------

    /**
     * Registers a handler for one product channel's data payloads.
     *
     * <pre>{@code
     * ws.on("announcements", data -> System.out.println(data.path("summary").asText()));
     * }</pre>
     *
     * @param channel a product name from {@link Products#ALL}
     * @param handler receives each delivered payload
     * @return this session, for chaining
     * @throws IllegalArgumentException when {@code channel} is not a known product
     */
    public DrishtiWebSocketSession on(String channel, Consumer<? super ObjectNode> handler) {
        if (!Products.isKnown(channel)) {
            throw new IllegalArgumentException("Unknown WebSocket channel \"" + channel + "\"; expected one of: "
                    + String.join(", ", Products.ALL) + ". Use onSubscribed/onData/onError/onHeartbeat/onRaw/onMessage for "
                    + String.join(", ", EVENT_KINDS) + ".");
        }
        return addHandler(channel, handler);
    }

    /**
     * Registers a handler for news payloads.
     *
     * @param handler receives each news item
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onNews(Consumer<? super ObjectNode> handler) {
        return on(Products.NEWS, handler);
    }

    /**
     * Registers a handler for block-deal payloads.
     *
     * @param handler receives each block deal
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onBlockDeals(Consumer<? super ObjectNode> handler) {
        return on(Products.BLOCK_DEALS, handler);
    }

    /**
     * Registers a handler for announcement payloads.
     *
     * @param handler receives each announcement
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onAnnouncements(Consumer<? super ObjectNode> handler) {
        return on(Products.ANNOUNCEMENTS, handler);
    }

    /**
     * Registers a handler for earnings payloads.
     *
     * @param handler receives each earnings filing
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onEarnings(Consumer<? super ObjectNode> handler) {
        return on(Products.EARNINGS, handler);
    }

    /**
     * Registers a handler for conference-call payloads.
     *
     * @param handler receives each conference-call update
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onConcalls(Consumer<? super ObjectNode> handler) {
        return on(Products.CONCALLS, handler);
    }

    /**
     * Registers a handler for market-alert payloads.
     *
     * @param handler receives each alert
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onAlerts(Consumer<? super ObjectNode> handler) {
        return on(Products.ALERTS, handler);
    }

    /**
     * Registers a handler for subscribe acknowledgements.
     *
     * @param handler receives each acknowledgement, including those sent after a reconnect
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onSubscribed(Consumer<? super SubscribedEvent> handler) {
        return addHandler(WebSocketEvent.SUBSCRIBED, handler);
    }

    /**
     * Registers a handler for every data delivery, whatever the channel.
     *
     * @param handler receives each data event
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onData(Consumer<? super DataEvent> handler) {
        return addHandler(WebSocketEvent.DATA, handler);
    }

    /**
     * Registers a handler for server errors and frames that could not be parsed.
     *
     * @param handler receives each error event
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onError(Consumer<? super ErrorEvent> handler) {
        return addHandler(WebSocketEvent.ERROR, handler);
    }

    /**
     * Registers a handler for server heartbeats.
     *
     * @param handler receives each heartbeat
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onHeartbeat(Consumer<? super HeartbeatEvent> handler) {
        return addHandler(WebSocketEvent.HEARTBEAT, handler);
    }

    /**
     * Registers a handler for messages the SDK does not recognise.
     *
     * @param handler receives each raw event
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onRaw(Consumer<? super RawEvent> handler) {
        return addHandler(WebSocketEvent.RAW, handler);
    }

    /**
     * Registers a handler for every event, after the kind-specific handlers.
     *
     * @param handler receives every event
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onMessage(Consumer<? super WebSocketEvent> handler) {
        return addHandler(MESSAGE, handler);
    }

    /**
     * Removes a handler registered with any {@code on...} method. It is safe to call from inside
     * the handler itself.
     *
     * @param handler the handler instance that was registered
     * @return {@code true} when the handler was found and removed
     */
    public boolean off(Consumer<?> handler) {
        boolean removed = false;
        for (List<Consumer<?>> list : handlers.values()) {
            removed |= list.removeIf(candidate -> candidate == handler);
        }
        return removed;
    }

    /**
     * Sets the callback that runs after each successful connection, once subscriptions are sent.
     *
     * @param callback receives {@code "connected"}, or {@code null} to remove the callback
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onOpen(Consumer<String> callback) {
        this.onOpen = callback;
        return this;
    }

    /**
     * Sets the callback that runs when an established connection ends. It is not called when the
     * session itself is stopped.
     *
     * @param callback receives the close reason, such as {@code heartbeat timeout}, or
     *     {@code null} to remove the callback
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onClose(Consumer<String> callback) {
        this.onClose = callback;
        return this;
    }

    /**
     * Sets the callback for reconnect attempts. Without one, each attempt is logged.
     *
     * @param listener the listener, or {@code null} to remove it
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onReconnectAttempt(ReconnectListener listener) {
        this.onReconnectAttempt = listener;
        return this;
    }

    /**
     * Sets the callback for repeated connection failures. Without one, a warning is logged.
     *
     * @param listener the listener, or {@code null} to remove it
     * @return this session, for chaining
     */
    public DrishtiWebSocketSession onReconnectWarning(ReconnectWarningListener listener) {
        this.onReconnectWarning = listener;
        return this;
    }

    // --- Subscriptions and lifecycle -----------------------------------------------------------

    /**
     * Subscribes to the full feed of a product, which needs the Scale plan.
     *
     * @param product a product name from {@link Products}
     * @return this session, for chaining
     * @throws DrishtiWebSocketException when called from a handler while connected and the server
     *     rejects the subscription
     * @see #subscribe(String, Collection, Collection, boolean)
     */
    public DrishtiWebSocketSession subscribe(String product) {
        return subscribe(product, List.of(), List.of(), true);
    }

    /**
     * Subscribes to a product for a watchlist, with the detailed payload shape.
     *
     * @param product a product name from {@link Products}
     * @param symbols the symbols to watch
     * @return this session, for chaining
     * @throws DrishtiWebSocketException when called from a handler while connected and the server
     *     rejects the subscription
     * @see #subscribe(String, Collection, Collection, boolean)
     */
    public DrishtiWebSocketSession subscribe(String product, Collection<String> symbols) {
        return subscribe(product, symbols, List.of(), true);
    }

    /**
     * Subscribes to a product. A second subscription to the same product replaces the first.
     *
     * <p>When this is sent depends on the session state:
     *
     * <ul>
     *   <li>before {@link #run()}, the subscription is only recorded, and sent after connecting;</li>
     *   <li>while connected, from a handler or {@link #onOpen(Consumer)}, it is sent at once and a
     *       rejection throws;</li>
     *   <li>while connected, from any other thread, it is queued and sent by the session thread,
     *       and a rejection arrives as an {@link ErrorEvent}.</li>
     * </ul>
     *
     * <p>Subscriptions are sent again after every reconnect.
     *
     * <pre>{@code
     * ws.subscribe("announcements", List.of("RELIANCE"), List.of("Board Meeting", "Dividend"), false);
     * }</pre>
     *
     * @param product a product name from {@link Products}
     * @param symbols the symbols to watch; empty for the full feed (Scale plan only)
     * @param categories announcement categories to filter on; ignored by other products
     * @param detailed {@code true} for the detail payload shape, {@code false} for the summary
     * @return this session, for chaining
     * @throws DrishtiWebSocketException when called from a handler while connected and the server
     *     rejects the subscription
     */
    public DrishtiWebSocketSession subscribe(
            String product, Collection<String> symbols, Collection<String> categories, boolean detailed) {
        Objects.requireNonNull(product, "product");
        Subscription subscription = new Subscription(
                product,
                WebSocketMessages.normalizeSymbols(symbols),
                WebSocketMessages.normalizeCategories(categories),
                detailed);
        subscriptions.put(product, subscription);
        if (connection != null) {
            if (Thread.currentThread() == loopThread) {
                sendSubscribeWithRetry(subscription, "subscribe");
            } else {
                queuedSubscriptions.add(subscription);
            }
        }
        return this;
    }

    /**
     * Connects and processes events until {@link #stop()} is called. It reconnects
     * automatically and never throws for connection problems.
     *
     * @throws IllegalStateException when the session is already running
     */
    public void run() {
        run(null);
    }

    /**
     * Connects and processes events until {@link #stop()} is called or the duration elapses.
     *
     * <p>If the calling thread is interrupted, the session stops and the interrupt flag stays set.
     *
     * @param duration the longest time to run, or {@code null} to run until stopped
     * @throws IllegalStateException when the session is already running
     */
    public void run(Duration duration) {
        stopped = false;
        runLoop(duration);
    }

    private void runLoop(Duration duration) {
        synchronized (this) {
            if (loopThread != null) {
                throw new IllegalStateException("DrishtiWebSocketSession is already running");
            }
            loopThread = Thread.currentThread();
        }
        deadline = duration == null ? null : now() + Math.max(0.0, seconds(duration));
        int attempt = 0;
        double delay = reconnectInitialDelay;
        String reason = "initial";
        try {
            while (!shouldStop()) {
                attempt++;
                if (attempt > 1) {
                    emitReconnectAttempt(attempt - 1, delay, reason);
                    sleepFor(jitter(delay));
                    if (shouldStop()) {
                        break;
                    }
                } else if (!reason.equals("initial")) {
                    logLifecycle("reconnecting (" + reason + ")");
                }
                try {
                    Map<String, String> handshake = new LinkedHashMap<>(headers);
                    handshake.put("X-API-Key", apiKey);
                    connection = connector.connect(WebSocketMessages.buildWebSocketUrl(baseUrl), handshake);
                    lastMessageAt = now();
                    // Clear first: anything queued after this point is also in the snapshot or re-queued.
                    queuedSubscriptions.clear();
                    List<Subscription> current = snapshotSubscriptions();
                    logLifecycle("connected; resubscribing " + current.size() + " product(s)");
                    for (Subscription subscription : current) {
                        sendSubscribeWithRetry(subscription, "reconnect");
                    }
                    Consumer<String> open = onOpen;
                    if (open != null) {
                        open.accept("connected");
                    }
                    attempt = 0;
                    delay = reconnectInitialDelay;
                    String closeReason = receiveUntilClosed();
                    closeConnection();
                    Consumer<String> close = onClose;
                    if (closeReason != null && close != null) {
                        close.accept(closeReason);
                    }
                    reason = "connection closed";
                } catch (InterruptedException e) {
                    throw e;
                } catch (Exception e) {
                    closeConnection();
                    logLifecycle("connection setup failed: " + e.getMessage());
                    if (delay >= reconnectMaxDelay && attempt >= reconnectWarnAfterAttempts) {
                        emitReconnectWarning(attempt, reason, e);
                        attempt = 0;
                    }
                    delay = Math.min(delay * reconnectBackoffMultiplier, reconnectMaxDelay);
                }
            }
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            stopped = true;
        } finally {
            closeConnection();
            pendingSubscribe = null;
            loopThread = null;
        }
    }

    /**
     * Starts {@link #run()} on a new daemon thread named {@code drishti-websocket} and returns at
     * once.
     *
     * @return the thread running the session
     * @throws IllegalStateException when the session is already running
     */
    public synchronized Thread start() {
        if (loopThread != null || (backgroundThread != null && backgroundThread.isAlive())) {
            throw new IllegalStateException("DrishtiWebSocketSession is already running");
        }
        // Reset here, not in the new thread, so a close() right after start() is not lost.
        stopped = false;
        Thread thread = new Thread(() -> runLoop(null), "drishti-websocket");
        thread.setDaemon(true);
        backgroundThread = thread;
        thread.start();
        return thread;
    }

    /**
     * Asks the session to stop after the current step and close the socket. It is safe to call
     * from handlers and from other threads, and returns at once.
     */
    public void stop() {
        stopped = true;
    }

    /**
     * Stops the session and, when it was started with {@link #start()}, waits up to five seconds
     * for the background thread to finish.
     */
    @Override
    public void close() {
        stop();
        Thread thread = backgroundThread;
        if (thread != null && thread != Thread.currentThread()) {
            try {
                thread.join(5_000);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
            }
        }
    }

    /**
     * Reports whether a connection is open right now.
     *
     * @return {@code true} while connected
     */
    public boolean isConnected() {
        return connection != null;
    }

    /**
     * Returns the time of the last server heartbeat, as read from this session's
     * {@link SessionClock}.
     *
     * @return the {@link SessionClock#nanoTime()} reading of the last heartbeat, or empty when
     *     none has arrived yet
     */
    public OptionalLong lastHeartbeatNanoTime() {
        long value = lastHeartbeatNanos;
        return value == Long.MIN_VALUE ? OptionalLong.empty() : OptionalLong.of(value);
    }

    /**
     * Returns the recorded subscriptions, as they are sent after each reconnect.
     *
     * @return the subscribe messages by product, in subscription order
     */
    public Map<String, ObjectNode> subscriptions() {
        Map<String, ObjectNode> out = new LinkedHashMap<>();
        for (Subscription subscription : snapshotSubscriptions()) {
            out.put(subscription.product(), subscription.toMessage());
        }
        return Collections.unmodifiableMap(out);
    }

    // --- Internals -----------------------------------------------------------------------------

    private DrishtiWebSocketSession addHandler(String key, Consumer<?> handler) {
        Objects.requireNonNull(handler, "handler");
        handlers.computeIfAbsent(key, k -> new CopyOnWriteArrayList<>()).add(handler);
        return this;
    }

    private List<Subscription> snapshotSubscriptions() {
        synchronized (subscriptions) {
            return new ArrayList<>(subscriptions.values());
        }
    }

    /** Returns the close reason, or {@code null} when the session was stopped locally. */
    private String receiveUntilClosed() throws InterruptedException {
        while (!shouldStop()) {
            WebSocketConnection current = connection;
            if (current == null) {
                return "closed";
            }
            Subscription queued;
            while ((queued = queuedSubscriptions.poll()) != null) {
                try {
                    sendSubscribeWithRetry(queued, "subscribe");
                } catch (DrishtiWebSocketException e) {
                    if (connection == null) {
                        return "closed: " + e.getMessage();
                    }
                }
            }
            String raw;
            try {
                raw = current.receive(pollTimeout());
            } catch (DrishtiWebSocketException e) {
                return "closed: " + e.getMessage();
            }
            if (raw == null) {
                if (heartbeatTimeout > 0 && now() - lastMessageAt >= heartbeatTimeout) {
                    logLifecycle(String.format(Locale.ROOT, "no message for %.0fs; reconnecting", heartbeatTimeout));
                    return "heartbeat timeout";
                }
                continue;
            }
            handleRaw(raw);
        }
        return null;
    }

    private void handleRaw(String raw) {
        lastMessageAt = now();
        WebSocketEvent event = WebSocketMessages.parse(raw);
        if (event instanceof HeartbeatEvent) {
            lastHeartbeatNanos = clock.nanoTime();
        } else if (pendingSubscribe != null) {
            if (event instanceof SubscribedEvent ack && ack.product().equals(pendingSubscribe.product)) {
                pendingSubscribe.result = ack;
            } else if (event instanceof ErrorEvent error) {
                pendingSubscribe.result = error;
            }
        }
        dispatch(event);
    }

    private void dispatch(WebSocketEvent event) {
        notify(event.kind(), event);
        notify(MESSAGE, event);
        if (event instanceof DataEvent data && Products.isKnown(data.channel())) {
            notify(data.channel(), data.data());
        }
    }

    @SuppressWarnings("unchecked")
    private void notify(String key, Object payload) {
        List<Consumer<?>> list = handlers.get(key);
        if (list == null) {
            return;
        }
        for (Consumer<?> handler : list) {
            try {
                ((Consumer<Object>) handler).accept(payload);
            } catch (DrishtiWebSocketException e) {
                // Raised by subscribe() inside a handler; it has already been logged.
            } catch (RuntimeException e) {
                logger.log(Level.WARNING, LOG_PREFIX + "handler for '" + key + "' threw an exception", e);
            }
        }
    }

    private void sendSubscribeWithRetry(Subscription subscription, String reason) {
        int attempt = 0;
        double delay = subscribeRetryInitialDelay;
        while (attempt < subscribeMaxAttempts) {
            if (connection == null) {
                throw new DrishtiWebSocketException("WebSocket is not connected");
            }
            attempt++;
            try {
                SubscribedEvent ack = sendSubscribeAndWaitForAck(subscription);
                logLifecycle(String.format(Locale.ROOT, "subscribed %s tier=%s full_feed=%s (%s, attempt %d)",
                        subscription.product(), ack.tier(), ack.fullFeed(), reason, attempt));
                return;
            } catch (DrishtiWebSocketException e) {
                String code = e.getErrorCode();
                logLifecycle(String.format(Locale.ROOT, "subscribe failed product=%s attempt=%d/%d (%s): %s%s",
                        subscription.product(), attempt, subscribeMaxAttempts, reason, e.getMessage(),
                        code != null ? " [" + code + "]" : ""));
                boolean permanent = code != null && Products.PERMANENT_SUBSCRIBE_ERROR_CODES.contains(code);
                if (connection == null || attempt >= subscribeMaxAttempts || permanent) {
                    throw e;
                }
                try {
                    sleepFor(jitter(delay));
                } catch (InterruptedException interrupted) {
                    Thread.currentThread().interrupt();
                    stopped = true;
                    throw new DrishtiWebSocketException("Interrupted while retrying subscribe", code, interrupted);
                }
                delay = Math.min(delay * subscribeRetryBackoffMultiplier, subscribeRetryMaxDelay);
            }
        }
    }

    private SubscribedEvent sendSubscribeAndWaitForAck(Subscription subscription) {
        WebSocketConnection current = connection;
        if (current == null) {
            throw new DrishtiWebSocketException("WebSocket is not connected");
        }
        // A handler may subscribe while an outer subscribe waits; restore the outer wait afterwards.
        PendingSubscribe previous = pendingSubscribe;
        PendingSubscribe pending = new PendingSubscribe(subscription.product());
        pendingSubscribe = pending;
        try {
            current.send(Json.write(subscription.toMessage()));
            double ackDeadline = now() + subscribeAckTimeout;
            WebSocketEvent result;
            while ((result = pending.result) == null) {
                double remaining = ackDeadline - now();
                if (remaining <= 0) {
                    throw new DrishtiWebSocketException("Subscribe acknowledgement timed out");
                }
                String raw;
                try {
                    raw = current.receive(duration(Math.min(remaining, POLL_INTERVAL_SECONDS)));
                } catch (DrishtiWebSocketException e) {
                    closeConnection();
                    throw new DrishtiWebSocketException("WebSocket connection closed", null, e);
                } catch (InterruptedException e) {
                    Thread.currentThread().interrupt();
                    stopped = true;
                    throw new DrishtiWebSocketException("Interrupted while waiting for subscribe acknowledgement", null, e);
                }
                if (raw != null) {
                    handleRaw(raw);
                }
            }
            if (result instanceof ErrorEvent error) {
                throw new DrishtiWebSocketException(error.message(), error.code());
            }
            return (SubscribedEvent) result;
        } finally {
            pendingSubscribe = previous;
        }
    }

    private void emitReconnectAttempt(int attempt, double delay, String reason) {
        ReconnectListener listener = onReconnectAttempt;
        if (listener == null) {
            logLifecycle(String.format(Locale.ROOT, "reconnect attempt=%d delay=%.1fs reason=%s", attempt, delay, reason));
            return;
        }
        listener.onReconnectAttempt(attempt, duration(delay), reason);
    }

    private void emitReconnectWarning(int attempt, String reason, Exception e) {
        ReconnectWarningListener listener = onReconnectWarning;
        if (listener != null) {
            listener.onReconnectWarning(attempt, reason);
            return;
        }
        logger.log(Level.WARNING, String.format(Locale.ROOT,
                "Drishti WebSocket still unable to connect after %d attempts (%s): %s", attempt, reason, e.getMessage()));
    }

    private void closeConnection() {
        WebSocketConnection current = connection;
        connection = null;
        if (current != null) {
            try {
                current.close();
            } catch (RuntimeException ignored) {
                // Closing is best effort.
            }
        }
    }

    private void logLifecycle(String message) {
        if (lifecycleLogging) {
            logger.log(Level.INFO, LOG_PREFIX + message);
        }
    }

    private double jitter(double delay) {
        if (reconnectJitterRatio <= 0) {
            return delay;
        }
        double spread = delay * reconnectJitterRatio;
        return Math.max(0.0, delay + (ThreadLocalRandom.current().nextDouble() * 2 - 1) * spread);
    }

    private void sleepFor(double seconds) throws InterruptedException {
        double bounded = seconds;
        if (deadline != null) {
            bounded = Math.min(bounded, Math.max(0.0, deadline - now()));
        }
        clock.sleep(duration(bounded));
    }

    private Duration pollTimeout() {
        double timeout = POLL_INTERVAL_SECONDS;
        if (deadline != null) {
            timeout = Math.min(timeout, Math.max(0.0, deadline - now()));
        }
        return duration(timeout);
    }

    private boolean shouldStop() {
        return stopped || (deadline != null && now() >= deadline);
    }

    private double now() {
        return clock.nanoTime() / 1e9;
    }

    private static double seconds(Duration duration) {
        return duration.toNanos() / 1e9;
    }

    private static Duration duration(double seconds) {
        return Duration.ofNanos(Math.round(seconds * 1e9));
    }

    /** One product subscription, already normalised. */
    private record Subscription(String product, List<String> symbols, List<String> categories, boolean detailed) {
        ObjectNode toMessage() {
            return WebSocketMessages.buildSubscribeMessage(product, symbols, categories, detailed);
        }
    }

    /** State of a subscribe request waiting for its acknowledgement. */
    private static final class PendingSubscribe {
        private final String product;
        private WebSocketEvent result;

        PendingSubscribe(String product) {
            this.product = product;
        }
    }

    /**
     * Builder for {@link DrishtiWebSocketSession}.
     *
     * <p>Only the API key is required; the defaults match the other Drishti SDKs. Builders are
     * not thread-safe.
     *
     * @since 1.0.0
     */
    public static final class Builder {

        private String apiKey;
        private String baseUrl = DrishtiClient.DEFAULT_BASE_URL;
        private final Map<String, String> headers = new LinkedHashMap<>();
        private WebSocketConnector connector;
        private System.Logger logger;
        private boolean lifecycleLogging = true;
        private SessionClock clock;
        private Duration reconnectInitialDelay = Duration.ofSeconds(1);
        private Duration reconnectMaxDelay = Duration.ofSeconds(30);
        private double reconnectBackoffMultiplier = 2.0;
        private double reconnectJitterRatio = 0.2;
        private int reconnectWarnAfterAttempts = 10;
        private Duration subscribeAckTimeout = Duration.ofSeconds(10);
        private int subscribeMaxAttempts = 10;
        private Duration subscribeRetryInitialDelay = Duration.ofSeconds(1);
        private Duration subscribeRetryMaxDelay = Duration.ofSeconds(30);
        private double subscribeRetryBackoffMultiplier = 2.0;
        private Duration heartbeatTimeout = Duration.ofSeconds(90);

        private Builder() {
        }

        /**
         * Sets the API key sent as the {@code X-API-Key} handshake header.
         *
         * @param apiKey the Drishti API key
         * @return this builder
         */
        public Builder apiKey(String apiKey) {
            this.apiKey = apiKey;
            return this;
        }

        /**
         * Sets the HTTP base URL; the websocket URL is derived from it.
         *
         * @param baseUrl the base URL; defaults to {@value DrishtiClient#DEFAULT_BASE_URL}
         * @return this builder
         */
        public Builder baseUrl(String baseUrl) {
            this.baseUrl = Objects.requireNonNull(baseUrl, "baseUrl");
            return this;
        }

        /**
         * Adds handshake headers. They cannot replace the {@code X-API-Key} header.
         *
         * @param headers the headers to add
         * @return this builder
         */
        public Builder headers(Map<String, String> headers) {
            this.headers.putAll(headers);
            return this;
        }

        /**
         * Replaces the connector, for example to use another websocket library or a fake in tests.
         *
         * @param connector the connector; defaults to a {@link JdkWebSocketConnector}
         * @return this builder
         */
        public Builder connector(WebSocketConnector connector) {
            this.connector = connector;
            return this;
        }

        /**
         * Sets the logger for lifecycle lines (prefixed {@code [drishti-sdk]}) and warnings.
         *
         * @param logger the logger; defaults to {@code System.getLogger(DrishtiWebSocketSession.class.getName())}
         * @return this builder
         */
        public Builder logger(System.Logger logger) {
            this.logger = logger;
            return this;
        }

        /**
         * Turns lifecycle logging (connect, subscribe and reconnect lines, at {@code INFO}) on or
         * off. Warnings are always logged.
         *
         * @param enabled {@code false} to silence lifecycle lines; default {@code true}
         * @return this builder
         */
        public Builder lifecycleLogging(boolean enabled) {
            this.lifecycleLogging = enabled;
            return this;
        }

        /**
         * Replaces the clock, for tests.
         *
         * @param clock the clock; defaults to {@link SessionClock#system()}
         * @return this builder
         */
        public Builder clock(SessionClock clock) {
            this.clock = clock;
            return this;
        }

        /**
         * Sets the first reconnect delay; the delay doubles after each failed attempt.
         *
         * @param delay the initial delay, at least 100 ms; default 1 second
         * @return this builder
         */
        public Builder reconnectInitialDelay(Duration delay) {
            this.reconnectInitialDelay = Objects.requireNonNull(delay, "delay");
            return this;
        }

        /**
         * Sets the longest reconnect delay.
         *
         * @param delay the maximum delay; default 30 seconds
         * @return this builder
         */
        public Builder reconnectMaxDelay(Duration delay) {
            this.reconnectMaxDelay = Objects.requireNonNull(delay, "delay");
            return this;
        }

        /**
         * Sets the factor the reconnect delay grows by after each failed attempt.
         *
         * @param multiplier the multiplier, at least {@code 1.0}; default {@code 2.0}
         * @return this builder
         */
        public Builder reconnectBackoffMultiplier(double multiplier) {
            this.reconnectBackoffMultiplier = multiplier;
            return this;
        }

        /**
         * Sets the random spread applied to each delay, so many clients do not reconnect at the
         * same moment.
         *
         * @param ratio the spread as a fraction of the delay, from {@code 0.0} to {@code 1.0};
         *     default {@code 0.2} (plus or minus 20%)
         * @return this builder
         */
        public Builder reconnectJitterRatio(double ratio) {
            this.reconnectJitterRatio = ratio;
            return this;
        }

        /**
         * Sets how many failed attempts at the maximum delay trigger the reconnect warning.
         *
         * @param attempts the attempt count; default {@code 10}
         * @return this builder
         */
        public Builder reconnectWarnAfterAttempts(int attempts) {
            this.reconnectWarnAfterAttempts = attempts;
            return this;
        }

        /**
         * Sets how long to wait for the server to acknowledge a subscribe request.
         *
         * @param timeout the timeout, at least 1 second; default 10 seconds
         * @return this builder
         */
        public Builder subscribeAckTimeout(Duration timeout) {
            this.subscribeAckTimeout = Objects.requireNonNull(timeout, "timeout");
            return this;
        }

        /**
         * Sets how many times a subscribe request is tried on one connection. Permanent rejections
         * are not retried.
         *
         * @param attempts the attempt count; default {@code 10}
         * @return this builder
         */
        public Builder subscribeMaxAttempts(int attempts) {
            this.subscribeMaxAttempts = attempts;
            return this;
        }

        /**
         * Sets the delay before the first subscribe retry.
         *
         * @param delay the initial delay; default 1 second
         * @return this builder
         */
        public Builder subscribeRetryInitialDelay(Duration delay) {
            this.subscribeRetryInitialDelay = Objects.requireNonNull(delay, "delay");
            return this;
        }

        /**
         * Sets the longest delay between subscribe retries.
         *
         * @param delay the maximum delay; default 30 seconds
         * @return this builder
         */
        public Builder subscribeRetryMaxDelay(Duration delay) {
            this.subscribeRetryMaxDelay = Objects.requireNonNull(delay, "delay");
            return this;
        }

        /**
         * Sets the factor the subscribe retry delay grows by.
         *
         * @param multiplier the multiplier, at least {@code 1.0}; default {@code 2.0}
         * @return this builder
         */
        public Builder subscribeRetryBackoffMultiplier(double multiplier) {
            this.subscribeRetryBackoffMultiplier = multiplier;
            return this;
        }

        /**
         * Sets how long the session may go without receiving any message before it reconnects.
         *
         * @param timeout the timeout, or {@link Duration#ZERO} to disable; default 90 seconds
         * @return this builder
         */
        public Builder heartbeatTimeout(Duration timeout) {
            this.heartbeatTimeout = Objects.requireNonNull(timeout, "timeout");
            return this;
        }

        /**
         * Builds the session. It does not connect until {@link DrishtiWebSocketSession#run()} or
         * {@link DrishtiWebSocketSession#start()}.
         *
         * @return a new session
         * @throws IllegalArgumentException when the API key is {@code null} or blank
         */
        public DrishtiWebSocketSession build() {
            return new DrishtiWebSocketSession(this);
        }
    }
}
