using System.Text.Json;
using System.Text.Json.Serialization;

namespace Manasija.Drishti.Models;

/// <summary>
/// An earnings result. Detail fields are set when the request used <c>detailed=true</c>.
/// </summary>
/// <remarks>
/// When the API sends only <c>earnings_table_extraction</c>, the SDK copies it to
/// <c>earnings_table</c>, as every Drishti SDK does.
/// </remarks>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class Earnings(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The earnings id.</summary>
    public string? Id => GetString("id");

    /// <summary>The NSE symbol.</summary>
    public string? Symbol => GetString("symbol");

    /// <summary>The BSE scrip code.</summary>
    public string? ScripCode => GetString("scrip_code");

    /// <summary>The company name.</summary>
    public string? CompanyName => GetString("company_name");

    /// <summary>An image URL.</summary>
    public string? Image => GetString("image");

    /// <summary>The fiscal quarter, such as <c>q4_26</c>.</summary>
    public string? Quarter => GetString("quarter");

    /// <summary>The filing date.</summary>
    public string? Date => GetString("date");

    /// <summary>A short AI summary.</summary>
    public string? Summary => GetString("summary");

    /// <summary>Whether the result is significant (detailed only).</summary>
    public bool? EarningsSignificant => GetBoolean("earnings_significant");

    /// <summary>The extracted results table (detailed only).</summary>
    public JsonElement? EarningsTable => GetElement("earnings_table");

    /// <summary>
    /// The 1-indexed pages of the source PDF that hold the results statements (detailed only;
    /// empty when missing). Pass one to <c>GetEarningsCitationPageAsync</c>.
    /// </summary>
    public IReadOnlyList<int> ResultsPages => GetElement("results_pages") is { ValueKind: JsonValueKind.Array } pages
        ? pages.EnumerateArray()
            .Where(page => page.ValueKind == JsonValueKind.Number && page.TryGetInt32(out _))
            .Select(page => page.GetInt32())
            .ToList()
        : [];

    /// <summary>The source filing URL (WebSocket payloads only).</summary>
    public string? AttachmentUrl => GetString("attachment_url");
}

/// <summary>A scheduled earnings announcement.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class UpcomingEarnings(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The record id.</summary>
    public string? Id => GetString("id");

    /// <summary>The exchange event id.</summary>
    public string? EventId => GetString("event_id");

    /// <summary>The board meeting description.</summary>
    public string? BmDesc => GetString("bm_desc");

    /// <summary>The intimation text.</summary>
    public string? Body => GetString("body");

    /// <summary>The company name.</summary>
    public string? Company => GetString("company");

    /// <summary>The meeting date.</summary>
    public string? Date => GetString("date");

    /// <summary>The meeting purpose.</summary>
    public string? Purpose => GetString("purpose");

    /// <summary>The NSE symbol.</summary>
    public string? Symbol => GetString("symbol");

    /// <summary>The intimation title.</summary>
    public string? Title => GetString("title");

    /// <summary>The fiscal quarter.</summary>
    public string? Quarter => GetString("quarter");
}

/// <summary>An earnings call (concall) with AI analysis.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class Concall(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The concall id.</summary>
    public string? Id => GetString("id");

    /// <summary>The NSE symbol.</summary>
    public string? Symbol => GetString("symbol");

    /// <summary>The fiscal quarter.</summary>
    public string? Quarter => GetString("quarter");

    /// <summary>The call date.</summary>
    public string? Date => GetString("date");

    /// <summary>A short structured analysis.</summary>
    public JsonElement? ShortAnalysis => GetElement("short_analysis");

    /// <summary>A pre-signed transcript URL.</summary>
    public string? TranscriptUrl => GetString("transcript_url");

    /// <summary>A pre-signed audio URL.</summary>
    public string? AudioUrl => GetString("audio_url");

    /// <summary>Sentiment analysis: <c>{"sentiment": {"key_indicators": {"positive": [], "negative": []}}}</c>.</summary>
    public JsonElement? SentimentAnalysis => GetElement("sentiment_analysis");

    /// <summary>The full analysis (detailed only).</summary>
    public JsonElement? ExpandedAnalysis => GetElement("expanded_analysis");
}

/// <summary>A scheduled concall.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class UpcomingConcall(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The record id.</summary>
    public string? Id => GetString("id");

    /// <summary>The NSE symbol.</summary>
    public string? Symbol => GetString("symbol");

    /// <summary>The fiscal quarter.</summary>
    public string? Quarter => GetString("quarter");

    /// <summary>The meeting date.</summary>
    public string? MeetingDate => GetString("meeting_date");

    /// <summary>The intimation attachment URL (detailed only).</summary>
    public string? IntimationAttachment => GetString("intimation_attachment");
}

/// <summary>Pre-signed transcript and audio URLs for one concall.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class ConcallArtifactUrls(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The transcript URL.</summary>
    public string? TranscriptUrl => GetString("transcript_url");

    /// <summary>The audio URL.</summary>
    public string? AudioUrl => GetString("audio_url");

    /// <summary>Seconds until the URLs expire.</summary>
    public long? ExpiresIn => GetInt64("expires_in");
}

/// <summary>One result of a batch transcript lookup.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class ConcallTranscriptLookupItem(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The requested symbol.</summary>
    public string? Symbol => GetString("symbol");

    /// <summary>The requested quarter.</summary>
    public string? Quarter => GetString("quarter");

    /// <summary>The concall id, when found.</summary>
    public string? Id => GetString("id");

    /// <summary><c>ready</c>, <c>not_found</c> or <c>no_transcript</c>.</summary>
    public string? Status => GetString("status");

    /// <summary>The transcript URL.</summary>
    public string? TranscriptUrl => GetString("transcript_url");

    /// <summary>The audio URL.</summary>
    public string? AudioUrl => GetString("audio_url");

    /// <summary>Seconds until the URLs expire.</summary>
    public long? ExpiresIn => GetInt64("expires_in");

    /// <summary>A human-readable explanation when not ready.</summary>
    public string? Message => GetString("message");
}
