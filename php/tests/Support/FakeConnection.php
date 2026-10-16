<?php

declare(strict_types=1);

namespace Drishti\Tests\Support;

use Closure;
use Drishti\Exception\DrishtiWebSocketException;
use Drishti\WebSocket\WebSocketConnection;

/** Scripted socket: replies are queued by the test or by an onSend hook. */
final class FakeConnection implements WebSocketConnection
{
    /** @var list<array<string, mixed>> */
    public array $sent = [];
    public bool $closed = false;

    /** @var list<string> */
    private array $inbox = [];
    private bool $serverClosed = false;

    /** @param (Closure(self, array<string, mixed>): void)|null $onSend */
    public function __construct(private readonly FakeClock $clock, public ?Closure $onSend = null)
    {
    }

    /** @param array<string, mixed> ...$messages */
    public function push(array ...$messages): void
    {
        foreach ($messages as $message) {
            $this->inbox[] = json_encode($message, JSON_THROW_ON_ERROR);
        }
    }

    public function closeFromServer(): void
    {
        $this->serverClosed = true;
    }

    public function send(string $text): void
    {
        if ($this->closed || $this->serverClosed) {
            throw new DrishtiWebSocketException('send on closed socket');
        }
        /** @var array<string, mixed> $message */
        $message = json_decode($text, true, flags: JSON_THROW_ON_ERROR);
        $this->sent[] = $message;
        if ($this->onSend !== null) {
            ($this->onSend)($this, $message);
        }
    }

    public function receive(float $timeoutSeconds): ?string
    {
        if ($this->inbox !== []) {
            return array_shift($this->inbox);
        }
        if ($this->closed || $this->serverClosed) {
            throw new DrishtiWebSocketException('connection closed by server');
        }
        $this->clock->advance($timeoutSeconds);

        return null;
    }

    public function close(): void
    {
        $this->closed = true;
    }

    /** @return list<string> Products of the subscribe messages sent on this socket. */
    public function subscribedProducts(): array
    {
        return array_values(array_map(
            static fn (array $message): string => is_string($message['product'] ?? null) ? $message['product'] : '',
            array_filter($this->sent, static fn (array $message): bool => ($message['op'] ?? null) === 'subscribe'),
        ));
    }
}
