<?php

declare(strict_types=1);

namespace Drishti\Exception;

use RuntimeException;

/** Raised when the API returns a non-success HTTP status. */
final class DrishtiApiException extends RuntimeException
{
    public function __construct(private readonly int $statusCode, private readonly mixed $body)
    {
        $rendered = is_string($body) ? $body : json_encode($body, JSON_UNESCAPED_SLASHES);
        parent::__construct(sprintf('Drishti API error %d: %s', $statusCode, (string) $rendered));
    }

    public function getStatusCode(): int
    {
        return $this->statusCode;
    }

    /** Parsed JSON body (array), raw text, or null when the response was empty. */
    public function getBody(): mixed
    {
        return $this->body;
    }
}
