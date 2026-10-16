<?php

declare(strict_types=1);

namespace Drishti\Tests\Support;

use Closure;
use Drishti\WebSocket\WebSocketConnection;
use Drishti\WebSocket\WebSocketConnector;

/** Hands out FakeConnections; $configure scripts each one (by 1-based connection number). */
final class ScriptedConnector implements WebSocketConnector
{
    /** @var list<FakeConnection> */
    public array $connections = [];

    /** @var list<array{url: string, headers: array<string, string>}> */
    public array $calls = [];

    /** @param Closure(int, FakeConnection): void $configure */
    public function __construct(private readonly FakeClock $clock, private readonly Closure $configure)
    {
    }

    public function connect(string $url, array $headers): WebSocketConnection
    {
        $this->calls[] = ['url' => $url, 'headers' => $headers];
        $connection = new FakeConnection($this->clock);
        $this->connections[] = $connection;
        ($this->configure)(count($this->connections), $connection);

        return $connection;
    }

    /** onSend hook that acknowledges every subscribe. */
    public static function acknowledge(string $tier = 'pro'): Closure
    {
        return static function (FakeConnection $connection, array $message) use ($tier): void {
            $connection->push([
                'status' => 'subscribed',
                'product' => $message['product'] ?? '',
                'tier' => $tier,
                'full_feed' => false,
                'symbols' => $message['symbols'] ?? [],
                'detailed' => $message['detailed'] ?? true,
            ]);
        };
    }

    /** onSend hook that rejects every subscribe with a server error. */
    public static function reject(string $message, string $code): Closure
    {
        return static function (FakeConnection $connection) use ($message, $code): void {
            $connection->push(['error' => $message, 'code' => $code]);
        };
    }
}
