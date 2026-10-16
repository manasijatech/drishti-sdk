<?php

declare(strict_types=1);

namespace Drishti\Tests;

use Drishti\DrishtiClient;
use Drishti\Exception\DrishtiWebSocketException;
use Drishti\Tests\Support\FakeClock;
use Drishti\Tests\Support\FakeConnection;
use Drishti\Tests\Support\RecordingHttpClient;
use Drishti\Tests\Support\RecordingLogger;
use Drishti\Tests\Support\ScriptedConnector;
use Drishti\WebSocket\DrishtiWebSocketSession;
use Drishti\WebSocket\Event\DataEvent;
use Drishti\WebSocket\Event\HeartbeatEvent;
use Drishti\WebSocket\Event\SubscribedEvent;
use Drishti\WebSocket\Event\WebSocketEvent;
use InvalidArgumentException;
use PHPUnit\Framework\TestCase;
use RuntimeException;

/**
 * Session behaviour mirrored from python/tests/test_websocket.py and js/tests/websocket.test.mjs.
 * Time is faked: it only advances when the session sleeps or waits on the socket.
 */
final class WebSocketSessionTest extends TestCase
{
    private FakeClock $clock;
    private RecordingLogger $logger;

    protected function setUp(): void
    {
        $this->clock = new FakeClock();
        $this->logger = new RecordingLogger();
    }

    private function session(ScriptedConnector $connector, mixed ...$options): DrishtiWebSocketSession
    {
        /** @phpstan-ignore argument.type (named options forwarded verbatim, like the SDK's own websocket()) */
        return new DrishtiWebSocketSession(...[
            'apiKey' => 'test-key',
            'baseUrl' => 'https://api.test',
            'connector' => $connector,
            'logger' => $this->logger,
            'reconnectJitterRatio' => 0.0,
            'clock' => $this->clock->now(...),
            'sleep' => $this->clock->sleep(...),
            ...$options,
        ]);
    }

    public function testSubscribesOnConnectAndLogsTheAck(): void
    {
        $connector = new ScriptedConnector($this->clock, static function (int $n, FakeConnection $c): void {
            $c->onSend = ScriptedConnector::acknowledge('pro');
        });
        $acks = [];
        /** @var DrishtiWebSocketSession|null $session */
        $session = null;
        $session = $this->session(
            $connector,
            onSubscribed: static function (WebSocketEvent $event) use (&$acks): void {
                $acks[] = $event;
            },
            onOpen: static function () use (&$session): void {
                $session?->stop();
            },
        );
        $session->subscribe('news', symbols: [' tcs ', 'TCS', '']);

        $session->run(60.0);

        self::assertCount(1, $connector->connections);
        self::assertSame(
            [['op' => 'subscribe', 'product' => 'news', 'symbols' => ['TCS'], 'detailed' => true]],
            $connector->connections[0]->sent,
        );
        self::assertCount(1, $acks);
        self::assertInstanceOf(SubscribedEvent::class, $acks[0]);
        self::assertSame('pro', $acks[0]->tier);
        self::assertContains('[drishti-sdk] connected; resubscribing 1 product(s)', $this->logger->messages());
        self::assertContains(
            '[drishti-sdk] subscribed news tier=pro full_feed=false (reconnect, attempt 1)',
            $this->logger->messages(),
        );
        self::assertTrue($connector->connections[0]->closed, 'stop() closes the socket');
    }

    public function testRejectionWhileConnectedRaisesTheServerError(): void
    {
        $connector = new ScriptedConnector($this->clock, static function (int $n, FakeConnection $c): void {
            $c->onSend = ScriptedConnector::reject('subscription denied', 'forbidden');
        });
        $caught = null;
        /** @var DrishtiWebSocketSession|null $session */
        $session = null;
        $session = $this->session(
            $connector,
            subscribeMaxAttempts: 1,
            onOpen: static function () use (&$session, &$caught): void {
                try {
                    $session?->subscribe('announcements');
                } catch (DrishtiWebSocketException $e) {
                    $caught = $e;
                }
                $session?->stop();
            },
        );

        $session->run(60.0);

        self::assertInstanceOf(DrishtiWebSocketException::class, $caught);
        self::assertSame('subscription denied', $caught->getMessage());
        self::assertSame('forbidden', $caught->getErrorCode());
        self::assertContains(
            '[drishti-sdk] subscribe failed product=announcements attempt=1/1 (subscribe): subscription denied [forbidden]',
            $this->logger->messages(),
        );
    }

