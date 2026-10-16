<?php

declare(strict_types=1);

namespace Drishti\WebSocket;

use Closure;
use Drishti\DrishtiClient;
use Drishti\Exception\DrishtiWebSocketException;
use Drishti\WebSocket\Event\DataEvent;
use Drishti\WebSocket\Event\ErrorEvent;
use Drishti\WebSocket\Event\HeartbeatEvent;
use Drishti\WebSocket\Event\SubscribedEvent;
use Drishti\WebSocket\Event\WebSocketEvent;
use InvalidArgumentException;
use Psr\Log\LoggerInterface;
use Psr\Log\NullLogger;
use Throwable;

/**
 * Blocking client for the Drishti WebSocket (`/v1/ws`), for long-running CLI workers.
 *
 * Register handlers and subscriptions, then call {@see run()}. The session connects, sends every
 * subscription, dispatches events, and reconnects with exponential backoff when the connection
 * drops, a subscribe fails, or no message arrives within `heartbeatTimeout`. Subscriptions are kept
 * across reconnects, including ones the server rejected. Call {@see stop()} (e.g. from a handler)
 * to end the loop.
 */
final class DrishtiWebSocketSession
{
    private const LIFECYCLE_EVENTS = ['subscribed', 'data', 'error', 'heartbeat', 'raw', 'message'];
    private const POLL_INTERVAL_SECONDS = 1.0;

    private readonly string $baseUrl;
    private readonly WebSocketConnector $connector;
    private readonly LoggerInterface $logger;
    private readonly float $reconnectInitialDelay;
    private readonly float $reconnectMaxDelay;
    private readonly float $reconnectBackoffMultiplier;
    private readonly float $reconnectJitterRatio;
    private readonly int $reconnectWarnAfterAttempts;
    private readonly float $subscribeAckTimeout;
    private readonly int $subscribeMaxAttempts;
    private readonly float $subscribeRetryInitialDelay;
    private readonly float $subscribeRetryMaxDelay;
    private readonly float $subscribeRetryBackoffMultiplier;
    private readonly float $heartbeatTimeout;
    /** @var Closure(): float */
    private readonly Closure $clock;
    /** @var Closure(float): void */
    private readonly Closure $sleep;

    /** @var array<string, list<callable>> */
    private array $handlers = [];
    /** @var array<string, array{product: string, symbols: list<string>, categories: list<string>, detailed: bool}> */
    private array $subscriptions = [];
    private ?WebSocketConnection $connection = null;
    private bool $stopped = false;
    private ?float $deadline = null;
    private ?float $lastMessageAt = null;
    private ?float $lastHeartbeatAt = null;
    /** @var array{product: string, result: SubscribedEvent|ErrorEvent|null}|null */
    private ?array $pendingSubscribe = null;

