<?php

declare(strict_types=1);

namespace Drishti;

use Drishti\Exception\DrishtiApiException;
use Drishti\WebSocket\DrishtiWebSocketSession;
use GuzzleHttp\Client as GuzzleClient;
use GuzzleHttp\Psr7\HttpFactory;
use GuzzleHttp\Psr7\MultipartStream;
use InvalidArgumentException;
use Psr\Http\Client\ClientInterface;
use Psr\Http\Client\NetworkExceptionInterface;
use Psr\Http\Message\RequestFactoryInterface;
use Psr\Http\Message\ResponseInterface;
use Psr\Http\Message\StreamFactoryInterface;
use Psr\Http\Message\StreamInterface;
use RuntimeException;

/**
 * Sync HTTP client for Drishti API v1.
 *
 * JSON responses are returned as associative arrays, other responses (CSV, PDF, JSONL) as strings,
 * and empty responses as null. Non-2xx responses throw {@see DrishtiApiException}.
 */
final class DrishtiClient
{
    public const DEFAULT_BASE_URL = 'https://developers.manasija.in';
    public const DEFAULT_RETRY_ON_STATUSES = [408, 409, 425, 429, 500, 502, 503, 504];

    private readonly string $baseUrl;
    private readonly ClientInterface $httpClient;
    private readonly RequestFactoryInterface $requestFactory;
    private readonly StreamFactoryInterface $streamFactory;

    /**
     * @param array<string, string> $headers Extra headers sent with every request (cannot override X-API-Key).
     * @param ClientInterface|null $httpClient Any PSR-18 client; defaults to Guzzle.
     * @param list<int> $retryOnStatuses
     */
    public function __construct(
        private readonly string $apiKey,
        string $baseUrl = self::DEFAULT_BASE_URL,
        private readonly array $headers = [],
        ?ClientInterface $httpClient = null,
        ?RequestFactoryInterface $requestFactory = null,
        ?StreamFactoryInterface $streamFactory = null,
        private readonly int $maxRetries = 2,
        private readonly int $initialDelayMs = 300,
        private readonly int $maxDelayMs = 5000,
        private readonly float $multiplier = 2.0,
        private readonly array $retryOnStatuses = self::DEFAULT_RETRY_ON_STATUSES,
        float $timeout = 60.0,
    ) {
        if (trim($apiKey) === '') {
            throw new InvalidArgumentException('DrishtiClient requires a non-empty apiKey');
        }
        $this->baseUrl = rtrim($baseUrl, '/');
        $this->httpClient = $httpClient ?? new GuzzleClient(['timeout' => $timeout]);
        $factory = new HttpFactory();
        $this->requestFactory = $requestFactory ?? $factory;
        $this->streamFactory = $streamFactory ?? $factory;
    }

    // --- Low-level requests ----------------------------------------------------------------

    /**
     * Send a request to the API and return the parsed response.
     *
     * @param array<string, scalar|list<scalar>|null> $query
     * @param array<string, string|int> $pathParams Replaces `{name}` or `:name` in $path (percent-encoded).
     * @param array<string, string> $headers
     * @param mixed $json JSON-encoded request body.
     * @param StreamInterface|null $body Raw body (e.g. multipart); takes precedence over $json.
     */
    public function request(
        string $method,
        string $path,
        array $query = [],
        mixed $json = null,
        array $pathParams = [],
        array $headers = [],
        ?StreamInterface $body = null,
    ): mixed {
        $url = $this->baseUrl . '/' . ltrim($this->buildPath($path, $pathParams), '/');
        $queryString = $this->buildQuery($query);
        if ($queryString !== '') {
            $url .= '?' . $queryString;
        }
        $requestHeaders = ['Accept' => 'application/json', ...$this->headers, ...$headers];
        if ($body === null && $json !== null) {
            $body = $this->streamFactory->createStream(json_encode($json, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES));
            $requestHeaders['Content-Type'] = 'application/json';
        }

        $attempts = max(0, $this->maxRetries) + 1;
        for ($attempt = 1; ; $attempt++) {
            $request = $this->requestFactory->createRequest(strtoupper($method), $url);
            foreach ($requestHeaders as $name => $value) {
                $request = $request->withHeader($name, $value);
            }
            $request = $request->withHeader('X-API-Key', $this->apiKey);
            if ($body !== null) {
                if ($body->isSeekable()) {
                    $body->rewind();
                }
                $request = $request->withBody($body);
            }
            try {
                $response = $this->httpClient->sendRequest($request);
            } catch (NetworkExceptionInterface $exception) {
                if ($attempt >= $attempts) {
                    throw $exception;
                }
                $this->sleepBeforeRetry($attempt);
                continue;
            }
            $status = $response->getStatusCode();
            if ($status >= 400 && $attempt < $attempts && in_array($status, $this->retryOnStatuses, true)) {
                $this->sleepBeforeRetry($attempt);
                continue;
            }

            return $this->parseResponse($response);
        }
    }

