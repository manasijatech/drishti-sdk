<?php

declare(strict_types=1);

namespace Drishti\WebSocket;

/** Opens WebSocket connections. The default is {@see PhrityConnector}. */
interface WebSocketConnector
{
    /** @param array<string, string> $headers Handshake headers (includes X-API-Key). */
    public function connect(string $url, array $headers): WebSocketConnection;
}
