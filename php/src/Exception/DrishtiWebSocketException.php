<?php

declare(strict_types=1);

namespace Drishti\Exception;

use RuntimeException;
use Throwable;

/** Raised when the WebSocket connection fails, closes unexpectedly, or a subscribe is rejected. */
final class DrishtiWebSocketException extends RuntimeException
{
    public function __construct(string $message, private readonly ?string $errorCode = null, ?Throwable $previous = null)
    {
        parent::__construct($message, 0, $previous);
    }

    /** Server error code such as "forbidden", when the server sent one. */
    public function getErrorCode(): ?string
    {
        return $this->errorCode;
    }
}