    /** @param array<string, scalar|list<scalar>|null> $query */
    public function get(string $path, array $query = []): mixed
    {
        return $this->request('GET', $path, query: $query);
    }

    /** @param array<string, scalar|list<scalar>|null> $query */
    public function post(string $path, mixed $json = null, array $query = []): mixed
    {
        return $this->request('POST', $path, query: $query, json: $json);
    }

    /** @param array<string, scalar|list<scalar>|null> $query */
    public function put(string $path, mixed $json = null, array $query = []): mixed
    {
        return $this->request('PUT', $path, query: $query, json: $json);
    }

    /** @param array<string, scalar|list<scalar>|null> $query */
    public function patch(string $path, mixed $json = null, array $query = []): mixed
    {
        return $this->request('PATCH', $path, query: $query, json: $json);
    }

    /** @param array<string, scalar|list<scalar>|null> $query */
    public function delete(string $path, array $query = []): mixed
    {
        return $this->request('DELETE', $path, query: $query);
    }

    // --- News, block deals, symbols --------------------------------------------------------

    /**
     * GET /v1/news.
     *
     * @param list<string>|null $symbols
     * @param list<string>|null $scripCodes
     * @param 'positive'|'negative'|'neutral'|null $sentiment
     * @param list<string>|null $sectors Case-insensitive exact sector match, such as Bank or IT.
     * @return array<string, mixed>|null
     */
    public function getNews(
        ?array $symbols = null,
        ?array $scripCodes = null,
        ?string $sentiment = null,
        ?string $from = null,
        ?string $to = null,
        ?int $page = null,
        ?int $limit = null,
        ?array $sectors = null,
    ): ?array {
        return $this->getArray('/v1/news', [
            'symbols' => $symbols,
            'scrip_codes' => $scripCodes,
            'sectors' => $sectors,
            'sentiment' => $sentiment,
            'from' => $from,
            'to' => $to,
            'page' => $page,
            'limit' => $limit,
        ]);
    }

    /**
     * GET /v1/block-deals.
     *
     * @param list<string>|null $symbols
     * @return array<string, mixed>|null
     */
    public function getBlockDeals(
        ?array $symbols = null,
        ?string $exchange = null,
        ?string $from = null,
        ?string $to = null,
        ?int $page = null,
        ?int $limit = null,
    ): ?array {
        return $this->getArray('/v1/block-deals', [
            'symbols' => $symbols,
            'exchange' => $exchange,
            'from' => $from,
            'to' => $to,
            'page' => $page,
            'limit' => $limit,
        ]);
    }

    /**
     * GET /v1/symbols/metadata.
     *
     * @param list<string>|null $symbols
     * @param list<string>|null $scripCodes
     * @return array<string, mixed>|null
     */
    public function getSymbolsMetadata(?array $symbols = null, ?array $scripCodes = null): ?array
    {
        return $this->getArray('/v1/symbols/metadata', ['symbols' => $symbols, 'scrip_codes' => $scripCodes]);
    }

    /**
     * GET /v1/symbols. The API returns CSV text by default, or an array with $format = 'json'.
     *
     * @param 'csv'|'json'|null $format
     * @return string|array<string, mixed>
     */
    public function getSymbols(?string $format = null): string|array
    {
        $result = $this->get('/v1/symbols', Params::query(['format' => $format]));

        return is_array($result) ? (self::assoc($result) ?? []) : (is_string($result) ? $result : '');
    }

