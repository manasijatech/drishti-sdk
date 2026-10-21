using Manasija.Drishti.Exceptions;
using Manasija.Drishti.Http;
using Manasija.Drishti.Models;
using Manasija.Drishti.Params;
using Manasija.Drishti.WebSocket;

namespace Manasija.Drishti;

/// <summary>
/// The Drishti REST API. Every method throws <see cref="DrishtiApiException"/> when the API
/// returns an error status, and <see cref="DrishtiNetworkException"/> when no response arrives
/// after all retries. Methods returning a model return <see langword="null"/> when the response
/// body is empty.
/// </summary>
public interface IDrishtiClient
{
    /// <summary>The base URL requests are sent to, without a trailing slash.</summary>
    string BaseUrl { get; }

    /// <summary>
    /// Sends any request. <paramref name="path"/> is appended to the base URL as is (include
    /// <c>/v1</c>); <c>{name}</c> or <c>:name</c> placeholders are replaced from
    /// <paramref name="pathParams"/> and percent-encoded.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path, such as <c>/v1/news</c>.</param>
    /// <param name="query">Query parameters; nulls and empty lists are omitted, lists are comma-joined.</param>
    /// <param name="pathParams">Values for path placeholders.</param>
    /// <param name="body">A JSON body: any serializable object, or a <see cref="System.Text.Json.JsonElement"/>.</param>
    /// <param name="headers">Extra headers for this request. <c>X-API-Key</c> cannot be overridden.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The response.</returns>
    Task<DrishtiResponse> RequestAsync(
        string method,
        string path,
        IEnumerable<KeyValuePair<string, object?>>? query = null,
        IReadOnlyDictionary<string, object?>? pathParams = null,
        object? body = null,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);