    /**
     * @param array<string, string> $headers Extra handshake headers (cannot override X-API-Key).
     * @param WebSocketConnector|null $connector Defaults to {@see PhrityConnector}.
     * @param float $heartbeatTimeout Reconnect when nothing arrives for this many seconds (0 disables).
     * @param bool $enableLifecycleLogging Log connect/subscribe/reconnect lines at info level.
     * @param (callable(WebSocketEvent): void)|null $onSubscribed
     * @param (callable(WebSocketEvent): void)|null $onData
     * @param (callable(array<string, mixed>): void)|null $onNews
     * @param (callable(array<string, mixed>): void)|null $onBlockDeals
     * @param (callable(array<string, mixed>): void)|null $onAnnouncements
     * @param (callable(array<string, mixed>): void)|null $onEarnings
     * @param (callable(array<string, mixed>): void)|null $onConcalls
     * @param (callable(array<string, mixed>): void)|null $onAlerts
     * @param (callable(WebSocketEvent): void)|null $onError
     * @param (callable(WebSocketEvent): void)|null $onMessage Every event.
     * @param (callable(string): void)|null $onOpen Called with "connected" after subscriptions are sent.
     * @param (callable(string): void)|null $onClose Called with the reason an established connection ended.
     * @param (callable(int, float, string): void)|null $onReconnectAttempt (attempt, delay seconds, reason)
     * @param (callable(int, string): void)|null $onReconnectWarning (attempt, reason) after repeated failures.
     * @param (Closure(): float)|null $clock Monotonic seconds; injectable for tests.
     * @param (Closure(float): void)|null $sleep Sleep for seconds; injectable for tests.
     */
    public function __construct(
        private readonly string $apiKey,
        string $baseUrl = DrishtiClient::DEFAULT_BASE_URL,
        private readonly array $headers = [],
        ?WebSocketConnector $connector = null,
        float $reconnectInitialDelay = 1.0,
        float $reconnectMaxDelay = 30.0,
        float $reconnectBackoffMultiplier = 2.0,
        float $reconnectJitterRatio = 0.2,
        int $reconnectWarnAfterAttempts = 10,
        float $subscribeAckTimeout = 10.0,
        int $subscribeMaxAttempts = 10,
        float $subscribeRetryInitialDelay = 1.0,
        float $subscribeRetryMaxDelay = 30.0,
        float $subscribeRetryBackoffMultiplier = 2.0,
        float $heartbeatTimeout = 90.0,
        private readonly bool $enableLifecycleLogging = true,
        ?LoggerInterface $logger = null,
        ?callable $onSubscribed = null,
        ?callable $onData = null,
        ?callable $onNews = null,
        ?callable $onBlockDeals = null,
        ?callable $onAnnouncements = null,
        ?callable $onEarnings = null,
        ?callable $onConcalls = null,
        ?callable $onAlerts = null,
        ?callable $onError = null,
        ?callable $onMessage = null,
        private readonly mixed $onOpen = null,
        private readonly mixed $onClose = null,
        private readonly mixed $onReconnectAttempt = null,
        private readonly mixed $onReconnectWarning = null,
        ?Closure $clock = null,
        ?Closure $sleep = null,
    ) {
        if (trim($apiKey) === '') {
            throw new InvalidArgumentException('DrishtiWebSocketSession requires a non-empty apiKey');
        }
        $this->baseUrl = rtrim($baseUrl, '/');
        $this->connector = $connector ?? new PhrityConnector();
        $this->logger = $logger ?? new NullLogger();
        $this->reconnectInitialDelay = max(0.1, $reconnectInitialDelay);
        $this->reconnectMaxDelay = max($this->reconnectInitialDelay, $reconnectMaxDelay);
        $this->reconnectBackoffMultiplier = max(1.0, $reconnectBackoffMultiplier);
        $this->reconnectJitterRatio = min(1.0, max(0.0, $reconnectJitterRatio));
        $this->reconnectWarnAfterAttempts = max(1, $reconnectWarnAfterAttempts);
        $this->subscribeAckTimeout = max(1.0, $subscribeAckTimeout);
        $this->subscribeMaxAttempts = max(1, $subscribeMaxAttempts);
        $this->subscribeRetryInitialDelay = max(0.1, $subscribeRetryInitialDelay);
        $this->subscribeRetryMaxDelay = max($this->subscribeRetryInitialDelay, $subscribeRetryMaxDelay);
        $this->subscribeRetryBackoffMultiplier = max(1.0, $subscribeRetryBackoffMultiplier);
        $this->heartbeatTimeout = max(0.0, $heartbeatTimeout);
        $this->clock = $clock ?? static fn (): float => hrtime(true) / 1e9;
        $this->sleep = $sleep ?? static function (float $seconds): void {
            if ($seconds > 0) {
                usleep((int) round($seconds * 1_000_000));
            }
        };

        $initialHandlers = [
            'subscribed' => $onSubscribed,
            'data' => $onData,
            Products::NEWS => $onNews,
            Products::BLOCK_DEALS => $onBlockDeals,
            Products::ANNOUNCEMENTS => $onAnnouncements,
            Products::EARNINGS => $onEarnings,
            Products::CONCALLS => $onConcalls,
            Products::ALERTS => $onAlerts,
            'error' => $onError,
            'message' => $onMessage,
        ];
        foreach ($initialHandlers as $event => $handler) {
            if ($handler !== null) {
                $this->on($event, $handler);
            }
        }
    }

    // --- Handlers --------------------------------------------------------------------------

    /**
     * Listen for an event kind ("subscribed", "data", "error", "heartbeat", "raw", "message") —
     * handler receives the {@see WebSocketEvent} — or a product channel ("news", "alerts", …) —
     * handler receives that channel's data payload array.
     */
    public function on(string $event, callable $handler): void
    {
        self::assertKnownEvent($event);
        $this->handlers[$event][] = $handler;
    }