    // --- Announcements ---------------------------------------------------------------------

    /** @return array<string, mixed>|null */
    public function getAnnouncementsCategories(): ?array
    {
        return $this->getArray('/v1/announcements/categories');
    }

    /**
     * GET /v1/announcements.
     *
     * @param list<string>|null $symbols
     * @param list<string>|null $scripCodes
     * @param list<string>|null $categories
     * @return array<string, mixed>|null
     */
    public function getAnnouncements(
        ?array $symbols = null,
        ?array $scripCodes = null,
        ?array $categories = null,
        ?string $from = null,
        ?string $to = null,
        ?bool $detailed = null,
        ?int $page = null,
        ?int $limit = null,
    ): ?array {
        return $this->getArray('/v1/announcements', [
            'symbols' => $symbols,
            'scrip_codes' => $scripCodes,
            'categories' => $categories,
            'from' => $from,
            'to' => $to,
            'detailed' => $detailed,
            'page' => $page,
            'limit' => $limit,
        ]);
    }

    /**
     * GET /v1/announcements/attachments.
     *
     * @param list<string> $ids
     * @return array<string, mixed>|null
     */
    public function getAnnouncementsAttachments(array $ids): ?array
    {
        return $this->getArray('/v1/announcements/attachments', ['ids' => $ids]);
    }

    /** GET /v1/announcements/citations/{announcement_id}/pdf. Returns the PDF bytes. */
    public function getAnnouncementCitationPdf(string $announcementId): string
    {
        return $this->getString('/v1/announcements/citations/{announcement_id}/pdf', ['announcement_id' => $announcementId]);
    }

    // --- Daily summary ---------------------------------------------------------------------

    /**
     * POST /v1/daily-summary/. The body is sent as given; the API applies its own defaults.
     *
     * @param array<string, mixed> $body e.g. ['symbols' => ['TCS'], 'mode' => 'news_context', 'context_window_days' => 7]
     * @param 'none'|null $contentRetention Sent as X-Alpha-Content-Retention.
     * @return array<string, mixed>|null
     */
    public function postDailySummary(array $body, ?string $contentRetention = null): ?array
    {
        return self::assoc($this->request(
            'POST',
            '/v1/daily-summary/',
            json: $body === [] ? new \stdClass() : $body,
            headers: self::contentRetentionHeaders($contentRetention),
        ));
    }

    // --- Earnings --------------------------------------------------------------------------

    /**
     * GET /v1/earnings/index.
     *
     * @param list<string>|null $symbols
     * @param list<string>|null $scripCodes
     * @return array<string, mixed>|null
     */
    public function getEarningsIndex(
        ?array $symbols = null,
        ?array $scripCodes = null,
        ?string $from = null,
        ?string $to = null,
        ?string $quarter = null,
        ?int $page = null,
        ?int $limit = null,
    ): ?array {
        return $this->getArray('/v1/earnings/index', [
            'symbols' => $symbols,
            'scrip_codes' => $scripCodes,
            'from' => $from,
            'to' => $to,
            'quarter' => $quarter,
            'page' => $page,
            'limit' => $limit,
        ]);
    }

    /**
     * GET /v1/earnings. Items with only `earnings_table_extraction` also get `earnings_table`.
     *
     * @param list<string>|null $ids
     * @param list<string>|null $symbols
     * @param list<string>|null $scripCodes
     * @return array<string, mixed>|null
     */
    public function getEarnings(
        ?array $ids = null,
        ?array $symbols = null,
        ?array $scripCodes = null,
        ?string $from = null,
        ?string $to = null,
        ?string $quarter = null,
        ?bool $detailed = null,
        ?int $page = null,
        ?int $limit = null,
    ): ?array {
        $response = $this->getArray('/v1/earnings', [
            'ids' => $ids,
            'symbols' => $symbols,
            'scrip_codes' => $scripCodes,
            'from' => $from,
            'to' => $to,
            'quarter' => $quarter,
            'detailed' => $detailed,
            'page' => $page,
            'limit' => $limit,
        ]);
        if (isset($response['data']) && is_array($response['data'])) {
            $response['data'] = array_map(self::normalizeEarningsItem(...), $response['data']);
        }

        return $response;
    }

