<?php

declare(strict_types=1);

namespace Drishti\WebSocket;

/** WebSocket product (channel) names and wire-format mapping. */
final class Products
{
    public const NEWS = 'news';
    public const BLOCK_DEALS = 'block_deals';
    public const ANNOUNCEMENTS = 'announcements';
    public const EARNINGS = 'earnings';
    public const CONCALLS = 'concalls';
    public const ALERTS = 'alerts';

    public const ALL = [
        self::NEWS,
        self::BLOCK_DEALS,
        self::ANNOUNCEMENTS,
        self::EARNINGS,
        self::CONCALLS,
        self::ALERTS,
    ];

    /**
     * Subscribe rejections that retrying on the same socket cannot fix (missing add-on, tier or
     * symbol limits, invalid categories). They fail fast instead of being retried in place; the
     * subscription is kept and retried on the next reconnect, with backoff.
     */
    public const PERMANENT_SUBSCRIBE_ERROR_CODES = ['forbidden', 'bad_request', 'unauthorized'];

    private function __construct()
    {
    }

    /** Server wire name → SDK product name (`block-deals` → `block_deals`). */
    public static function normalize(string $product): string
    {
        return $product === 'block-deals' ? self::BLOCK_DEALS : $product;
    }

    /** SDK product name → server wire name (`block_deals` → `block-deals`). */
    public static function toWire(string $product): string
    {
        return $product === self::BLOCK_DEALS ? 'block-deals' : $product;
    }

    public static function isKnown(string $product): bool
    {
        return in_array($product, self::ALL, true);
    }
}
