<?php

declare(strict_types=1);

namespace Drishti\WebSocket\Event;

/** A data payload pushed on a product channel (news, announcements, …). */
final readonly class DataEvent implements WebSocketEvent
{
    /** @param array<string, mixed> $data Non-object payloads are wrapped as ['raw' => $value]. */
    public function __construct(public string $channel, public array $data)
    {
    }

    public function kind(): string
    {
        return 'data';
    }

    public function toArray(): array
    {
        return ['kind' => 'data', 'channel' => $this->channel, 'data' => $this->data];
    }
}