    /**
     * GET /v1/earnings/detail.
     *
     * @return array<string, mixed>|null
     */
    public function getEarningsDetail(
        string $quarter,
        ?string $symbol = null,
        ?string $scripCode = null,
        ?bool $detailed = null,
    ): ?array {
        $response = $this->getArray('/v1/earnings/detail', [
            'quarter' => $quarter,
            'symbol' => $symbol,
            'scrip_code' => $scripCode,
            'detailed' => $detailed,
        ]);

        return self::assoc(self::normalizeEarningsItem($response));
    }

    /**
     * GET /v1/earnings/attachments.
     *
     * @param list<string> $ids
     * @return array<string, mixed>|null
     */
    public function getEarningsAttachments(array $ids): ?array
    {
        return $this->getArray('/v1/earnings/attachments', ['ids' => $ids]);
    }

    /** GET /v1/earnings/citations/{earnings_id}/pdf. Returns the PDF bytes. */
    public function getEarningsCitationPdf(string $earningsId, ?int $page = null): string
    {
        return $this->getString(
            '/v1/earnings/citations/{earnings_id}/pdf',
            ['earnings_id' => $earningsId],
            ['page' => $page],
        );
    }

    /** GET /v1/earnings/citations/{earnings_id}/page/{page}. */
    public function getEarningsCitationPage(string $earningsId, int $page): string
    {
        return $this->getString(
            '/v1/earnings/citations/{earnings_id}/page/{page}',
            ['earnings_id' => $earningsId, 'page' => $page],
        );
    }

    /**
     * GET /v1/earnings/upcoming.
     *
     * @param list<string>|null $symbols
     * @param list<string>|null $scripCodes
     * @return array<string, mixed>|null
     */
    public function getUpcomingEarnings(
        ?array $symbols = null,
        ?array $scripCodes = null,
        ?int $page = null,
        ?int $limit = null,
    ): ?array {
        return $this->getArray('/v1/earnings/upcoming', [
            'symbols' => $symbols,
            'scrip_codes' => $scripCodes,
            'page' => $page,
            'limit' => $limit,
        ]);
    }

    // --- Concalls --------------------------------------------------------------------------

    /**
     * GET /v1/concalls.
     *
     * @param list<string>|null $symbols
     * @param list<string>|null $scripCodes
     * @return array<string, mixed>|null
     */
    public function getConcalls(
        ?array $symbols = null,
        ?array $scripCodes = null,
        ?string $from = null,
        ?string $to = null,
        ?bool $detailed = null,
        ?int $page = null,
        ?int $limit = null,
    ): ?array {
        return $this->getArray('/v1/concalls', [
            'symbols' => $symbols,
            'scrip_codes' => $scripCodes,
            'from' => $from,
            'to' => $to,
            'detailed' => $detailed,
            'page' => $page,
            'limit' => $limit,
        ]);
    }

    /** GET /v1/concalls/citations/{concall_id}/pdf. Returns the PDF bytes. */
    public function getConcallCitationPdf(string $concallId): string
    {
        return $this->getString('/v1/concalls/citations/{concall_id}/pdf', ['concall_id' => $concallId]);
    }

    /**
     * GET /v1/concalls/index.
     *
     * @param list<string>|null $symbols
     * @param list<string>|null $scripCodes
     * @return array<string, mixed>|null
     */
    public function getConcallsIndex(
        ?array $symbols = null,
        ?array $scripCodes = null,
        ?string $from = null,
        ?string $to = null,
        ?string $quarter = null,
        ?int $page = null,
        ?int $limit = null,
    ): ?array {
        return $this->getArray('/v1/concalls/index', [
            'symbols' => $symbols,
            'scrip_codes' => $scripCodes,
            'from' => $from,
            'to' => $to,
            'quarter' => $quarter,
            'page' => $page,
            'limit' => $limit,
        ]);
    }

