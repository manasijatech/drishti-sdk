<?php

declare(strict_types=1);

namespace Drishti\WebSocket\Event;

/** A parsed message from the Drishti WebSocket. */
interface WebSocketEvent
{
    /** One of "subscribed", "data", "error", "heartbeat", "raw". */
    public function kind(): string;

    /**
     * Canonical snake_case representation, identical across the Drishti SDKs.
     *
     * @return array<string, mixed>
     */
    public function toArray(): array;
}
