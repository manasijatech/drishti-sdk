<?php

// Minimal Drishti WebSocket listener. Run as a long-lived CLI worker:
//   DRISHTI_API_KEY=... php php/examples/ws_listen.php
// In production keep it alive with supervisord, systemd, or a framework queue worker.

declare(strict_types=1);

require __DIR__ . '/../../vendor/autoload.php';

use Drishti\DrishtiClient;
use Drishti\WebSocket\Event\WebSocketEvent;

$apiKey = getenv('DRISHTI_API_KEY');
if (!is_string($apiKey) || $apiKey === '') {
    fwrite(STDERR, "Set DRISHTI_API_KEY first.\n");
    exit(1);
}

$client = new DrishtiClient(apiKey: $apiKey);
$session = $client->websocket(
    onOpen: static fn (string $reason) => print("connected\n"),
    onClose: static fn (string $reason) => print("disconnected: {$reason}\n"),
    onError: static fn (WebSocketEvent $event) => print('error: ' . json_encode($event->toArray()) . "\n"),
);

$session->onNews(static function (array $item): void {
    printf("[news] %s\n", json_encode($item, JSON_UNESCAPED_SLASHES));
});
$session->onAnnouncements(static function (array $item): void {
    printf("[announcement] %s\n", json_encode($item, JSON_UNESCAPED_SLASHES));
});

$session->subscribe('news', symbols: ['RELIANCE', 'TCS']);
$session->subscribe('announcements', categories: ['Board Meeting']);

// Blocks until $session->stop() is called (e.g. from a handler or a signal handler).
$session->run();