    /**
     * GET /v1/concalls/upcoming.
     *
     * @param list<string>|null $symbols
     * @param list<string>|null $scripCodes
     * @return array<string, mixed>|null
     */
    public function getUpcomingConcalls(
        ?array $symbols = null,
        ?array $scripCodes = null,
        ?bool $detailed = null,
        ?int $page = null,
        ?int $limit = null,
    ): ?array {
        return $this->getArray('/v1/concalls/upcoming', [
            'symbols' => $symbols,
            'scrip_codes' => $scripCodes,
            'detailed' => $detailed,
            'page' => $page,
            'limit' => $limit,
        ]);
    }

    /**
     * GET /v1/concalls/detail.
     *
     * @return array<string, mixed>|null
     */
    public function getConcallsDetail(
        string $quarter,
        ?string $symbol = null,
        ?string $scripCode = null,
        ?bool $detailed = null,
    ): ?array {
        return $this->getArray('/v1/concalls/detail', [
            'quarter' => $quarter,
            'symbol' => $symbol,
            'scrip_code' => $scripCode,
            'detailed' => $detailed,
        ]);
    }

    /**
     * GET /v1/concalls/transcript.
     *
     * @return array<string, mixed>|null
     */
    public function getConcallsTranscript(string $quarter, ?string $symbol = null, ?string $scripCode = null): ?array
    {
        return $this->getArray('/v1/concalls/transcript', [
            'quarter' => $quarter,
            'symbol' => $symbol,
            'scrip_code' => $scripCode,
        ]);
    }

    /**
     * POST /v1/concalls/transcripts.
     *
     * @param list<array{symbol: string, quarter: string}> $items
     * @return array<string, mixed>|null
     */
    public function postConcallsTranscripts(array $items): ?array
    {
        return self::assoc($this->post('/v1/concalls/transcripts', ['items' => $items]));
    }

    // --- Alerts ----------------------------------------------------------------------------

    /**
     * GET /v1/alerts.
     *
     * @param list<string>|null $symbols
     * @param list<string>|null $scripCodes
     * @param list<string>|null $type Alert types, see {@see Types::ALERT_TYPES}.
     * @return array<string, mixed>|null
     */
    public function getAlerts(
        ?array $symbols = null,
        ?array $scripCodes = null,
        ?array $type = null,
        ?string $from = null,
        ?string $to = null,
        ?bool $important = null,
        ?int $page = null,
        ?int $limit = null,
    ): ?array {
        return $this->getArray('/v1/alerts', [
            'symbols' => $symbols,
            'scrip_codes' => $scripCodes,
            'type' => $type,
            'from' => $from,
            'to' => $to,
            'important' => $important,
            'page' => $page,
            'limit' => $limit,
        ]);
    }

    // --- Account ---------------------------------------------------------------------------

    /** @return array<string, mixed>|null */
    public function getAccount(): ?array
    {
        return $this->getArray('/v1/account');
    }

    /** @return array<string, mixed>|null */
    public function getAccountLimits(): ?array
    {
        return $this->getArray('/v1/account/limits');
    }

    /** @return array<string, mixed>|null */
    public function getAccountUsage(): ?array
    {
        return $this->getArray('/v1/account/usage');
    }

    /** @return array<string, mixed>|null */
    public function getAccountLedger(?int $limit = null): ?array
    {
        return $this->getArray('/v1/account/ledger', ['limit' => $limit]);
    }

    // --- Batch jobs ------------------------------------------------------------------------

    /**
     * Alias of {@see postBatchJobsFile()}.
     *
     * @param 'none'|null $contentRetention
     * @return array<string, mixed>|null
     */
    public function postBatchJobs(
        string $fileName,
        string $fileContent,
        ?string $displayName = null,
        ?string $metadata = null,
        ?string $contentRetention = null,
    ): ?array {
        return $this->postBatchJobsFile($fileName, $fileContent, $displayName, $metadata, $contentRetention);
    }

