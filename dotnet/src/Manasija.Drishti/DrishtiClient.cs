using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Manasija.Drishti.Batch;
using Manasija.Drishti.Exceptions;
using Manasija.Drishti.Http;
using Manasija.Drishti.Internal;
using Manasija.Drishti.Models;
using Manasija.Drishti.Params;
using Manasija.Drishti.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Manasija.Drishti;

/// <summary>
/// The Drishti REST client. Thread-safe; create one and reuse it.
/// </summary>
/// <example>
/// <code>
/// using var client = new DrishtiClient(Environment.GetEnvironmentVariable("DRISHTI_API_KEY")!);
/// var news = await client.GetNewsAsync(new NewsParams { Symbols = ["TCS", "INFY"], Limit = 5 });
/// foreach (var item in news!.Data)
/// {
///     Console.WriteLine($"{item.Symbol}: {item.Title}");
/// }
/// </code>
/// </example>
public sealed class DrishtiClient : IDrishtiClient, IDisposable
{
    private const int MaxRedirects = 5;
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromMinutes(5);

    private readonly string _apiKey;
    private readonly Dictionary<string, string> _headers;
    private readonly IDrishtiTransport _transport;
    private readonly HttpClient? _ownedHttpClient;
    private readonly int _maxRetries;
    private readonly TimeSpan _initialRetryDelay;
    private readonly TimeSpan _maxRetryDelay;
    private readonly double _retryMultiplier;
    private readonly HashSet<int> _retryOnStatuses;
    private readonly TimeSpan _timeout;
    private readonly string _userAgent;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    /// <summary>Creates a client with default settings.</summary>
    /// <param name="apiKey">Your API key.</param>
    /// <exception cref="ArgumentException"><paramref name="apiKey"/> is empty or whitespace.</exception>
    public DrishtiClient(string apiKey)
        : this(new DrishtiClientOptions { ApiKey = apiKey })
    {
    }

    /// <summary>Creates a client.</summary>
    /// <param name="options">Client settings.</param>
    /// <param name="transport">
    /// Sends requests. Null creates an <see cref="HttpClientTransport"/> that the client owns and
    /// disposes.
    /// </param>
    /// <param name="loggerFactory">Creates loggers for retries, redirects and the WebSocket session.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">An option is invalid, such as a blank API key.</exception>
    public DrishtiClient(DrishtiClientOptions options, IDrishtiTransport? transport = null, ILoggerFactory? loggerFactory = null)
    {
        Guard.NotNull(options, nameof(options));
        _apiKey = ValidateApiKey(options.ApiKey);
        BaseUrl = NormalizeBaseUrl(options.BaseUrl);
        Validate(options);

        _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in options.Headers ?? new Dictionary<string, string>())
        {
            if (!IsApiKeyHeader(header.Key))
            {
                _headers[header.Key] = header.Value;
            }
        }

        _maxRetries = options.MaxRetries;
        _initialRetryDelay = options.InitialRetryDelay;
        _maxRetryDelay = options.MaxRetryDelay;
        _retryMultiplier = options.RetryMultiplier;
        _retryOnStatuses = [.. (IEnumerable<int>?)options.RetryOnStatuses ?? DrishtiConstants.DefaultRetryStatuses];
        _timeout = options.Timeout;
        _userAgent = options.UserAgent;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<DrishtiClient>();

