<?php

declare(strict_types=1);

namespace Drishti\WebSocket\Event;

/** Periodic server heartbeat. */
final readonly class HeartbeatEvent implements WebSocketEvent
{
    public function __construct(public string $sentAt)
    {
    }

    public function kind(): string
    {
        return 'heartbeat';
    }

    public function toArray(): array
    {
        return ['kind' => 'heartbeat', 'sent_at' => $this->sentAt];
    }
}
