<?php

declare(strict_types=1);

namespace Drishti;

use InvalidArgumentException;

/** Request serialisation helpers shared by the client and batch workflows. */
final class Params
{
    private function __construct()
    {
    }

    /**
     * Drop null values and empty lists; join lists with commas (the API's list filter convention).
     *
     * @param array<string, scalar|list<scalar>|null> $params
     * @return array<string, scalar>
     */
    public static function query(array $params): array
    {
        $out = [];
        foreach ($params as $key => $value) {
            if ($value === null) {
                continue;
            }
            if (is_array($value)) {
                if ($value === []) {
                    continue;
                }
                $out[$key] = implode(',', array_map(static fn (mixed $item): string => (string) $item, $value));
                continue;
            }
            $out[$key] = $value;
        }

        return $out;
    }

    /**
     * Serialise batch summary input lines (each a POST /v1/daily-summary body plus `custom_id`) to JSONL.
     *
     * @param list<array<string, mixed>> $lines
     */
    public static function buildBatchInputJsonl(array $lines): string
    {
        if ($lines === []) {
            throw new InvalidArgumentException('At least one batch input line is required');
        }
        $encoded = array_map(
            static fn (array $line): string => json_encode($line, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES),
            $lines,
        );

        return implode("\n", $encoded) . "\n";
    }

    /**
     * Parse JSONL text from GET /v1/batch/jobs/{job_id}/results, skipping blank lines.
     *
     * @return list<array<string, mixed>>
     */
    public static function parseBatchResultJsonl(string $content): array
    {
        $results = [];
        foreach (preg_split('/\r?\n/', $content) ?: [] as $line) {
            if (trim($line) === '') {
                continue;
            }
            $parsed = json_decode($line, true, flags: JSON_THROW_ON_ERROR);
            // Only JSON objects are result lines; `{}` decodes to [] so check the raw text.
            if (is_array($parsed) && str_starts_with(ltrim($line), '{')) {
                /** @var array<string, mixed> $parsed */
                $results[] = $parsed;
            }
        }

        return $results;
    }
}
