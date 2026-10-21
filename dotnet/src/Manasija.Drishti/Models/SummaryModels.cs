using System.Text.Json;
using System.Text.Json.Serialization;

namespace Manasija.Drishti.Models;

/// <summary>A generated daily summary.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class SummaryResponse(JsonElement json) : DrishtiObject(json)
{
    /// <summary><c>success</c>, <c>success_no_news</c>, <c>success_no_signal</c> or <c>error</c>.</summary>
    public string? Status => GetString("status");

    /// <summary>The summary text (Markdown).</summary>
    public string? Summary => GetString("summary");

    /// <summary>The error message when <see cref="Status"/> is <c>error</c>.</summary>
    public string? Error => GetString("error");

    /// <summary>Details about how the summary was produced.</summary>
    public SummaryDetails? Details => GetObject<SummaryDetails>("details");
}

/// <summary>Details about how a summary was produced.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class SummaryDetails(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The number of portfolio entries.</summary>
    public long? PortfolioSize => GetInt64("portfolio_size");

    /// <summary>The number of symbols submitted.</summary>
    public long? SubmittedSymbolCount => GetInt64("submitted_symbol_count");

    /// <summary>The number of symbols processed.</summary>
    public long? SymbolsProcessed => GetInt64("symbols_processed");

    /// <summary>The request id, for support.</summary>
    public string? RequestId => GetString("request_id");

    /// <summary><c>exposure</c>, <c>intraday_movements</c> or <c>news_context</c>.</summary>
    public string? Mode => GetString("mode");

    /// <summary><c>portfolio</c> or <c>watchlist</c>.</summary>
    public string? InputType => GetString("input_type");

    /// <summary>The news window, in days.</summary>
    public long? ContextWindowDays => GetInt64("context_window_days");
}

/// <summary>A batch job.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class BatchJob(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The job id.</summary>
    public string? Id => GetString("id");

    /// <summary>The object type, <c>batch_job</c>.</summary>
    public string? Object => GetString("object");

    /// <summary>The display name you gave the job.</summary>
    public string? DisplayName => GetString("display_name");

    /// <summary>The status; see <see cref="DrishtiConstants.BatchJobStatuses"/>.</summary>
    public string? Status => GetString("status");

    /// <summary>Whether <see cref="Status"/> is terminal (compared case-insensitively).</summary>
    public bool IsTerminal => Status is { } status && DrishtiConstants.BatchJobTerminalStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);

    /// <summary>When the job was created (Unix seconds).</summary>
    public long? CreatedAt => GetInt64("created_at");

    /// <summary>When the job started running (Unix seconds).</summary>
    public long? InProgressAt => GetInt64("in_progress_at");

    /// <summary>When the job completed (Unix seconds).</summary>
    public long? CompletedAt => GetInt64("completed_at");

    /// <summary>When the job failed (Unix seconds).</summary>
    public long? FailedAt => GetInt64("failed_at");

    /// <summary>When the job was cancelled (Unix seconds).</summary>
    public long? CancelledAt => GetInt64("cancelled_at");

    /// <summary>Request counts: <c>{"total", "completed", "failed"}</c>.</summary>
    public JsonElement? RequestCounts => GetElement("request_counts");

    /// <summary>The metadata you attached.</summary>
    public JsonElement? Metadata => GetElement("metadata");

    /// <summary>Credits charged for the job.</summary>
    public JsonElement? Billing => GetElement("billing");
}

/// <summary>A page of batch jobs: <c>{"object": "list", "data": [...]}</c>.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class BatchJobList(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The object type, <c>list</c>.</summary>
    public string? Object => GetString("object");

    /// <summary>The jobs, newest first.</summary>
    public IReadOnlyList<BatchJob> Data => GetList<BatchJob>("data");
}

/// <summary>One line of a batch job's results file.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class BatchResultLine(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The result id.</summary>
    public string? Id => GetString("id");

    /// <summary>The <c>custom_id</c> you set on the input line.</summary>
    public string? CustomId => GetString("custom_id");

    /// <summary>The summary status for this line.</summary>
    public string? Status => GetString("status");

    /// <summary>The response: <c>{"status_code", "body": {"summary", "details"}}</c>.</summary>
    public JsonElement? Response => GetElement("response");

    /// <summary>The generated summary, read from <c>response.body.summary</c>.</summary>
    public string? Summary => Response is { ValueKind: JsonValueKind.Object } response
        && response.TryGetProperty("body", out var body)
        && body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty("summary", out var summary)
        && summary.ValueKind == JsonValueKind.String
        ? summary.GetString()
        : null;

    /// <summary>The error for this line, when it failed.</summary>
    public JsonElement? Error => GetElement("error");
}
