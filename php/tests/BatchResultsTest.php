<?php

declare(strict_types=1);

namespace Drishti\Tests;

use Drishti\DrishtiClient;
use Drishti\Tests\Support\RecordingHttpClient;
use PHPUnit\Framework\TestCase;

/** Regression tests: batch results are JSONL (`application/jsonl`) and must not be JSON-decoded. */
final class BatchResultsTest extends TestCase
{
    private const JSONL = "{\"custom_id\":\"a\",\"status\":\"succeeded\"}\n{\"custom_id\":\"b\",\"status\":\"failed\"}\n";

    private RecordingHttpClient $http;

    private DrishtiClient $client;

    protected function setUp(): void
    {
        $this->http = new RecordingHttpClient([
            ['status' => 200, 'content_type' => 'application/jsonl', 'text' => self::JSONL],
        ]);
        $this->client = new DrishtiClient(apiKey: 'test-key', baseUrl: 'https://api.test', httpClient: $this->http);
    }

    public function testResultsReturnsRawJsonlText(): void
    {
        self::assertSame(self::JSONL, $this->client->getBatchJobsJobIdResults('job_1'));
        self::assertSame('/v1/batch/jobs/job_1/results', $this->http->requests[0]->getUri()->getPath());
    }

    public function testResultsParsedSplitsJsonlLines(): void
    {
        self::assertSame(
            [
                ['custom_id' => 'a', 'status' => 'succeeded'],
                ['custom_id' => 'b', 'status' => 'failed'],
            ],
            $this->client->getBatchJobsJobIdResultsParsed('job_1'),
        );
    }
}
