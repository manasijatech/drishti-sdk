<?php

declare(strict_types=1);

namespace Drishti\Tests;

use Drishti\DrishtiClient;
use Drishti\Exception\DrishtiApiException;
use Drishti\Params;
use Drishti\Types;
use Drishti\Tests\Support\RecordingHttpClient;
use Drishti\WebSocket\Products;
use Drishti\WebSocket\WebSocketMessages;
use InvalidArgumentException;
use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\TestCase;
use Psr\Http\Message\RequestInterface;
use Throwable;

/**
 * Runs the shared cross-SDK conformance fixtures in ../../conformance against the PHP SDK.
 * The Python and JS SDKs run the same fixtures; see conformance/README.md for the format.
 */
final class ConformanceTest extends TestCase
{
    private const CONFORMANCE_DIR = __DIR__ . '/../../conformance';
    private const TEST_API_KEY = 'test-key';
    private const TEST_BASE_URL = 'https://api.test';

    /** @return array<string, mixed> */
    private static function readJson(string $relativePath): array
    {
        $decoded = json_decode(
            (string) file_get_contents(self::CONFORMANCE_DIR . '/' . $relativePath),
            true,
            flags: JSON_THROW_ON_ERROR,
        );
        self::assertIsArray($decoded);

        /** @var array<string, mixed> $decoded */
        return $decoded;
    }

    /** @return iterable<string, array{array<string, mixed>}> */
    private static function casesIn(string $subdir): iterable
    {
        $files = glob(self::CONFORMANCE_DIR . '/' . $subdir . '/*.json') ?: [];
        sort($files);
        foreach ($files as $file) {
            /** @var list<array<string, mixed>> $cases */
            $cases = self::readJson($subdir . '/' . basename($file))['cases'];
            foreach ($cases as $case) {
                yield self::str($case['id']) => [$case];
            }
        }
    }

    /** @return iterable<string, array{array<string, mixed>}> */
    public static function restCases(): iterable
    {
        return self::casesIn('rest');
    }

    /** @return iterable<string, array{array<string, mixed>}> */
    public static function helperCases(): iterable
    {
        return self::casesIn('helpers');
    }

    /** @return iterable<string, array{array<string, mixed>}> */
    public static function wsCases(string $name): iterable
    {
        /** @var list<array<string, mixed>> $cases */
        $cases = self::readJson("websocket/{$name}.json")['cases'];
        foreach ($cases as $case) {
            yield self::str($case['id']) => [$case];
        }
    }

    /** @return iterable<string, array{array<string, mixed>}> */
    public static function wsParseCases(): iterable
    {
        return self::wsCases('parse');
    }

    /** @return iterable<string, array{array<string, mixed>}> */
    public static function wsSubscribeCases(): iterable
    {
        return self::wsCases('subscribe');
    }

    /** @return iterable<string, array{array<string, mixed>}> */
    public static function wsUrlCases(): iterable
    {
        return self::wsCases('url');
    }

    // --- REST ------------------------------------------------------------------------------