        if (transport is null)
        {
            _ownedHttpClient = new HttpClient(HttpClientTransport.CreateHandler(), disposeHandler: true)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            };
            transport = new HttpClientTransport(_ownedHttpClient);
        }

        _transport = transport;
    }

    /// <inheritdoc />
    public string BaseUrl { get; }

    /// <summary>
    /// Creates a client from the <c>DRISHTI_API_KEY</c> and optional <c>DRISHTI_BASE_URL</c>
    /// environment variables.
    /// </summary>
    /// <returns>A new client.</returns>
    /// <exception cref="InvalidOperationException"><c>DRISHTI_API_KEY</c> is not set.</exception>
    public static DrishtiClient FromEnvironment()
    {
        var apiKey = Environment.GetEnvironmentVariable(DrishtiConstants.ApiKeyEnvironmentVariable);
        if (apiKey is null || apiKey.Trim().Length == 0)
        {
            throw new InvalidOperationException(Messages.Format("EnvironmentApiKeyMissing", DrishtiConstants.ApiKeyEnvironmentVariable));
        }

        var options = new DrishtiClientOptions { ApiKey = apiKey };
        var baseUrl = Environment.GetEnvironmentVariable(DrishtiConstants.BaseUrlEnvironmentVariable);
        if (baseUrl is not null && baseUrl.Trim().Length > 0)
        {
            options.BaseUrl = baseUrl;
        }

        return new DrishtiClient(options);
    }

    /// <summary>Disposes the HTTP client, when this client created it.</summary>
    public void Dispose() => _ownedHttpClient?.Dispose();

    // --- Low level ------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<DrishtiResponse> RequestAsync(
        string method,
        string path,
        IEnumerable<KeyValuePair<string, object?>>? query = null,
        IReadOnlyDictionary<string, object?>? pathParams = null,
        object? body = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(method, nameof(method));
        Guard.NotNull(path, nameof(path));
        var builder = new QueryBuilder();
        foreach (var pair in query ?? [])
        {
            builder.AddValue(pair.Key, pair.Value);
        }

        var bodyBytes = body is null ? null : DrishtiJson.SerializeToUtf8(body);
        return SendAsync(method, path, pathParams, builder.Pairs, bodyBytes, bodyBytes is null ? null : "application/json", headers, cancellationToken);
    }

    // --- News, block deals, symbols -------------------------------------------------------------

    /// <inheritdoc />
    public Task<Paginated<NewsItem>?> GetNewsAsync(NewsParams? parameters = null, CancellationToken cancellationToken = default)
        => GetModelAsync<Paginated<NewsItem>>("/v1/news", parameters, cancellationToken);

    /// <inheritdoc />
    public Task<Paginated<BlockDealItem>?> GetBlockDealsAsync(BlockDealsParams? parameters = null, CancellationToken cancellationToken = default)
        => GetModelAsync<Paginated<BlockDealItem>>("/v1/block-deals", parameters, cancellationToken);

    /// <inheritdoc />
    public Task<DataList<SymbolMetadata>?> GetSymbolsMetadataAsync(SymbolsMetadataParams parameters, CancellationToken cancellationToken = default)
        => GetModelAsync<DataList<SymbolMetadata>>("/v1/symbols/metadata", Guard.NotNull(parameters, nameof(parameters)), cancellationToken);

    /// <inheritdoc />
    public async Task<string> GetSymbolsAsync(CancellationToken cancellationToken = default)
        => AsText(await SendAsync("GET", "/v1/symbols", null, [], null, null, null, cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task<DataList<SymbolMetadata>?> GetSymbolsJsonAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("GET", "/v1/symbols", null, [Pair("format", "json")], null, null, null, cancellationToken).ConfigureAwait(false);
        return Wrap<DataList<SymbolMetadata>>(response.Json);
    }

    // --- Announcements --------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<DataResponse<AnnouncementCategories>?> GetAnnouncementsCategoriesAsync(CancellationToken cancellationToken = default)
        => GetModelAsync<DataResponse<AnnouncementCategories>>("/v1/announcements/categories", null, cancellationToken);

    /// <inheritdoc />
    public Task<Paginated<Announcement>?> GetAnnouncementsAsync(AnnouncementsParams? parameters = null, CancellationToken cancellationToken = default)
        => GetModelAsync<Paginated<Announcement>>("/v1/announcements", parameters, cancellationToken);

    /// <inheritdoc />
    public Task<DataList<AttachmentLookupItem>?> GetAnnouncementsAttachmentsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
        => GetModelWithQueryAsync<DataList<AttachmentLookupItem>>("/v1/announcements/attachments", IdsQuery(ids), cancellationToken);

    /// <inheritdoc />
    public Task<byte[]> GetAnnouncementCitationPdfAsync(string announcementId, CancellationToken cancellationToken = default)
        => GetBytesAsync(
            "/v1/announcements/citations/{announcement_id}/pdf",
            PathParam("announcement_id", Guard.NotBlank(announcementId, nameof(announcementId))),
            [],
            cancellationToken);

    // --- Daily summary --------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<SummaryResponse?> PostDailySummaryAsync(object body, ContentRetention? contentRetention = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(body, nameof(body));
        var response = await SendAsync(
            "POST",
            "/v1/daily-summary/",
            null,
            [],
            DrishtiJson.SerializeToUtf8(body),
            "application/json",
            RetentionHeaders(contentRetention),
            cancellationToken).ConfigureAwait(false);
        return Wrap<SummaryResponse>(response.Json);
    }

    // --- Earnings -------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<Paginated<IndexItem>?> GetEarningsIndexAsync(IndexParams? parameters = null, CancellationToken cancellationToken = default)
        => GetModelAsync<Paginated<IndexItem>>("/v1/earnings/index", parameters, cancellationToken);

    /// <inheritdoc />
    public async Task<Paginated<Earnings>?> GetEarningsAsync(EarningsParams? parameters = null, CancellationToken cancellationToken = default)
    {
        var json = await GetJsonAsync("/v1/earnings", parameters, cancellationToken).ConfigureAwait(false);
        return Wrap<Paginated<Earnings>>(NormalizeEarnings(json, isList: true));
    }

    /// <inheritdoc />
    public async Task<Earnings?> GetEarningsDetailAsync(DetailParams parameters, CancellationToken cancellationToken = default)
    {
        var json = await GetJsonAsync("/v1/earnings/detail", ValidateDetail(parameters), cancellationToken).ConfigureAwait(false);
        return Wrap<Earnings>(NormalizeEarnings(json, isList: false));
    }

    /// <inheritdoc />
    public Task<DataList<AttachmentLookupItem>?> GetEarningsAttachmentsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
        => GetModelWithQueryAsync<DataList<AttachmentLookupItem>>("/v1/earnings/attachments", IdsQuery(ids), cancellationToken);

    /// <inheritdoc />
    public Task<byte[]> GetEarningsCitationPdfAsync(string earningsId, int? page = null, CancellationToken cancellationToken = default)
        => GetBytesAsync(
            "/v1/earnings/citations/{earnings_id}/pdf",
            PathParam("earnings_id", Guard.NotBlank(earningsId, nameof(earningsId))),
            new QueryBuilder().Add("page", page).Pairs,
            cancellationToken);

    /// <inheritdoc />
    public async Task<string> GetEarningsCitationPageAsync(string earningsId, int page, CancellationToken cancellationToken = default)
    {
        var pathParams = new Dictionary<string, object?>
        {
            ["earnings_id"] = Guard.NotBlank(earningsId, nameof(earningsId)),
            ["page"] = page,
        };
        var response = await SendAsync("GET", "/v1/earnings/citations/{earnings_id}/page/{page}", pathParams, [], null, null, null, cancellationToken)
            .ConfigureAwait(false);
        return AsText(response);
    }

    /// <inheritdoc />
    public Task<Paginated<UpcomingEarnings>?> GetUpcomingEarningsAsync(UpcomingEarningsParams? parameters = null, CancellationToken cancellationToken = default)
        => GetModelAsync<Paginated<UpcomingEarnings>>("/v1/earnings/upcoming", parameters, cancellationToken);

    // --- Concalls -------------------------------------------------------------------------------

    /// <inheritdoc />
    public Task<Paginated<Concall>?> GetConcallsAsync(ConcallsParams? parameters = null, CancellationToken cancellationToken = default)
        => GetModelAsync<Paginated<Concall>>("/v1/concalls", parameters, cancellationToken);

    /// <inheritdoc />
    public Task<byte[]> GetConcallCitationPdfAsync(string concallId, CancellationToken cancellationToken = default)
        => GetBytesAsync(
            "/v1/concalls/citations/{concall_id}/pdf",
            PathParam("concall_id", Guard.NotBlank(concallId, nameof(concallId))),
            [],
            cancellationToken);

    /// <inheritdoc />
    public Task<Paginated<IndexItem>?> GetConcallsIndexAsync(IndexParams? parameters = null, CancellationToken cancellationToken = default)
        => GetModelAsync<Paginated<IndexItem>>("/v1/concalls/index", parameters, cancellationToken);

    /// <inheritdoc />
    public Task<Paginated<UpcomingConcall>?> GetUpcomingConcallsAsync(UpcomingConcallsParams? parameters = null, CancellationToken cancellationToken = default)
        => GetModelAsync<Paginated<UpcomingConcall>>("/v1/concalls/upcoming", parameters, cancellationToken);

    /// <inheritdoc />
    public Task<Concall?> GetConcallsDetailAsync(DetailParams parameters, CancellationToken cancellationToken = default)
        => GetModelAsync<Concall>("/v1/concalls/detail", ValidateDetail(parameters), cancellationToken);

    /// <inheritdoc />
    public Task<ConcallArtifactUrls?> GetConcallsTranscriptAsync(TranscriptParams parameters, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(parameters, nameof(parameters));
        Guard.NotBlank(parameters.Quarter, nameof(parameters.Quarter));
        RequireSymbolOrScripCode(parameters.Symbol, parameters.ScripCode);
        return GetModelAsync<ConcallArtifactUrls>("/v1/concalls/transcript", parameters, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DataList<ConcallTranscriptLookupItem>?> PostConcallsTranscriptsAsync(
        IEnumerable<TranscriptLookup> items,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(items, nameof(items));
        var body = new { items = items.Select(i => new { symbol = i.Symbol, quarter = i.Quarter }).ToList() };
        var response = await SendAsync("POST", "/v1/concalls/transcripts", null, [], DrishtiJson.SerializeToUtf8(body), "application/json", null, cancellationToken)
            .ConfigureAwait(false);
        return Wrap<DataList<ConcallTranscriptLookupItem>>(response.Json);
    }

    // --- Alerts and account ---------------------------------------------------------------------

    /// <inheritdoc />
    public Task<Paginated<Alert>?> GetAlertsAsync(AlertsParams? parameters = null, CancellationToken cancellationToken = default)
        => GetModelAsync<Paginated<Alert>>("/v1/alerts", parameters, cancellationToken);

    /// <inheritdoc />
    public Task<DataResponse<Account>?> GetAccountAsync(CancellationToken cancellationToken = default)
        => GetModelAsync<DataResponse<Account>>("/v1/account", null, cancellationToken);

    /// <inheritdoc />
    public Task<AccountLimits?> GetAccountLimitsAsync(CancellationToken cancellationToken = default)
        => GetModelAsync<AccountLimits>("/v1/account/limits", null, cancellationToken);

    /// <inheritdoc />
    public Task<DataResponse<AccountUsage>?> GetAccountUsageAsync(CancellationToken cancellationToken = default)
        => GetModelAsync<DataResponse<AccountUsage>>("/v1/account/usage", null, cancellationToken);

    /// <inheritdoc />
    public async Task<DataList<LedgerEntry>?> GetAccountLedgerAsync(int? limit = null, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("GET", "/v1/account/ledger", null, new QueryBuilder().Add("limit", limit).Pairs, null, null, null, cancellationToken)
            .ConfigureAwait(false);
        return Wrap<DataList<LedgerEntry>>(response.Json);
    }

    // --- Batch jobs -----------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<BatchJob?> PostBatchJobsAsync(BatchJobRequest request, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(request, nameof(request));
        var multipart = new MultipartWriter("drishti-" + Guid.NewGuid().ToString("N"))
            .File("file", request.FileName, "application/jsonl", request.FileContent);
        if (request.DisplayName is not null)
        {
            multipart.Field("display_name", request.DisplayName);
        }

        if (request.Metadata is not null)
        {
            multipart.Field("metadata", request.Metadata);
        }

        var response = await SendAsync(
            "POST",
            "/v1/batch/jobs",
            null,
            [],
            multipart.Finish(),
            multipart.ContentType,
            RetentionHeaders(request.ContentRetention),
            cancellationToken).ConfigureAwait(false);
        return Wrap<BatchJob>(response.Json);
    }

    /// <inheritdoc />
    public async Task<BatchJobList?> GetBatchJobsAsync(int? limit = null, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("GET", "/v1/batch/jobs", null, new QueryBuilder().Add("limit", limit).Pairs, null, null, null, cancellationToken)
            .ConfigureAwait(false);
        return Wrap<BatchJobList>(response.Json);
    }

    /// <inheritdoc />
    public async Task<BatchJob?> GetBatchJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("GET", "/v1/batch/jobs/{job_id}", JobPath(jobId), [], null, null, null, cancellationToken).ConfigureAwait(false);
        return Wrap<BatchJob>(response.Json);
    }

    /// <inheritdoc />
    public async Task<BatchJob?> CancelBatchJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("DELETE", "/v1/batch/jobs/{job_id}", JobPath(jobId), [], null, null, null, cancellationToken).ConfigureAwait(false);
        return Wrap<BatchJob>(response.Json);
    }

    /// <inheritdoc />
    public async Task<string> GetBatchJobResultsAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("GET", "/v1/batch/jobs/{job_id}/results", JobPath(jobId), [], null, null, null, cancellationToken)
            .ConfigureAwait(false);
        return AsText(response);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BatchResultLine>> GetBatchJobResultsParsedAsync(string jobId, CancellationToken cancellationToken = default)
        => BatchJsonl.ParseResults(await GetBatchJobResultsAsync(jobId, cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task<BatchJob> WaitForBatchJobCompletionAsync(
        string jobId,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        IEnumerable<string>? terminalStatuses = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(jobId, nameof(jobId));
        var interval = Guard.NotNegative(pollInterval ?? DefaultPollInterval, nameof(pollInterval));
        var limit = Guard.NotNegative(timeout ?? DefaultWaitTimeout, nameof(timeout));
        var terminal = new HashSet<string>(terminalStatuses ?? DrishtiConstants.BatchJobTerminalStatuses, StringComparer.OrdinalIgnoreCase);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var job = await GetBatchJobAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (job?.Status is { } status && terminal.Contains(status))
            {
                return job;
            }

            if (stopwatch.Elapsed >= limit)
            {
                throw new DrishtiException(Messages.Format("BatchJobTimedOut", jobId));
            }

            if (interval > TimeSpan.Zero)
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task<BatchJob> SubmitBatchJobAndWaitAsync(
        BatchJobRequest request,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var job = await PostBatchJobsAsync(request, cancellationToken).ConfigureAwait(false);
        var id = job?.Json.ValueKind == JsonValueKind.Object && job.Json.TryGetProperty("id", out var idElement)
            ? idElement.ValueKind switch
            {
                JsonValueKind.String => idElement.GetString(),
                JsonValueKind.Number => idElement.GetRawText(),
                _ => null,
            }
            : null;
        if (id is null || id.Length == 0)
        {
            throw new DrishtiException(Messages.Get("BatchJobMissingId"));
        }

        return await WaitForBatchJobCompletionAsync(id, pollInterval, timeout, null, cancellationToken).ConfigureAwait(false);
    }

    // --- WebSocket ------------------------------------------------------------------------------

    /// <inheritdoc />
    public IDrishtiWebSocketSession CreateWebSocketSession(Action<DrishtiWebSocketOptions>? configure = null)
    {
        var options = new DrishtiWebSocketOptions { ApiKey = _apiKey, BaseUrl = BaseUrl };
        foreach (var header in _headers)
        {
            options.Headers[header.Key] = header.Value;
        }

        configure?.Invoke(options);
        return new DrishtiWebSocketSession(options, connector: null, clock: null, _loggerFactory.CreateLogger<DrishtiWebSocketSession>());
    }

    // --- Internals ------------------------------------------------------------------------------

    private async Task<T?> GetModelAsync<T>(string path, QueryParams? parameters, CancellationToken cancellationToken)
        where T : DrishtiObject
        => Wrap<T>(await GetJsonAsync(path, parameters, cancellationToken).ConfigureAwait(false));

    private async Task<T?> GetModelWithQueryAsync<T>(string path, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken cancellationToken)
        where T : DrishtiObject
    {
        var response = await SendAsync("GET", path, null, query, null, null, null, cancellationToken).ConfigureAwait(false);
        return Wrap<T>(response.Json);
    }

    private async Task<JsonElement?> GetJsonAsync(string path, QueryParams? parameters, CancellationToken cancellationToken)
    {
        var query = parameters?.ToQuery() ?? [];
        var response = await SendAsync("GET", path, null, query, null, null, null, cancellationToken).ConfigureAwait(false);
        return response.Json;
    }

    private async Task<byte[]> GetBytesAsync(
        string path,
        IReadOnlyDictionary<string, object?> pathParams,
        IReadOnlyList<KeyValuePair<string, string>> query,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync("GET", path, pathParams, query, null, null, null, cancellationToken).ConfigureAwait(false);
        return response.Json is { } json ? Encoding.UTF8.GetBytes(json.GetRawText()) : response.Body;
    }

    private async Task<DrishtiResponse> SendAsync(
        string method,
        string path,
        IReadOnlyDictionary<string, object?>? pathParams,
        IReadOnlyList<KeyValuePair<string, string>> query,
        byte[]? body,
        string? contentType,
        IReadOnlyDictionary<string, string>? extraHeaders,
        CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "application/json",
            ["User-Agent"] = _userAgent,
        };
        foreach (var header in _headers)
        {
            headers[header.Key] = header.Value;
        }

        foreach (var header in extraHeaders ?? new Dictionary<string, string>())
        {
            if (!IsApiKeyHeader(header.Key))
            {
                headers[header.Key] = header.Value;
            }
        }

        if (contentType is not null)
        {
            headers["Content-Type"] = contentType;
        }

        headers[DrishtiConstants.ApiKeyHeader] = _apiKey;

        var request = new TransportRequest(method.ToUpperInvariant(), BuildUri(path, pathParams, query), headers, body, _timeout);
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["DrishtiMethod"] = request.Method, ["DrishtiPath"] = request.Uri.AbsolutePath });

        var response = await SendWithRetriesAsync(request, cancellationToken).ConfigureAwait(false);
        for (var hop = 0; IsRedirect(response) && hop < MaxRedirects; hop++)
        {
            request = RedirectRequest(request, response);
            _logger.LogDebug(LogMessages.FollowingRedirect, response.StatusCode, request.Uri.GetLeftPart(UriPartial.Path));
            response = await SendWithRetriesAsync(request, cancellationToken).ConfigureAwait(false);
        }

        return ParseResponse(response);
    }

    private async Task<TransportResponse> SendWithRetriesAsync(TransportRequest request, CancellationToken cancellationToken)
    {
        var attempts = _maxRetries + 1;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TransportResponse response;
            try
            {
                response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNetworkError(ex, cancellationToken))
            {
                if (attempt >= attempts)
                {
                    throw new DrishtiNetworkException(Messages.Format("NetworkError", request.Method, request.Uri.AbsolutePath, ex.Message), ex);
                }

                var networkDelay = RetryDelay(attempt);
                _logger.LogWarning(ex, LogMessages.RetryingRequest, request.Method, request.Uri.AbsolutePath, "network error", attempt, attempts - 1, networkDelay.TotalMilliseconds);
                await DelayAsync(networkDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (response.StatusCode >= 400 && attempt < attempts && _retryOnStatuses.Contains(response.StatusCode))
            {
                var delay = RetryDelay(attempt);
                _logger.LogWarning(LogMessages.RetryingRequest, request.Method, request.Uri.AbsolutePath, response.StatusCode, attempt, attempts - 1, delay.TotalMilliseconds);
                await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            return response;
        }
    }

    private static bool IsNetworkError(Exception ex, CancellationToken cancellationToken)
        => ex is HttpRequestException or IOException or TimeoutException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => delay > TimeSpan.Zero ? Task.Delay(delay, cancellationToken) : Task.CompletedTask;

    private TimeSpan RetryDelay(int attempt)
    {
        var millis = _initialRetryDelay.TotalMilliseconds * Math.Pow(_retryMultiplier, Math.Max(0, attempt - 1));
        return TimeSpan.FromMilliseconds(Math.Min(_maxRetryDelay.TotalMilliseconds, millis));
    }

    private static bool IsRedirect(TransportResponse response)
        => response.StatusCode is 301 or 302 or 303 or 307 or 308 && response.Header("Location") is not null;

    /// <summary>
    /// Builds the follow-up request for a redirect. Citation routes redirect to file storage on
    /// another host, so the API key and custom headers are kept only for same-origin redirects.
    /// </summary>
    private TransportRequest RedirectRequest(TransportRequest request, TransportResponse response)
    {
        var location = response.Header("Location")!;
        if (!Uri.TryCreate(request.Uri, location, out var target))
        {
            throw new DrishtiException(Messages.Format("InvalidRedirectLocation", location));
        }

        if (request.Uri.Scheme == Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttps)
        {
            throw new DrishtiException(Messages.Format("InsecureRedirect", target.Scheme, target));
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (SameOrigin(request.Uri, target))
        {
            foreach (var header in request.Headers)
            {
                headers[header.Key] = header.Value;
            }
        }
        else
        {
            headers["Accept"] = "*/*";
            headers["User-Agent"] = _userAgent;
        }

        var status = response.StatusCode;
        var switchToGet = status == 303 || (status is 301 or 302 && request.Method != "GET");
        if (switchToGet)
        {
            headers.Remove("Content-Type");
        }

        return new TransportRequest(switchToGet ? "GET" : request.Method, target, headers, switchToGet ? null : request.Body, request.Timeout);
    }

    private static bool SameOrigin(Uri a, Uri b)
        => string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
            && a.Port == b.Port;

    private Uri BuildUri(string path, IReadOnlyDictionary<string, object?>? pathParams, IReadOnlyList<KeyValuePair<string, string>> query)
    {
        var resolved = path;
        foreach (var pair in pathParams ?? new Dictionary<string, object?>())
        {
            var encoded = UrlEncoding.EncodeComponent(Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty);
            resolved = resolved.Replace("{" + pair.Key + "}", encoded).Replace(":" + pair.Key, encoded);
        }

        var url = new StringBuilder(BaseUrl).Append('/').Append(resolved.TrimStart('/'));
        for (var i = 0; i < query.Count; i++)
        {
            url.Append(i == 0 ? '?' : '&')
                .Append(UrlEncoding.EncodeComponent(query[i].Key))
                .Append('=')
                .Append(UrlEncoding.EncodeComponent(query[i].Value));
        }

        return new Uri(url.ToString(), UriKind.Absolute);
    }

    private static DrishtiResponse ParseResponse(TransportResponse response)
    {
        var status = response.StatusCode;
        var body = response.Body;
        var contentType = response.ContentType;
        JsonElement? json = null;
        if (status != 204 && body.Length > 0 && contentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            try
            {
                json = DrishtiJson.Parse(body);
            }
            catch (JsonException ex) when (status < 400)
            {
                throw new DrishtiException(Messages.Format("InvalidJsonResponse", status), ex);
            }
            catch (JsonException)
            {
                // An error body that claims to be JSON but is not is reported as text below.
            }
        }

        if (status >= 400)
        {
            throw new DrishtiApiException(status, json, body.Length == 0 ? null : Encoding.UTF8.GetString(body), response.Headers);
        }

        return new DrishtiResponse(status, contentType, body, json);
    }

    private static T? Wrap<T>(JsonElement? json)
        where T : DrishtiObject
        => json is { } element ? DrishtiObject.From<T>(element) : null;

    private static string AsText(DrishtiResponse response)
        => response.IsEmpty ? string.Empty : response.Json is { } json ? json.GetRawText() : response.Text;

    private static JsonElement? NormalizeEarnings(JsonElement? json, bool isList)
    {
        if (json is not { ValueKind: JsonValueKind.Object } element)
        {
            return json;
        }

        var node = JsonNode.Parse(element.GetRawText())!.AsObject();
        if (isList)
        {
            if (node["data"] is JsonArray items)
            {
                foreach (var item in items)
                {
                    NormalizeEarningsItem(item);
                }
            }
        }
        else
        {
            NormalizeEarningsItem(node);
        }

        return DrishtiJson.Parse(node.ToJsonString());
    }

    private static void NormalizeEarningsItem(JsonNode? item)
    {
        if (item is JsonObject obj && !obj.ContainsKey("earnings_table") && obj.TryGetPropertyValue("earnings_table_extraction", out var extraction))
        {
            obj["earnings_table"] = extraction?.DeepClone();
        }
    }

    private static IReadOnlyDictionary<string, string>? RetentionHeaders(ContentRetention? retention)
        => retention switch
        {
            ContentRetention.None => new Dictionary<string, string> { [DrishtiConstants.ContentRetentionHeader] = "none" },
            _ => null,
        };

    private static IReadOnlyList<KeyValuePair<string, string>> IdsQuery(IEnumerable<string> ids)
    {
        Guard.NotNull(ids, nameof(ids));
        var list = ids.Where(id => id is not null && id.Trim().Length > 0).ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException(Messages.Get("IdsRequired"), nameof(ids));
        }

        return new QueryBuilder().Add("ids", list).Pairs;
    }

    private static DetailParams ValidateDetail(DetailParams parameters)
    {
        Guard.NotNull(parameters, nameof(parameters));
        Guard.NotBlank(parameters.Quarter, nameof(parameters.Quarter));
        RequireSymbolOrScripCode(parameters.Symbol, parameters.ScripCode);
        return parameters;
    }

    private static void RequireSymbolOrScripCode(string? symbol, string? scripCode)
    {
        if (string.IsNullOrWhiteSpace(symbol) && string.IsNullOrWhiteSpace(scripCode))
        {
            throw new ArgumentException(Messages.Get("SymbolOrScripCodeRequired"));
        }
    }

    private static Dictionary<string, object?> JobPath(string jobId)
        => PathParam("job_id", Guard.NotBlank(jobId, nameof(jobId)));

    private static Dictionary<string, object?> PathParam(string name, object value) => new() { [name] = value };

    private static KeyValuePair<string, string> Pair(string name, string value) => new(name, value);

    private static bool IsApiKeyHeader(string name)
        => string.Equals(name, DrishtiConstants.ApiKeyHeader, StringComparison.OrdinalIgnoreCase);

    private static string ValidateApiKey(string? apiKey)
    {
        if (apiKey is null || apiKey.Trim().Length == 0)
        {
            throw new ArgumentException(Messages.Get("ApiKeyRequired"), nameof(DrishtiClientOptions.ApiKey));
        }

        return apiKey;
    }

    private static string NormalizeBaseUrl(string? baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(Messages.Format("InvalidBaseUrl", baseUrl), nameof(DrishtiClientOptions.BaseUrl));
        }

        return trimmed;
    }

    private static void Validate(DrishtiClientOptions options)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true))
        {
            throw new ArgumentException(string.Join(" ", results.Select(r => r.ErrorMessage)), nameof(options));
        }

        Guard.NotNegative(options.InitialRetryDelay, nameof(options.InitialRetryDelay));
        Guard.NotNegative(options.MaxRetryDelay, nameof(options.MaxRetryDelay));
        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Timeout, Messages.Get("TimeoutMustBePositive"));
        }
    }
}
