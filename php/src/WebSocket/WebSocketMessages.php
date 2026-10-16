<?php

declare(strict_types=1);

namespace Drishti\WebSocket;

use Drishti\WebSocket\Event\DataEvent;
use Drishti\WebSocket\Event\ErrorEvent;
use Drishti\WebSocket\Event\HeartbeatEvent;
use Drishti\WebSocket\Event\RawEvent;
use Drishti\WebSocket\Event\SubscribedEvent;
use Drishti\WebSocket\Event\WebSocketEvent;
use JsonException;
use stdClass;

/** Pure helpers for the Drishti WebSocket wire protocol (`/v1/ws`). */
final class WebSocketMessages
{
    private function __construct()
    {
    }

    /** Build the WebSocket URL for an HTTP base URL (https → wss, http → ws). */
    public static function buildWebSocketUrl(string $baseUrl, ?string $apiKey = null): string
    {
        $base = rtrim($baseUrl, '/');
        $base = (string) preg_replace(['/^https:/i', '/^http:/i'], ['wss:', 'ws:'], $base);
        $url = $base . '/v1/ws';
        if ($apiKey !== null && $apiKey !== '') {
            $url .= '?' . http_build_query(['api_key' => $apiKey]);
        }

        return $url;
    }

    /**
     * Build the `subscribe` message: symbols are trimmed, upper-cased and de-duplicated; categories
     * are trimmed and de-duplicated and only sent when non-empty.
     *
     * @param list<string> $symbols
     * @param list<string> $categories
     * @return array{op: 'subscribe', product: string, symbols: list<string>, detailed: bool, categories?: list<string>}
     */
    public static function buildSubscribeMessage(
        string $product,
        array $symbols = [],
        array $categories = [],
        bool $detailed = true,
    ): array {
        $message = [
            'op' => 'subscribe',
            'product' => Products::toWire($product),
            'symbols' => self::normalizeSymbols($symbols),
            'detailed' => $detailed,
        ];
        $normalizedCategories = self::normalizeCategories($categories);
        if ($normalizedCategories !== []) {
            $message['categories'] = $normalizedCategories;
        }

        return $message;
    }

    /** Parse one raw text frame into a typed event. Never throws. */
    public static function parse(string $raw): WebSocketEvent
    {
        try {
            $object = json_decode($raw, false, flags: JSON_THROW_ON_ERROR);
        } catch (JsonException) {
            return new ErrorEvent('Invalid JSON');
        }
        if (!$object instanceof stdClass) {
            return new ErrorEvent('Expected a JSON object');
        }
        /** @var array<string, mixed> $payload */
        $payload = json_decode($raw, true, flags: JSON_THROW_ON_ERROR);

        if (($payload['type'] ?? null) === 'heartbeat') {
            return new HeartbeatEvent(self::stringOrEmpty($payload['sent_at'] ?? null));
        }
        if (($payload['status'] ?? null) === 'subscribed') {
            return new SubscribedEvent(
                product: Products::normalize(self::stringOrEmpty($payload['product'] ?? null)),
                tier: self::stringOrEmpty($payload['tier'] ?? null),
                fullFeed: (bool) ($payload['full_feed'] ?? false),
                symbols: self::stringList($payload['symbols'] ?? null),
                categories: self::stringList($payload['categories'] ?? null),
                detailed: ($payload['detailed'] ?? true) !== false,
            );
        }
        if (array_key_exists('error', $payload)) {
            $error = $payload['error'];
            $code = $payload['code'] ?? null;

            return new ErrorEvent(
                in_array($error, [null, '', false, 0, 0.0], true) ? 'Unknown error' : self::stringify($error),
                $code === null ? null : self::stringify($code),
            );
        }
        if (($payload['channel'] ?? null) !== null) {
            $channel = Products::normalize(self::stringify($payload['channel']));
            if (property_exists($object, 'data') && $object->data instanceof stdClass) {
                /** @var array<string, mixed> $data */
                $data = $payload['data'];

                return new DataEvent($channel, $data);
            }

            return new DataEvent($channel, ['raw' => $payload['data'] ?? null]);
        }

        return new RawEvent($payload);
    }

    /**
     * @param list<string> $symbols
     * @return list<string>
     */
    public static function normalizeSymbols(array $symbols): array
    {
        $normalized = [];
        foreach ($symbols as $symbol) {
            $token = strtoupper(trim($symbol));
            if ($token !== '' && !in_array($token, $normalized, true)) {
                $normalized[] = $token;
            }
        }

        return $normalized;
    }

    /**
     * @param list<string> $categories
     * @return list<string>
     */
    public static function normalizeCategories(array $categories): array
    {
        $normalized = [];
        foreach ($categories as $category) {
            $token = trim($category);
            if ($token !== '' && !in_array($token, $normalized, true)) {
                $normalized[] = $token;
            }
        }

        return $normalized;
    }

    private static function stringOrEmpty(mixed $value): string
    {
        return $value === null || $value === false ? '' : self::stringify($value);
    }

    private static function stringify(mixed $value): string
    {
        if (is_string($value)) {
            return $value;
        }
        if (is_bool($value)) {
            return $value ? 'true' : 'false';
        }
        if (is_int($value) || is_float($value)) {
            return (string) $value;
        }

        return (string) json_encode($value, JSON_UNESCAPED_SLASHES);
    }

    /** @return list<string> */
    private static function stringList(mixed $value): array
    {
        if (!is_array($value) || !array_is_list($value)) {
            return [];
        }

        return array_map(self::stringify(...), $value);
    }
}