    /**
     * POST /v1/batch/jobs with a JSONL input file (see {@see Params::buildBatchInputJsonl()}).
     *
     * @param 'none'|null $contentRetention
     * @return array<string, mixed>|null
     */
    public function postBatchJobsFile(
        string $fileName,
        string $fileContent,
        ?string $displayName = null,
        ?string $metadata = null,
        ?string $contentRetention = null,
    ): ?array {
        $parts = [[
            'name' => 'file',
            'contents' => $fileContent,
            'filename' => $fileName,
            'headers' => ['Content-Type' => 'application/jsonl'],
        ]];
        if ($displayName !== null) {
            $parts[] = ['name' => 'display_name', 'contents' => $displayName];
        }
        if ($metadata !== null) {
            $parts[] = ['name' => 'metadata', 'contents' => $metadata];
        }
        $body = new MultipartStream($parts);

        return self::assoc($this->request(
            'POST',
            '/v1/batch/jobs',
            headers: [
                'Content-Type' => 'multipart/form-data; boundary=' . $body->getBoundary(),
                ...self::contentRetentionHeaders($contentRetention),
            ],
            body: $body,
        ));
    }

    /** @return array<string, mixed>|null */
    public function getBatchJobs(?int $limit = null): ?array
    {
        return $this->getArray('/v1/batch/jobs', ['limit' => $limit]);
    }

    /** @return array<string, mixed>|null */
    public function getBatchJobsJobId(string|int $jobId): ?array
    {
        return self::assoc($this->request('GET', '/v1/batch/jobs/{job_id}', pathParams: ['job_id' => $jobId]));
    }

    /** @return array<string, mixed>|null */
    public function deleteBatchJobsJobId(string|int $jobId): ?array
    {
        return self::assoc($this->request('DELETE', '/v1/batch/jobs/{job_id}', pathParams: ['job_id' => $jobId]));
    }

    /** GET /v1/batch/jobs/{job_id}/results as raw JSONL text. */
    public function getBatchJobsJobIdResults(string|int $jobId): string
    {
        return $this->getString('/v1/batch/jobs/{job_id}/results', ['job_id' => $jobId]);
    }

    /** @return list<array<string, mixed>> */
    public function getBatchJobsJobIdResultsParsed(string|int $jobId): array
    {
        return Params::parseBatchResultJsonl($this->getBatchJobsJobIdResults($jobId));
    }

    /**
     * Poll a batch job until it reaches a terminal status.
     *
     * @param list<string> $terminalStatuses
     * @return array<string, mixed>
     * @throws RuntimeException When $timeoutMs elapses first.
     */
    public function waitForBatchJobCompletion(
        string|int $jobId,
        int $pollIntervalMs = 2000,
        int $timeoutMs = 300_000,
        array $terminalStatuses = Types::BATCH_JOB_TERMINAL_STATUSES,
    ): array {
        $terminal = array_map(strtolower(...), $terminalStatuses);
        $startedAt = hrtime(true);
        while (true) {
            $job = $this->getBatchJobsJobId($jobId) ?? [];
            $status = strtolower(is_string($job['status'] ?? null) ? $job['status'] : '');
            if (in_array($status, $terminal, true)) {
                return $job;
            }
            if ((hrtime(true) - $startedAt) / 1_000_000 >= $timeoutMs) {
                throw new RuntimeException(sprintf('Timed out waiting for batch job %s to complete', $jobId));
            }
            if ($pollIntervalMs > 0) {
                usleep($pollIntervalMs * 1000);
            }
        }
    }

    /**
     * Submit a batch job file and wait for it to finish.
     *
     * @param 'none'|null $contentRetention
     * @param list<string> $terminalStatuses
     * @return array<string, mixed>
     */
    public function submitBatchJobAndWait(
        string $fileName,
        string $fileContent,
        ?string $displayName = null,
        ?string $metadata = null,
        ?string $contentRetention = null,
        int $pollIntervalMs = 2000,
        int $timeoutMs = 300_000,
        array $terminalStatuses = Types::BATCH_JOB_TERMINAL_STATUSES,
    ): array {
        $job = $this->postBatchJobsFile($fileName, $fileContent, $displayName, $metadata, $contentRetention);
        $jobId = $job['id'] ?? null;
        if (!is_string($jobId) && !is_int($jobId)) {
            throw new RuntimeException('Batch job response did not include an id');
        }

        return $this->waitForBatchJobCompletion($jobId, $pollIntervalMs, $timeoutMs, $terminalStatuses);
    }

    // --- WebSocket -------------------------------------------------------------------------

