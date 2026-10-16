<?php

declare(strict_types=1);

namespace Drishti\Tests\Support;

use Psr\Log\AbstractLogger;
use Stringable;

final class RecordingLogger extends AbstractLogger
{
    /** @var list<array{level: string, message: string}> */
    public array $records = [];

    public function log($level, string|Stringable $message, array $context = []): void
    {
        $this->records[] = ['level' => is_string($level) ? $level : '', 'message' => (string) $message];
    }

    /** @return list<string> */
    public function messages(?string $level = null): array
    {
        $records = array_filter($this->records, static fn (array $r): bool => $level === null || $r['level'] === $level);

        return array_values(array_map(static fn (array $r): string => $r['message'], $records));
    }
}
