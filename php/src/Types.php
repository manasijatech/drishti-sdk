<?php

declare(strict_types=1);

namespace Drishti;

/**
 * Shared enumerations of the Drishti API (/v1).
 *
 * Responses are returned as decoded JSON arrays; see the API docs for field details:
 * https://developers.manasija.in/docs
 */
final class Types
{
    /** Known alert types for GET /v1/alerts `type`; the API may add more. */
    public const ALERT_TYPES = [
        '52w_high',
        '52w_low',
        'earnings',
        'high_growth_concalls',
        'price_alert',
        'rvol_alert',
        'volume_alert',
    ];

    public const BATCH_JOB_STATUSES = ['pending', 'running', 'succeeded', 'partial', 'failed', 'cancelled'];

    /** Polling stops when a batch job reaches one of these statuses. */
    public const BATCH_JOB_TERMINAL_STATUSES = ['succeeded', 'partial', 'failed', 'cancelled'];

    private function __construct()
    {
    }
}
