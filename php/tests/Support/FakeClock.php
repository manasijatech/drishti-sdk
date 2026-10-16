<?php

declare(strict_types=1);

namespace Drishti\Tests\Support;

/** Deterministic clock: time only moves when the code under test sleeps or waits on a socket. */
final class FakeClock
{
    public float $now = 1000.0;

    /** @var list<float> */
    public array $sleeps = [];

    public function now(): float
    {
        return $this->now;
    }

    public function sleep(float $seconds): void
    {
        $this->sleeps[] = $seconds;
        $this->advance($seconds);
    }

    public function advance(float $seconds): void
    {
        $this->now += max(0.0, $seconds);
    }
}