    /**
     * Create a WebSocket session that shares this client's API key, base URL and headers.
     *
     * Accepts the same named options as {@see DrishtiWebSocketSession::__construct()}.
     */
    public function websocket(mixed ...$options): DrishtiWebSocketSession
    {
        return new DrishtiWebSocketSession(
            // @phpstan-ignore argument.type (named options are forwarded verbatim to the session)
            ...[
                'apiKey' => $this->apiKey,
                'baseUrl' => $this->baseUrl,
                'headers' => $this->headers,
                ...$options,
            ],
        );
    }

    // --- Internals -------------------------------------------------------------------------

    /**
     * @param array<string, scalar|list<scalar>|null> $params
     * @return array<string, mixed>|null
     */
    private function getArray(string $path, array $params = []): ?array
    {
        return self::assoc($this->get($path, Params::query($params)));
    }

    /**
     * @param array<string, string|int> $pathParams
     * @param array<string, scalar|list<scalar>|null> $params
     */
    private function getString(string $path, array $pathParams = [], array $params = []): string
    {
        $result = $this->request('GET', $path, query: Params::query($params), pathParams: $pathParams);
        if (is_array($result)) {
            return json_encode($result, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES);
        }

        return is_string($result) ? $result : '';
    }

    /** @param array<string, string|int> $pathParams */
    private function buildPath(string $path, array $pathParams): string
    {
        foreach ($pathParams as $key => $value) {
            $encoded = rawurlencode((string) $value);
            $path = str_replace(['{' . $key . '}', ':' . $key], $encoded, $path);
        }

        return $path;
    }

    /** @param array<string, scalar|list<scalar>|null> $query */
    private function buildQuery(array $query): string
    {
        $pairs = [];
        foreach ($query as $key => $value) {
            foreach (is_array($value) ? $value : [$value] as $item) {
                if ($item === null) {
                    continue;
                }
                $pairs[] = urlencode($key) . '=' . urlencode(is_bool($item) ? ($item ? 'true' : 'false') : (string) $item);
            }
        }

        return implode('&', $pairs);
    }

    private function sleepBeforeRetry(int $attempt): void
    {
        $delayMs = min($this->maxDelayMs, $this->initialDelayMs * $this->multiplier ** max(0, $attempt - 1));
        if ($delayMs > 0) {
            usleep((int) ($delayMs * 1000));
        }
    }

    private function parseResponse(ResponseInterface $response): mixed
    {
        $status = $response->getStatusCode();
        if ($status === 204) {
            return null;
        }
        $text = (string) $response->getBody();
        if (self::isJsonContentType($response->getHeaderLine('Content-Type'))) {
            $data = $text === '' ? null : json_decode($text, true, flags: JSON_THROW_ON_ERROR);
        } else {
            $data = $text === '' ? null : $text;
        }
        if ($status >= 400) {
            throw new DrishtiApiException($status, $data);
        }

        return $data;
    }

    /**
     * True for `application/json` and `+json` media types, ignoring parameters and case.
     * A substring check would also match `application/jsonl` (batch results), which is not JSON.
     */
    private static function isJsonContentType(string $contentType): bool
    {
        $mediaType = strtolower(trim(explode(';', $contentType, 2)[0]));

        return $mediaType === 'application/json' || str_ends_with($mediaType, '+json');
    }

    /** @return array<string, string> */
    private static function contentRetentionHeaders(?string $contentRetention): array
    {
        return $contentRetention === null ? [] : ['X-Alpha-Content-Retention' => $contentRetention];
    }

    private static function normalizeEarningsItem(mixed $item): mixed
    {
        if (is_array($item) && !array_key_exists('earnings_table', $item) && array_key_exists('earnings_table_extraction', $item)) {
            $item['earnings_table'] = $item['earnings_table_extraction'];
        }

        return $item;
    }

    /** @return array<string, mixed>|null */
    private static function assoc(mixed $value): ?array
    {
        if ($value === null) {
            return null;
        }
        if (!is_array($value)) {
            throw new RuntimeException('Expected a JSON object response from the Drishti API');
        }

        /** @var array<string, mixed> $value */
        return $value;
    }
}