    /** Remove a handler registered with {@see on()} or an on<Channel>() helper. */
    public function off(string $event, callable $handler): void
    {
        $this->handlers[$event] = array_values(array_filter(
            $this->handlers[$event] ?? [],
            static fn (callable $candidate): bool => $candidate !== $handler,
        ));
    }

    /** @param callable(array<string, mixed>): void $handler */
    public function onNews(callable $handler): void
    {
        $this->on(Products::NEWS, $handler);
    }

    /** @param callable(array<string, mixed>): void $handler */
    public function onBlockDeals(callable $handler): void
    {
        $this->on(Products::BLOCK_DEALS, $handler);
    }

    /** @param callable(array<string, mixed>): void $handler */
    public function onAnnouncements(callable $handler): void
    {
        $this->on(Products::ANNOUNCEMENTS, $handler);
    }

    /** @param callable(array<string, mixed>): void $handler */
    public function onEarnings(callable $handler): void
    {
        $this->on(Products::EARNINGS, $handler);
    }

    /** @param callable(array<string, mixed>): void $handler */
    public function onConcalls(callable $handler): void
    {
        $this->on(Products::CONCALLS, $handler);
    }

    /** @param callable(array<string, mixed>): void $handler */
    public function onAlerts(callable $handler): void
    {
        $this->on(Products::ALERTS, $handler);
    }

    // --- Subscriptions and lifecycle -------------------------------------------------------

    /**
     * Subscribe to a product. Before {@see run()} this only records the subscription; while
     * connected (e.g. from a handler) it is sent immediately and a rejection is thrown.
     * Subscriptions are re-sent on every reconnect.
     *
     * @param list<string> $symbols Empty for the full feed your plan allows.
     * @param list<string> $categories Announcement categories (see getAnnouncementsCategories()).
     * @throws DrishtiWebSocketException When sent while connected and the server rejects it.
     */
    public function subscribe(string $product, array $symbols = [], array $categories = [], bool $detailed = true): void
    {
        $options = [
            'product' => $product,
            'symbols' => WebSocketMessages::normalizeSymbols($symbols),
            'categories' => WebSocketMessages::normalizeCategories($categories),
            'detailed' => $detailed,
        ];
        $this->subscriptions[$product] = $options;
        if ($this->connection !== null) {
            $this->sendSubscribeWithRetry($options, 'subscribe');
        }
    }

    /**
     * Connect and process events until {@see stop()} is called or $durationSeconds elapses.
     * Reconnects automatically; never throws for connection problems.
     */
    public function run(?float $durationSeconds = null): void
    {
        $this->stopped = false;
        $this->deadline = $durationSeconds === null ? null : $this->now() + max(0.0, $durationSeconds);
        $attempt = 0;
        $delay = $this->reconnectInitialDelay;
        $reason = 'initial';
        try {
            while (!$this->shouldStop()) {
                $attempt++;
                if ($attempt > 1) {
                    $this->emitReconnectAttempt($attempt - 1, $delay, $reason);
                    $this->sleepFor($this->jitter($delay));
                    if ($this->shouldStop()) {
                        break;
                    }
                } elseif ($reason !== 'initial') {
                    $this->logLifecycle("reconnecting ({$reason})");
                }
                try {
                    $this->connection = $this->connector->connect(
                        WebSocketMessages::buildWebSocketUrl($this->baseUrl),
                        [...$this->headers, 'X-API-Key' => $this->apiKey],
                    );
                    $this->lastMessageAt = $this->now();
                    $this->logLifecycle(sprintf('connected; resubscribing %d product(s)', count($this->subscriptions)));
                    foreach ($this->subscriptions as $options) {
                        $this->sendSubscribeWithRetry($options, 'reconnect');
                    }
                    self::call($this->onOpen, 'connected');
                    $attempt = 0;
                    $delay = $this->reconnectInitialDelay;
                    $closeReason = $this->receiveUntilClosed();
                    $this->closeConnection();
                    if ($closeReason !== null) {
                        self::call($this->onClose, $closeReason);
                    }
                    $reason = 'connection closed';
                } catch (Throwable $e) {
                    $this->closeConnection();
                    $this->logLifecycle('connection setup failed: ' . $e->getMessage());
                    if ($delay >= $this->reconnectMaxDelay && $attempt >= $this->reconnectWarnAfterAttempts) {
                        $this->emitReconnectWarning($attempt, $reason, $e);
                        $attempt = 0;
                    }
                    $delay = min($delay * $this->reconnectBackoffMultiplier, $this->reconnectMaxDelay);
                }
            }
        } finally {
            $this->closeConnection();
            $this->pendingSubscribe = null;
        }
    }