    /// <summary>News with AI summaries: <c>GET /v1/news</c>.</summary>
    /// <param name="parameters">Filters; null returns the latest news.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page of news.</returns>
    Task<Paginated<NewsItem>?> GetNewsAsync(NewsParams? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>Block deals: <c>GET /v1/block-deals</c>.</summary>
    /// <param name="parameters">Filters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page of deals.</returns>
    Task<Paginated<BlockDealItem>?> GetBlockDealsAsync(BlockDealsParams? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>Reference data for symbols: <c>GET /v1/symbols/metadata</c>.</summary>
    /// <param name="parameters">The symbols or scrip codes to look up.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One entry per symbol found.</returns>
    Task<DataList<SymbolMetadata>?> GetSymbolsMetadataAsync(SymbolsMetadataParams parameters, CancellationToken cancellationToken = default);

    /// <summary>The symbol universe as CSV: <c>GET /v1/symbols</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The CSV text.</returns>
    Task<string> GetSymbolsAsync(CancellationToken cancellationToken = default);

    /// <summary>The symbol universe as JSON: <c>GET /v1/symbols?format=json</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Every listed symbol.</returns>
    Task<DataList<SymbolMetadata>?> GetSymbolsJsonAsync(CancellationToken cancellationToken = default);

    /// <summary>Announcement categories: <c>GET /v1/announcements/categories</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The categories, split by importance.</returns>
    Task<DataResponse<AnnouncementCategories>?> GetAnnouncementsCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Corporate announcements: <c>GET /v1/announcements</c>.</summary>
    /// <param name="parameters">Filters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page of announcements.</returns>
    Task<Paginated<Announcement>?> GetAnnouncementsAsync(AnnouncementsParams? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>Download URLs for announcement filings: <c>GET /v1/announcements/attachments</c>.</summary>
    /// <param name="ids">Announcement ids.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One lookup result per id.</returns>
    Task<DataList<AttachmentLookupItem>?> GetAnnouncementsAttachmentsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);

    /// <summary>The source filing PDF: <c>GET /v1/announcements/citations/{announcement_id}/pdf</c>.</summary>
    /// <param name="announcementId">The announcement id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The PDF bytes.</returns>
    Task<byte[]> GetAnnouncementCitationPdfAsync(string announcementId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an AI summary for a portfolio or watchlist: <c>POST /v1/daily-summary/</c>.
    /// The body is sent as given; the API applies its own defaults.
    /// </summary>
    /// <param name="body">A <see cref="DailySummaryRequest"/>, or any JSON-serializable object.</param>
    /// <param name="contentRetention">Set <see cref="ContentRetention.None"/> to ask the API not to retain the request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The summary.</returns>
    Task<SummaryResponse?> PostDailySummaryAsync(object body, ContentRetention? contentRetention = null, CancellationToken cancellationToken = default);

    /// <summary>A lightweight earnings index: <c>GET /v1/earnings/index</c>.</summary>
    /// <param name="parameters">Filters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page of index rows.</returns>
    Task<Paginated<IndexItem>?> GetEarningsIndexAsync(IndexParams? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>Earnings results: <c>GET /v1/earnings</c>.</summary>
    /// <param name="parameters">Filters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page of results.</returns>
    Task<Paginated<Earnings>?> GetEarningsAsync(EarningsParams? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>One earnings result: <c>GET /v1/earnings/detail</c>.</summary>
    /// <param name="parameters">The quarter, and a symbol or scrip code.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result.</returns>
    Task<Earnings?> GetEarningsDetailAsync(DetailParams parameters, CancellationToken cancellationToken = default);

    /// <summary>Download URLs for earnings filings: <c>GET /v1/earnings/attachments</c>.</summary>
    /// <param name="ids">Earnings ids.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One lookup result per id.</returns>
    Task<DataList<AttachmentLookupItem>?> GetEarningsAttachmentsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);

    /// <summary>The source filing PDF: <c>GET /v1/earnings/citations/{earnings_id}/pdf</c>.</summary>
    /// <param name="earningsId">The earnings id.</param>
    /// <param name="page">Jump to this page of the filing.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The PDF bytes.</returns>
    Task<byte[]> GetEarningsCitationPdfAsync(string earningsId, int? page = null, CancellationToken cancellationToken = default);

    /// <summary>One page of a filing: <c>GET /v1/earnings/citations/{earnings_id}/page/{page}</c>.</summary>
    /// <param name="earningsId">The earnings id.</param>
    /// <param name="page">The 1-based page number.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page content.</returns>
    Task<string> GetEarningsCitationPageAsync(string earningsId, int page, CancellationToken cancellationToken = default);

    /// <summary>Scheduled earnings announcements: <c>GET /v1/earnings/upcoming</c>.</summary>
    /// <param name="parameters">Filters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page of upcoming events.</returns>
    Task<Paginated<UpcomingEarnings>?> GetUpcomingEarningsAsync(UpcomingEarningsParams? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>Earnings calls with AI analysis: <c>GET /v1/concalls</c>.</summary>
    /// <param name="parameters">Filters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page of concalls.</returns>
    Task<Paginated<Concall>?> GetConcallsAsync(ConcallsParams? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>The source PDF for a concall: <c>GET /v1/concalls/citations/{concall_id}/pdf</c>.</summary>
    /// <param name="concallId">The concall id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The PDF bytes.</returns>
    Task<byte[]> GetConcallCitationPdfAsync(string concallId, CancellationToken cancellationToken = default);

    /// <summary>A lightweight concall index: <c>GET /v1/concalls/index</c>.</summary>
    /// <param name="parameters">Filters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page of index rows.</returns>
    Task<Paginated<IndexItem>?> GetConcallsIndexAsync(IndexParams? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>Scheduled concalls: <c>GET /v1/concalls/upcoming</c>.</summary>
    /// <param name="parameters">Filters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page of upcoming concalls.</returns>
    Task<Paginated<UpcomingConcall>?> GetUpcomingConcallsAsync(UpcomingConcallsParams? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>One concall: <c>GET /v1/concalls/detail</c>.</summary>
    /// <param name="parameters">The quarter, and a symbol or scrip code.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The concall.</returns>
    Task<Concall?> GetConcallsDetailAsync(DetailParams parameters, CancellationToken cancellationToken = default);

    /// <summary>Transcript and audio URLs for one concall: <c>GET /v1/concalls/transcript</c>.</summary>
    /// <param name="parameters">The quarter, and a symbol or scrip code.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The URLs.</returns>
    Task<ConcallArtifactUrls?> GetConcallsTranscriptAsync(TranscriptParams parameters, CancellationToken cancellationToken = default);

    /// <summary>Transcript URLs for several concalls: <c>POST /v1/concalls/transcripts</c>.</summary>
    /// <param name="items">Symbol and quarter pairs.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>One lookup result per pair.</returns>
    Task<DataList<ConcallTranscriptLookupItem>?> PostConcallsTranscriptsAsync(IEnumerable<TranscriptLookup> items, CancellationToken cancellationToken = default);

    /// <summary>Market alerts: <c>GET /v1/alerts</c>.</summary>
    /// <param name="parameters">Filters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A page of alerts.</returns>
    Task<Paginated<Alert>?> GetAlertsAsync(AlertsParams? parameters = null, CancellationToken cancellationToken = default);

    /// <summary>Your account: <c>GET /v1/account</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The account.</returns>
    Task<DataResponse<Account>?> GetAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>Your rate limits: <c>GET /v1/account/limits</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The limits.</returns>
    Task<AccountLimits?> GetAccountLimitsAsync(CancellationToken cancellationToken = default);

    /// <summary>Today's usage: <c>GET /v1/account/usage</c>.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The usage.</returns>
    Task<DataResponse<AccountUsage>?> GetAccountUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>Credit ledger entries: <c>GET /v1/account/ledger</c>.</summary>
    /// <param name="limit">How many entries to return; null uses the API default.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The entries, newest first.</returns>
    Task<DataList<LedgerEntry>?> GetAccountLedgerAsync(int? limit = null, CancellationToken cancellationToken = default);

    /// <summary>Submits a batch job with a JSONL input file: <c>POST /v1/batch/jobs</c>.</summary>
    /// <param name="request">The file and options.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The created job.</returns>
    Task<BatchJob?> PostBatchJobsAsync(BatchJobRequest request, CancellationToken cancellationToken = default);

    /// <summary>Recent batch jobs: <c>GET /v1/batch/jobs</c>.</summary>
    /// <param name="limit">How many jobs to return; null uses the API default.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The jobs, newest first.</returns>
    Task<BatchJobList?> GetBatchJobsAsync(int? limit = null, CancellationToken cancellationToken = default);

    /// <summary>One batch job: <c>GET /v1/batch/jobs/{job_id}</c>.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The job.</returns>
    Task<BatchJob?> GetBatchJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a batch job: <c>DELETE /v1/batch/jobs/{job_id}</c>.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns><c>{"id", "status": "cancelled"}</c>, or null when the API answers 204.</returns>
    Task<BatchJob?> CancelBatchJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>A job's results file as raw JSONL: <c>GET /v1/batch/jobs/{job_id}/results</c>.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The JSONL text.</returns>
    Task<string> GetBatchJobResultsAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>A job's results, parsed one model per line. Blank lines are skipped.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The result lines.</returns>
    Task<IReadOnlyList<BatchResultLine>> GetBatchJobResultsParsedAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls a batch job until its status is terminal (compared case-insensitively).
    /// </summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="pollInterval">The wait between polls; default 2 s. <see cref="TimeSpan.Zero"/> polls without waiting.</param>
    /// <param name="timeout">The longest total wait; default 5 minutes.</param>
    /// <param name="terminalStatuses">Statuses that end polling; default <see cref="DrishtiConstants.BatchJobTerminalStatuses"/>.</param>
    /// <param name="cancellationToken">Stops polling.</param>
    /// <returns>The finished job.</returns>
    /// <exception cref="DrishtiException">The job did not finish within <paramref name="timeout"/>.</exception>
    Task<BatchJob> WaitForBatchJobCompletionAsync(
        string jobId,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        IEnumerable<string>? terminalStatuses = null,
        CancellationToken cancellationToken = default);

    /// <summary>Submits a batch job, then polls it until it finishes.</summary>
    /// <param name="request">The file and options.</param>
    /// <param name="pollInterval">The wait between polls; default 2 s.</param>
    /// <param name="timeout">The longest total wait; default 5 minutes.</param>
    /// <param name="cancellationToken">Cancels the submit and stops polling.</param>
    /// <returns>The finished job.</returns>
    /// <exception cref="DrishtiException">The submit response had no job id, or the job did not finish in time.</exception>
    Task<BatchJob> SubmitBatchJobAndWaitAsync(
        BatchJobRequest request,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a live-feed session that uses this client's API key, base URL and headers. The
    /// session connects when you call <see cref="IDrishtiWebSocketSession.StartAsync"/> or
    /// <see cref="IDrishtiWebSocketSession.RunAsync"/>.
    /// </summary>
    /// <param name="configure">Adjusts reconnect, heartbeat and subscribe settings.</param>
    /// <returns>A new, unconnected session. Dispose it when done.</returns>
    IDrishtiWebSocketSession CreateWebSocketSession(Action<DrishtiWebSocketOptions>? configure = null);
}
