<?php

declare(strict_types=1);

namespace Drishti\WebSocket\Event;

/** Server acknowledgement of a subscribe request. */
final readonly class SubscribedEvent implements WebSocketEvent
{
    /**
     * @param list<string> $symbols
     * @param list<string> $categories
     */
    public function __construct(
        public string $product,
        public string $tier = '',
        public bool $fullFeed = false,
        public array $symbols = [],
        public array $categories = [],
        public bool $detailed = true,
    ) {
    }

    public function kind(): string
    {
        return 'subscribed';
    }

    public function toArray(): array
    {
        return [
            'kind' => 'subscribed',
            'product' => $this->product,
            'tier' => $this->tier,
            'full_feed' => $this->fullFeed,
            'symbols' => $this->symbols,
            'categories' => $this->categories,
            'detailed' => $this->detailed,
        ];
    }
}
