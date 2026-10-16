<?php

declare(strict_types=1);

namespace Drishti\WebSocket\Event;

/** A JSON object the SDK does not recognise. */
final readonly class RawEvent implements WebSocketEvent
{
    /** @param array<string, mixed> $payload */
    public function __construct(public array $payload)
    {
    }

    public function kind(): string
    {
        return 'raw';
    }

    public function toArray(): array
    {
        return ['kind' => 'raw', 'payload' => $this->payload];
    }
}