    /** Stop {@see run()} after the current step and close the socket. Safe to call from handlers. */
    public function stop(): void
    {
        $this->stopped = true;
    }

    public function isConnected(): bool
    {
        return $this->connection !== null;
    }

    /** Clock time (see `clock`) of the last server heartbeat, or null if none yet. */
    public function lastHeartbeatReceivedAt(): ?float
    {
        return $this->lastHeartbeatAt;
    }

    // --- Internals -------------------------------------------------------------------------

    /** @return string|null Close reason, or null when stopped locally. */
    private function receiveUntilClosed(): ?string
    {
        while (!$this->shouldStop()) {
            $connection = $this->connection;
            if ($connection === null) {
                return 'closed';
            }
            try {
                $raw = $connection->receive($this->pollTimeout());
            } catch (DrishtiWebSocketException $e) {
                return 'closed: ' . $e->getMessage();
            }
            if ($raw === null) {
                if ($this->heartbeatTimeout > 0 && $this->now() - (float) $this->lastMessageAt >= $this->heartbeatTimeout) {
                    $this->logLifecycle(sprintf('no message for %.0fs; reconnecting', $this->heartbeatTimeout));

                    return 'heartbeat timeout';
                }
                continue;
            }
            $this->handleRaw($raw);
        }

        return null;
    }

    private function handleRaw(string $raw): void
    {
        $this->lastMessageAt = $this->now();
        $event = WebSocketMessages::parse($raw);
        if ($event instanceof HeartbeatEvent) {
            $this->lastHeartbeatAt = $this->now();
        } elseif ($this->pendingSubscribe !== null) {
            if ($event instanceof SubscribedEvent && $event->product === $this->pendingSubscribe['product']) {
                $this->pendingSubscribe['result'] = $event;
            } elseif ($event instanceof ErrorEvent) {
                $this->pendingSubscribe['result'] = $event;
            }
        }
        $this->dispatch($event);
    }

    private function dispatch(WebSocketEvent $event): void
    {
        foreach ([$event->kind(), 'message'] as $name) {
            foreach ($this->handlers[$name] ?? [] as $handler) {
                $handler($event);
            }
        }
        if ($event instanceof DataEvent && Products::isKnown($event->channel)) {
            foreach ($this->handlers[$event->channel] ?? [] as $handler) {
                $handler($event->data);
            }
        }
    }

    /** @param array{product: string, symbols: list<string>, categories: list<string>, detailed: bool} $options */
    private function sendSubscribeWithRetry(array $options, string $reason): void
    {
        $attempt = 0;
        $delay = $this->subscribeRetryInitialDelay;
        while ($attempt < $this->subscribeMaxAttempts) {
            if ($this->connection === null) {
                throw new DrishtiWebSocketException('WebSocket is not connected');
            }
            $attempt++;
            try {
                $ack = $this->sendSubscribeAndWaitForAck($options);
                $this->logLifecycle(sprintf(
                    'subscribed %s tier=%s full_feed=%s (%s, attempt %d)',
                    $options['product'],
                    $ack->tier,
                    $ack->fullFeed ? 'true' : 'false',
                    $reason,
                    $attempt,
                ));

                return;
            } catch (Throwable $e) {
                $code = $e instanceof DrishtiWebSocketException ? $e->getErrorCode() : null;
                $this->logLifecycle(sprintf(
                    'subscribe failed product=%s attempt=%d/%d (%s): %s%s',
                    $options['product'],
                    $attempt,
                    $this->subscribeMaxAttempts,
                    $reason,
                    $e->getMessage(),
                    $code !== null ? " [{$code}]" : '',
                ));
                $permanent = $code !== null && in_array($code, Products::PERMANENT_SUBSCRIBE_ERROR_CODES, true);
                if ($this->connection === null || $attempt >= $this->subscribeMaxAttempts || $permanent) {
                    throw $e instanceof DrishtiWebSocketException ? $e : new DrishtiWebSocketException($e->getMessage(), $code, $e);
                }
                $this->sleepFor($this->jitter($delay));
                $delay = min($delay * $this->subscribeRetryBackoffMultiplier, $this->subscribeRetryMaxDelay);
            }
        }
    }