    public function testPermanentRejectionIsNotRetriedInPlace(): void
    {
        $connector = new ScriptedConnector($this->clock, static function (int $n, FakeConnection $c): void {
            $c->onSend = ScriptedConnector::reject("WebSocket addon 'announcements' is not enabled for this key", 'forbidden');
        });
        $caught = null;
        /** @var DrishtiWebSocketSession|null $session */
        $session = null;
        $session = $this->session(
            $connector,
            subscribeMaxAttempts: 10,
            onOpen: static function () use (&$session, &$caught): void {
                try {
                    $session?->subscribe('announcements');
                } catch (DrishtiWebSocketException $e) {
                    $caught = $e;
                }
                $session?->stop();
            },
        );

        $session->run(60.0);

        self::assertInstanceOf(DrishtiWebSocketException::class, $caught);
        self::assertSame('forbidden', $caught->getErrorCode());
        self::assertSame(['announcements'], $connector->connections[0]->subscribedProducts());
        self::assertSame([], $this->clock->sleeps);
    }

    public function testTransientRejectionIsRetriedWithBackoff(): void
    {
        $connector = new ScriptedConnector($this->clock, static function (int $n, FakeConnection $c): void {
            $c->onSend = static function (FakeConnection $connection, array $message): void {
                if (count($connection->sent) < 3) {
                    $connection->push(['error' => 'busy', 'code' => 'rate_limited']);

                    return;
                }
                ScriptedConnector::acknowledge()($connection, $message);
            };
        });
        /** @var DrishtiWebSocketSession|null $session */
        $session = null;
        $session = $this->session(
            $connector,
            subscribeMaxAttempts: 3,
            subscribeRetryInitialDelay: 1.0,
            subscribeRetryBackoffMultiplier: 2.0,
            onOpen: static function () use (&$session): void {
                $session?->subscribe('alerts');
                $session?->stop();
            },
        );

        $session->run(60.0);

        self::assertSame(['alerts', 'alerts', 'alerts'], $connector->connections[0]->subscribedProducts());
        self::assertSame([1.0, 2.0], $this->clock->sleeps);
    }

    public function testConnectionCloseDuringPendingSubscribeFailsWithoutRetry(): void
    {
        $connector = new ScriptedConnector($this->clock, static function (int $n, FakeConnection $c): void {
            $c->onSend = static function (FakeConnection $connection): void {
                $connection->closeFromServer();
            };
        });
        $caught = null;
        /** @var DrishtiWebSocketSession|null $session */
        $session = null;
        $session = $this->session(
            $connector,
            onOpen: static function () use (&$session, &$caught): void {
                try {
                    $session?->subscribe('news');
                } catch (DrishtiWebSocketException $e) {
                    $caught = $e;
                }
                $session?->stop();
            },
        );

        $session->run(60.0);

        self::assertInstanceOf(DrishtiWebSocketException::class, $caught);
        self::assertSame('WebSocket connection closed', $caught->getMessage());
        self::assertSame(['news'], $connector->connections[0]->subscribedProducts());
    }

    public function testChannelHandlersReceiveTypedPayloads(): void
    {
        $connector = new ScriptedConnector($this->clock, static function (int $n, FakeConnection $c): void {
            $c->push(
                ['channel' => 'announcements', 'data' => ['symbol' => 'RELIANCE', 'title' => 'Results']],
                ['channel' => 'alerts', 'data' => ['symbol' => 'TCS', 'message' => 'Price move']],
                ['channel' => 'block-deals', 'data' => ['symbol' => 'INFY']],
            );
        });
        $announcements = [];
        $alerts = [];
        $blockDeals = [];
        $channels = [];
        $messageKinds = [];
        $session = $this->session($connector);
        $session->on('announcements', static function (array $data) use (&$announcements): void {
            $announcements[] = $data;
        });
        $session->onAlerts(static function (array $data) use (&$alerts): void {
            $alerts[] = $data;
        });
        $session->onBlockDeals(static function (array $data) use (&$blockDeals): void {
            $blockDeals[] = $data;
        });
        $session->on('data', static function (DataEvent $event) use (&$channels): void {
            $channels[] = $event->channel;
        });
        $session->on('message', static function (WebSocketEvent $event) use (&$messageKinds): void {
            $messageKinds[] = $event->kind();
        });

        $session->run(5.0);

        self::assertSame([['symbol' => 'RELIANCE', 'title' => 'Results']], $announcements);
        self::assertSame([['symbol' => 'TCS', 'message' => 'Price move']], $alerts);
        self::assertSame([['symbol' => 'INFY']], $blockDeals);
        self::assertSame(['announcements', 'alerts', 'block_deals'], $channels);
        self::assertSame(['data', 'data', 'data'], $messageKinds);
    }

