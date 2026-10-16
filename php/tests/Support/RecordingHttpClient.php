<?php

declare(strict_types=1);

namespace Drishti\Tests\Support;

use GuzzleHttp\Psr7\Response;
use Psr\Http\Client\ClientInterface;
use Psr\Http\Message\RequestInterface;
use Psr\Http\Message\ResponseInterface;
use RuntimeException;

/**
 * PSR-18 client that records every request and replays scripted responses in order.
 *
 * @phpstan-type ResponseSpec array{status?: int, json?: mixed, text?: string, content_type?: string, network_error?: bool}
 */
final class RecordingHttpClient implements ClientInterface
{
    /** @var list<RequestInterface> */
    public array $requests = [];

    /** @param list<ResponseSpec> $responses */
    public function __construct(private readonly array $responses)
    {
    }

    public function sendRequest(RequestInterface $request): ResponseInterface
    {
        $this->requests[] = $request;
        $index = count($this->requests) - 1;
        if (!array_key_exists($index, $this->responses)) {
            throw new RuntimeException(sprintf(
                'unexpected extra request #%d: %s %s',
                $index + 1,
                $request->getMethod(),
                (string) $request->getUri(),
            ));
        }
        $spec = $this->responses[$index];
        if (($spec['network_error'] ?? false) === true) {
            throw new SimulatedNetworkException('simulated network error', $request);
        }
        $status = $spec['status'] ?? 200;
        if ($status === 204) {
            return new Response(204);
        }
        $body = array_key_exists('json', $spec)
            ? json_encode($spec['json'], JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES)
            : ($spec['text'] ?? '');

        return new Response($status, ['Content-Type' => $spec['content_type'] ?? 'application/json'], $body);
    }
}
