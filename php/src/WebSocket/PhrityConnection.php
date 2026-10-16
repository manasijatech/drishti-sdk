<?php

declare(strict_types=1);

namespace Drishti\WebSocket;

use Drishti\Exception\DrishtiWebSocketException;
use Throwable;
use WebSocket\Client;
use WebSocket\Exception\ConnectionTimeoutException;
use WebSocket\Message\Binary;
use WebSocket\Message\Close;
use WebSocket\Message\Text;

/** {@see WebSocketConnection} over a connected phrity/websocket client. */
final class PhrityConnection implements WebSocketConnection
{
    public function __construct(private readonly Client $client)
    {
    }

    public function send(string $text): void
    {
        try {
            $this->client->text($text);
        } catch (Throwable $e) {
            throw new DrishtiWebSocketException('WebSocket send failed: ' . $e->getMessage(), null, $e);
        }
    }

    public function receive(float $timeoutSeconds): ?string
    {
        $deadline = microtime(true) + max(0.0, $timeoutSeconds);
        while (true) {
            $remaining = $deadline - microtime(true);
            if ($remaining <= 0) {
                return null;
            }
            if (!$this->client->isConnected()) {
                throw new DrishtiWebSocketException('WebSocket connection closed');
            }
            try {
                $this->client->setTimeout($remaining);
                $message = $this->client->receive();
            } catch (ConnectionTimeoutException) {
                return null;
            } catch (Throwable $e) {
                throw new DrishtiWebSocketException('WebSocket connection closed: ' . $e->getMessage(), null, $e);
            }
            if ($message instanceof Text || $message instanceof Binary) {
                return $message->getContent();
            }
            if ($message instanceof Close) {
                throw new DrishtiWebSocketException('WebSocket connection closed: ' . $message->getContent());
            }
            // Ping/pong frames are answered by middleware; keep waiting for data.
        }
    }

    public function close(): void
    {
        try {
            if ($this->client->isConnected()) {
                $this->client->close();
            }
        } catch (Throwable) {
            // Closing a broken socket is best-effort.
        } finally {
            try {
                $this->client->disconnect();
            } catch (Throwable) {
            }
        }
    }
}
