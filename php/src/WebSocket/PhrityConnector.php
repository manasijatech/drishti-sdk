<?php

declare(strict_types=1);

namespace Drishti\WebSocket;

use WebSocket\Client;
use WebSocket\Middleware\CloseHandler;
use WebSocket\Middleware\PingResponder;

/** Default connector backed by phrity/websocket. */
final class PhrityConnector implements WebSocketConnector
{
    public function __construct(private readonly float $openTimeout = 20.0)
    {
    }

    public function connect(string $url, array $headers): WebSocketConnection
    {
        $client = new Client($url);
        $client->setTimeout($this->openTimeout);
        foreach ($headers as $name => $value) {
            $client->addHeader($name, $value);
        }
        $client->addMiddleware(new CloseHandler());
        $client->addMiddleware(new PingResponder());
        $client->connect();

        return new PhrityConnection($client);
    }
}
