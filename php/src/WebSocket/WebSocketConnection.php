<?php

declare(strict_types=1);

namespace Drishti\WebSocket;

use Drishti\Exception\DrishtiWebSocketException;

/** One open WebSocket. Implemented by {@see PhrityConnection}; tests inject a fake. */
interface WebSocketConnection
{
    /** Send a text frame. */
    public function send(string $text): void;

    /**
     * Wait up to $timeoutSeconds for the next text frame.
     *
     * @return string|null The frame, or null if nothing arrived in time.
     * @throws DrishtiWebSocketException When the connection is closed or broken.
     */
    public function receive(float $timeoutSeconds): ?string;

    public function close(): void;
}
