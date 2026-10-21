namespace Manasija.Drishti;

/// <summary>Constants shared by every Drishti SDK.</summary>
public static class DrishtiConstants
{
    /// <summary>The production API base URL.</summary>
    public const string DefaultBaseUrl = "https://developers.manasija.in";

    /// <summary>The environment variable read by <see cref="DrishtiClient.FromEnvironment"/> for the API key.</summary>
    public const string ApiKeyEnvironmentVariable = "DRISHTI_API_KEY";

    /// <summary>The optional environment variable read by <see cref="DrishtiClient.FromEnvironment"/> for the base URL.</summary>
    public const string BaseUrlEnvironmentVariable = "DRISHTI_BASE_URL";

    /// <summary>The header that carries the API key.</summary>
    public const string ApiKeyHeader = "X-API-Key";

    /// <summary>The header that opts out of content retention.</summary>
    public const string ContentRetentionHeader = "X-Alpha-Content-Retention";

    /// <summary>The products available on the WebSocket feed.</summary>
    public static readonly IReadOnlyList<string> WebSocketProducts =
        ["news", "block_deals", "announcements", "earnings", "concalls", "alerts"];

    /// <summary>Subscribe error codes that retrying on the same socket cannot fix.</summary>
    public static readonly IReadOnlyList<string> PermanentSubscribeErrorCodes = ["bad_request", "forbidden", "unauthorized"];

    /// <summary>Known alert types. The API may add more, so treat the type as an open string.</summary>
    public static readonly IReadOnlyList<string> AlertTypes =
        ["52w_high", "52w_low", "earnings", "high_growth_concalls", "price_alert", "rvol_alert", "volume_alert"];

    /// <summary>Every batch job status.</summary>
    public static readonly IReadOnlyList<string> BatchJobStatuses =
        ["pending", "running", "succeeded", "partial", "failed", "cancelled"];

    /// <summary>Batch job statuses that end polling.</summary>
    public static readonly IReadOnlyList<string> BatchJobTerminalStatuses = ["succeeded", "partial", "failed", "cancelled"];

    /// <summary>HTTP statuses that are retried by default.</summary>
    public static readonly IReadOnlyList<int> DefaultRetryStatuses = [408, 409, 425, 429, 500, 502, 503, 504];
}

/// <summary>Content retention preference for requests that send your data.</summary>
public enum ContentRetention
{
    /// <summary>Ask the API not to retain the submitted content (<c>X-Alpha-Content-Retention: none</c>).</summary>
    None,
}