    private static function camel(string $name): string
    {
        return lcfirst(str_replace('_', '', ucwords($name, '_')));
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('restCases')]
    public function testRestConformance(array $case): void
    {
        /** @var list<array{status?: int, json?: mixed, text?: string, content_type?: string, network_error?: bool}> $responses */
        $responses = $case['responses'] ?? [$case['response'] ?? ['status' => 200, 'json' => []]];
        $recorder = new RecordingHttpClient($responses);
        /** @var array{api_key?: string, base_url?: string, headers?: array<string, string>, max_retries?: int} $clientConfig */
        $clientConfig = $case['client'] ?? [];
        /** @var array{method: string, args?: array<string, mixed>, positional?: list<mixed>} $call */
        $call = $case['call'];

        $result = null;
        $error = null;
        try {
            $client = new DrishtiClient(
                apiKey: $clientConfig['api_key'] ?? self::TEST_API_KEY,
                baseUrl: $clientConfig['base_url'] ?? self::TEST_BASE_URL,
                headers: $clientConfig['headers'] ?? [],
                httpClient: $recorder,
                maxRetries: $clientConfig['max_retries'] ?? 2,
                initialDelayMs: 0,
                maxDelayMs: 0,
            );
            $named = [];
            foreach ($call['args'] ?? [] as $key => $value) {
                $named[self::camel($key)] = $value;
            }
            $method = self::camel($call['method']);
            self::assertTrue(method_exists($client, $method), "DrishtiClient::{$method} is missing");
            $result = $client->{$method}(...($call['positional'] ?? []), ...$named);
        } catch (Throwable $caught) {
            $error = $caught;
        }

        /** @var list<array<string, mixed>>|null $expectedRequests */
        $expectedRequests = $case['expect_requests']
            ?? (isset($case['expect_request']) ? [$case['expect_request']] : null);
        if ($expectedRequests !== null) {
            self::assertCount(count($expectedRequests), $recorder->requests, 'request count');
            foreach ($expectedRequests as $i => $expected) {
                self::assertRequest($recorder->requests[$i], $expected);
            }
        }
        if (isset($case['expect_request_count'])) {
            self::assertCount(self::int($case['expect_request_count']), $recorder->requests, 'request count');
        }

        if (isset($case['expect_error'])) {
            /** @var array{type: string, status?: int, body?: mixed} $expectedError */
            $expectedError = $case['expect_error'];
            self::assertNotNull($error, "expected {$expectedError['type']}, got a result");
            if ($expectedError['type'] === 'api_error') {
                self::assertInstanceOf(DrishtiApiException::class, $error, (string) $error);
                self::assertSame($expectedError['status'] ?? null, $error->getStatusCode());
                self::assertSame(self::canonical($expectedError['body'] ?? null), self::canonical($error->getBody()));
            } else {
                self::assertInstanceOf(InvalidArgumentException::class, $error, (string) $error);
            }

            return;
        }
        if ($error !== null) {
            throw $error;
        }
        if (array_key_exists('expect_result', $case)) {
            self::assertSame(self::canonical($case['expect_result']), self::canonical($result));
        }
    }

    /** @param array<string, mixed> $expected */
    private static function assertRequest(RequestInterface $request, array $expected): void
    {
        self::assertSame($expected['method'], $request->getMethod());
        self::assertSame($expected['path'], $request->getUri()->getPath());
        self::assertSame(
            self::canonical($expected['query'] ?? []),
            self::canonical(self::queryMultimap($request->getUri()->getQuery())),
        );
        /** @var array<string, string> $headers */
        $headers = $expected['headers'] ?? [];
        foreach ($headers as $name => $value) {
            self::assertSame($value, $request->getHeaderLine($name), "header {$name}");
        }
        if (isset($expected['json_body'])) {
            self::assertSame(
                self::canonical($expected['json_body']),
                self::canonical(json_decode((string) $request->getBody(), true, flags: JSON_THROW_ON_ERROR)),
            );
        }
        if (isset($expected['multipart'])) {
            self::assertSame(self::canonical($expected['multipart']), self::canonical(self::multipart($request)));
        }
    }

    /** @return array<string, list<string>> */
    private static function queryMultimap(string $query): array
    {
        $out = [];
        if ($query === '') {
            return $out;
        }
        foreach (explode('&', $query) as $pair) {
            [$key, $value] = array_pad(explode('=', $pair, 2), 2, '');
            $out[urldecode($key)][] = urldecode($value);
        }

        return $out;
    }

    /** @return array{fields: array<string, string>, files: array<string, array{filename: string, content: string}>} */
    private static function multipart(RequestInterface $request): array
    {
        $contentType = $request->getHeaderLine('Content-Type');
        self::assertMatchesRegularExpression('/^multipart\/form-data; boundary=/', $contentType);
        $boundary = trim(substr($contentType, strpos($contentType, 'boundary=') + 9), '"');
        $fields = [];
        $files = [];
        $parts = explode("--{$boundary}", (string) $request->getBody());
        foreach (array_slice($parts, 1, -1) as $part) {
            [$rawHeaders, $content] = explode("\r\n\r\n", ltrim($part, "\r\n"), 2);
            $content = substr($content, 0, -2);
            if (preg_match('/name="([^"]*)"/', $rawHeaders, $name) !== 1) {
                self::fail("multipart part without a name: {$rawHeaders}");
            }
            if (preg_match('/filename="([^"]*)"/', $rawHeaders, $filename) === 1) {
                $files[$name[1]] = ['filename' => $filename[1], 'content' => $content];
            } else {
                $fields[$name[1]] = $content;
            }
        }

        return ['fields' => $fields, 'files' => $files];
    }