    /** @param array{product: string, symbols: list<string>, categories: list<string>, detailed: bool} $options */
    private function sendSubscribeAndWaitForAck(array $options): SubscribedEvent
    {
        $connection = $this->connection ?? throw new DrishtiWebSocketException('WebSocket is not connected');
        $this->pendingSubscribe = ['product' => $options['product'], 'result' => null];
        try {
            $connection->send(json_encode(
                WebSocketMessages::buildSubscribeMessage(...$options),
                JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES,
            ));
            $deadline = $this->now() + $this->subscribeAckTimeout;
            while (($result = $this->pendingSubscribeResult()) === null) {
                $remaining = $deadline - $this->now();
                if ($remaining <= 0) {
                    throw new DrishtiWebSocketException('Subscribe acknowledgement timed out');
                }
                try {
                    $raw = $connection->receive(min($remaining, self::POLL_INTERVAL_SECONDS));
                } catch (DrishtiWebSocketException $e) {
                    $this->closeConnection();
                    throw new DrishtiWebSocketException('WebSocket connection closed', null, $e);
                }
                if ($raw !== null) {
                    $this->handleRaw($raw);
                }
            }
            if ($result instanceof ErrorEvent) {
                throw new DrishtiWebSocketException($result->message, $result->code);
            }

            return $result;
        } finally {
            $this->pendingSubscribe = null;
        }
    }

    /** Set by handleRaw() while waiting for an ack. */
    private function pendingSubscribeResult(): SubscribedEvent|ErrorEvent|null
    {
        return $this->pendingSubscribe['result'] ?? null;
    }

    private function emitReconnectAttempt(int $attempt, float $delay, string $reason): void
    {
        if ($this->onReconnectAttempt === null) {
            $this->logLifecycle(sprintf('reconnect attempt=%d delay=%.1fs reason=%s', $attempt, $delay, $reason));

            return;
        }
        self::call($this->onReconnectAttempt, $attempt, $delay, $reason);
    }

    private function emitReconnectWarning(int $attempt, string $reason, Throwable $e): void
    {
        if ($this->onReconnectWarning !== null) {
            self::call($this->onReconnectWarning, $attempt, $reason);

            return;
        }
        $this->logger->warning(sprintf(
            'Drishti WebSocket still unable to connect after %d attempts (%s): %s',
            $attempt,
            $reason,
            $e->getMessage(),
        ));
    }

    private function closeConnection(): void
    {
        $connection = $this->connection;
        $this->connection = null;
        if ($connection !== null) {
            try {
                $connection->close();
            } catch (Throwable) {
            }
        }
    }

    private function logLifecycle(string $message): void
    {
        if ($this->enableLifecycleLogging) {
            $this->logger->info('[drishti-sdk] ' . $message);
        }
    }

    private function jitter(float $delay): float
    {
        if ($this->reconnectJitterRatio <= 0) {
            return $delay;
        }
        $spread = $delay * $this->reconnectJitterRatio;

        return max(0.0, $delay + (mt_rand() / mt_getrandmax() * 2 - 1) * $spread);
    }

    private function sleepFor(float $seconds): void
    {
        if ($this->deadline !== null) {
            $seconds = min($seconds, max(0.0, $this->deadline - $this->now()));
        }
        ($this->sleep)($seconds);
    }

    private function pollTimeout(): float
    {
        $timeout = self::POLL_INTERVAL_SECONDS;
        if ($this->deadline !== null) {
            $timeout = min($timeout, max(0.0, $this->deadline - $this->now()));
        }

        return $timeout;
    }

    private function shouldStop(): bool
    {
        return $this->stopped || ($this->deadline !== null && $this->now() >= $this->deadline);
    }

    private function now(): float
    {
        return ($this->clock)();
    }

    private static function call(mixed $handler, mixed ...$args): void
    {
        if (is_callable($handler)) {
            $handler(...$args);
        }
    }

    private static function assertKnownEvent(string $event): void
    {
        if (!in_array($event, self::LIFECYCLE_EVENTS, true) && !Products::isKnown($event)) {
            throw new InvalidArgumentException(sprintf(
                'Unknown WebSocket event "%s"; expected one of: %s',
                $event,
                implode(', ', [...self::LIFECYCLE_EVENTS, ...Products::ALL]),
            ));
        }
    }
}