    public function testOffRemovesAChannelHandler(): void
    {
        $connector = new ScriptedConnector($this->clock, static function (int $n, FakeConnection $c): void {
            $c->push(
                ['channel' => 'announcements', 'data' => ['symbol' => 'RELIANCE']],
                ['channel' => 'announcements', 'data' => ['symbol' => 'TCS']],
            );
        });
        $received = [];
        $session = $this->session($connector);
        $handler = null;
        $handler = static function (array $data) use (&$received, &$handler, &$session): void {
            $received[] = $data['symbol'] ?? null;
            $session->off('announcements', $handler);
        };
        $session->onAnnouncements($handler);

        $session->run(5.0);

        self::assertSame(['RELIANCE'], $received);
    }

    public function testHeartbeatsAreDispatchedAndTracked(): void
    {
        $connector = new ScriptedConnector($this->clock, static function (int $n, FakeConnection $c): void {
            $c->push(['type' => 'heartbeat', 'sent_at' => '2026-01-01T00:00:00Z']);
        });
        $beats = [];
        $session = $this->session($connector);
        $session->on('heartbeat', static function (HeartbeatEvent $event) use (&$beats): void {
            $beats[] = $event->sentAt;
        });
        $startedAt = $this->clock->now;

        $session->run(3.0);

        self::assertSame(['2026-01-01T00:00:00Z'], $beats);
        self::assertSame($startedAt, $session->lastHeartbeatReceivedAt());
    }

    public function testFailedConnectionSetupClosesTheSocket(): void
    {
        /** @var DrishtiWebSocketSession|null $session */
        $session = null;
        $connector = new ScriptedConnector($this->clock, static function (int $n, FakeConnection $c) use (&$session): void {
            if ($n === 3) {
                $session?->stop();
            }
        });
        $session = $this->session($connector, subscribeMaxAttempts: 1, subscribeAckTimeout: 1.0);
        $session->subscribe('news', symbols: ['INFY']);

        $session->run(600.0);

        self::assertCount(3, $connector->connections);
        foreach ($connector->connections as $connection) {
            self::assertTrue($connection->closed);
        }
        self::assertContains(
            '[drishti-sdk] connection setup failed: Subscribe acknowledgement timed out',
            $this->logger->messages(),
        );
    }

    public function testRejectedSubscriptionsKeepReconnectingWithBackoff(): void
    {
        /** @var DrishtiWebSocketSession|null $session */
        $session = null;
        $connector = new ScriptedConnector($this->clock, static function (int $n, FakeConnection $c) use (&$session): void {
            $reject = ScriptedConnector::reject('not enabled', 'forbidden');
            $c->onSend = static function (FakeConnection $connection, array $message) use ($n, $reject, &$session): void {
                $reject($connection, $message);
                if ($n >= 4) {
                    $session?->stop();
                }
            };
        });
        $session = $this->session(
            $connector,
            reconnectInitialDelay: 1.0,
            reconnectBackoffMultiplier: 2.0,
            reconnectMaxDelay: 30.0,
        );
        $session->subscribe('announcements');

        $session->run(600.0);

        self::assertCount(4, $connector->connections);
        foreach ($connector->connections as $connection) {
            self::assertTrue($connection->closed);
            self::assertSame(['announcements'], $connection->subscribedProducts(), 'subscription is kept');
        }
        self::assertSame([2.0, 4.0, 8.0], $this->clock->sleeps);
    }