    private static function str(mixed $value): string
    {
        self::assertIsString($value);

        return $value;
    }

    private static function int(mixed $value): int
    {
        self::assertIsInt($value);

        return $value;
    }

    /** Sort associative arrays by key, recursively, so comparisons ignore key order. */
    private static function canonical(mixed $value): mixed
    {
        if (!is_array($value)) {
            return $value;
        }
        $value = array_map(self::canonical(...), $value);
        if (!array_is_list($value)) {
            ksort($value);
        }

        return $value;
    }

    // --- Helpers ---------------------------------------------------------------------------

    /** @param array<string, mixed> $case */
    #[DataProvider('helperCases')]
    public function testHelperConformance(array $case): void
    {
        if ($case['function'] === 'build_batch_input_jsonl') {
            /** @var list<array<string, mixed>> $input */
            $input = $case['input'];
            if (isset($case['expect_error'])) {
                $this->expectException(InvalidArgumentException::class);
                Params::buildBatchInputJsonl($input);

                return;
            }
            $text = Params::buildBatchInputJsonl($input);
            self::assertStringEndsWith("\n", $text);
            $lines = array_map(
                static fn (string $line): mixed => json_decode($line, true, flags: JSON_THROW_ON_ERROR),
                explode("\n", rtrim($text, "\n")),
            );
            self::assertSame(self::canonical($case['expect_lines']), self::canonical($lines));
        } elseif ($case['function'] === 'parse_batch_result_jsonl') {
            self::assertSame(
                self::canonical($case['expect_result']),
                self::canonical(Params::parseBatchResultJsonl(self::str($case['input']))),
            );
        } else {
            self::fail('unknown helper ' . self::str($case['function']));
        }
    }

    // --- WebSocket -------------------------------------------------------------------------

    /** @param array<string, mixed> $case */
    #[DataProvider('wsParseCases')]
    public function testWebSocketParseConformance(array $case): void
    {
        self::assertSame(
            self::canonical($case['expect']),
            self::canonical(WebSocketMessages::parse(self::str($case['raw']))->toArray()),
        );
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('wsSubscribeCases')]
    public function testWebSocketSubscribeConformance(array $case): void
    {
        /** @var array{product: string, symbols?: list<string>, categories?: list<string>, detailed?: bool} $options */
        $options = $case['options'];
        self::assertSame(
            self::canonical($case['expect']),
            self::canonical(WebSocketMessages::buildSubscribeMessage(...$options)),
        );
    }

    /** @param array<string, mixed> $case */
    #[DataProvider('wsUrlCases')]
    public function testWebSocketUrlConformance(array $case): void
    {
        $apiKey = isset($case['api_key']) ? self::str($case['api_key']) : null;
        self::assertSame($case['expect'], WebSocketMessages::buildWebSocketUrl(self::str($case['base_url']), $apiKey));
    }

    // --- Constants -------------------------------------------------------------------------

    public function testConstantsConformance(): void
    {
        $expected = self::readJson('constants.json');
        $permanent = Products::PERMANENT_SUBSCRIBE_ERROR_CODES;
        sort($permanent);

        self::assertSame($expected['default_base_url'], DrishtiClient::DEFAULT_BASE_URL);
        self::assertSame($expected['ws_products'], Products::ALL);
        self::assertSame($expected['permanent_subscribe_error_codes'], $permanent);
        self::assertSame($expected['alert_types'], Types::ALERT_TYPES);
        self::assertSame($expected['batch_job_statuses'], Types::BATCH_JOB_STATUSES);
        self::assertSame($expected['batch_job_terminal_statuses'], Types::BATCH_JOB_TERMINAL_STATUSES);
    }
}
