<?php

declare(strict_types=1);

namespace Drishti\WebSocket\Event;

/** An error sent by the server (e.g. a rejected subscribe) or a message that could not be parsed. */
final readonly class ErrorEvent implements WebSocketEvent
{
    public function __construct(public string $message, public ?string $code = null)
    {
    }

    public function kind(): string
    {
        return 'error';
    }

    public function toArray(): array
    {
        return ['kind' => 'error', 'message' => $this->message, 'code' => $this->code];
    }
}