    public function testReconnectsImmediatelyAfterAnEstablishedConnectionDrops(): void
    {
        $connector = new ScriptedConnector($this->clock, static function (int $n, FakeConnection $c): void {
            $c->onSend = static function (FakeConnection $connection, array $message) use ($n): void {
                ScriptedConnector::acknowledge()($connection, $message);
                if ($n === 1) {
                    $connection->closeFromServer();
                }
            };
        });
        $closeReasons = [];
        $opens = 0;
        /** @var DrishtiWebSocketSession|null $session */
        $session = null;
        $session = $this->session(
            $connector,
            onOpen: static function () use (&$session, &$opens): void {
                if (++$opens === 2) {
                    $session?->stop();
                }
            },
            onClose: static function (string $reason) use (&$closeReasons): void {
                $closeReasons[] = $reason;
            },
        );
        $session->subscribe('news');

        $session->run(60.0);

        self::assertCount(2, $connector->connections);
        self::assertSame(['news'], $connector->connections[1]->subscribedProducts(), 'resubscribed after reconnect');
        self::assertSame([], $this->clock->sleeps, 'first reconnect is immediate');
        self::assertStringStartsWith('closed', $closeReasons[0] ?? '');
    }

    public function testHeartbeatTimeoutForcesReconnect(): void
    {
        $connector = new ScriptedConnector($this->clock, static function (int $n, FakeConnection $c): void {
            $c->onSend = ScriptedConnector::acknowledge();
        });
        $closeReasons = [];
        $opens = 0;
        /** @var DrishtiWebSocketSession|null $session */
        $session = null;
        $session = $this->session(
            $connector,
            heartbeatTimeout: 5.0,
            onOpen: static function () use (&$session, &$opens): void {
                if (++$opens === 2) {
                    $session?->stop();
                }
            },
            onClose: static function (string $reason) use (&$closeReasons): void {
                $closeReasons[] = $reason;
            },
        );
        $session->subscribe('news');

        $session->run(60.0);

        self::assertCount(2, $connector->connections);
        self::assertTrue($connector->connections[0]->closed);
        self::assertSame(['heartbeat timeout'], $closeReasons);
    }

    public function testWarnsAfterRepeatedConnectionFailures(): void
    {
        $connector = new ScriptedConnector($this->clock, static function (): void {
            throw new RuntimeException('connection refused');
        });
        $warnings = [];
        /** @var DrishtiWebSocketSession|null $session */
        $session = null;
        $session = $this->session(
            $connector,
            reconnectInitialDelay: 1.0,
            reconnectMaxDelay: 2.0,
            reconnectWarnAfterAttempts: 2,
            onReconnectWarning: static function (int $attempt, string $reason) use (&$warnings, &$session): void {
                $warnings[] = [$attempt, $reason];
                $session?->stop();
            },
        );

        $session->run(600.0);

        self::assertSame([[2, 'initial']], $warnings);
        self::assertSame([2.0], $this->clock->sleeps);
    }

    public function testClientWebsocketSharesCredentialsAndHeaders(): void
    {
        $connector = new ScriptedConnector($this->clock, static function (): void {
        });
        $client = new DrishtiClient(
            apiKey: 'test-key',
            baseUrl: 'https://api.test/',
            headers: ['X-Team' => 'ops', 'X-API-Key' => 'spoofed'],
            httpClient: new RecordingHttpClient([]),
        );
        /** @var DrishtiWebSocketSession|null $session */
        $session = null;
        $session = $client->websocket(
            connector: $connector,
            clock: $this->clock->now(...),
            sleep: $this->clock->sleep(...),
            onOpen: static function () use (&$session): void {
                $session?->stop();
            },
        );

        $session->run(60.0);

        self::assertSame('wss://api.test/v1/ws', $connector->calls[0]['url']);
        self::assertSame('test-key', $connector->calls[0]['headers']['X-API-Key']);
        self::assertSame('ops', $connector->calls[0]['headers']['X-Team']);
    }

    public function testBlankApiKeyIsRejected(): void
    {
        $this->expectException(InvalidArgumentException::class);

        new DrishtiWebSocketSession(apiKey: '  ');
    }
}
