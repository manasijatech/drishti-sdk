using System.Text;
using Manasija.Drishti.Internal;

namespace Manasija.Drishti.Params;

/// <summary>A symbol and quarter pair for <c>POST /v1/concalls/transcripts</c>.</summary>
/// <param name="Symbol">The NSE symbol.</param>
/// <param name="Quarter">The fiscal quarter, such as <c>q4_26</c>.</param>
public sealed record TranscriptLookup(string Symbol, string Quarter);

/// <summary>A portfolio position for a daily summary.</summary>
/// <param name="Symbol">The NSE symbol.</param>
public sealed record PortfolioPosition(string Symbol)
{
    /// <summary>The portfolio weight, from 0 to 1. Omit to let the API weight positions equally.</summary>
    public double? Exposure { get; init; }

    /// <summary>A label of your own, such as <c>core</c>.</summary>
    public string? Label { get; init; }
}

/// <summary>
/// The body of <c>POST /v1/daily-summary/</c>. Set <see cref="Portfolio"/> or <see cref="Symbols"/>.
/// Unset fields are not sent, so the API applies its own defaults.
/// </summary>
public sealed record DailySummaryRequest
{
    /// <summary><c>exposure</c>, <c>intraday_movements</c> or <c>news_context</c>.</summary>
    public string? Mode { get; init; }

    /// <summary>Portfolio positions.</summary>
    public IReadOnlyList<PortfolioPosition>? Portfolio { get; init; }

    /// <summary>A watchlist of NSE symbols.</summary>
    public IReadOnlyList<string>? Symbols { get; init; }

    /// <summary>Specific items to summarise.</summary>
    public IReadOnlyList<object>? Items { get; init; }

    /// <summary>How many days of news to consider.</summary>
    public int? ContextWindowDays { get; init; }
}

/// <summary>A batch job submission for <c>POST /v1/batch/jobs</c>.</summary>
public sealed record BatchJobRequest
{
    /// <summary>Creates a request from file bytes.</summary>
    /// <param name="fileName">The file name sent with the upload, such as <c>batch.jsonl</c>.</param>
    /// <param name="fileContent">The JSONL content; build it with <see cref="Batch.BatchJsonl.BuildInput"/>.</param>
    public BatchJobRequest(string fileName, byte[] fileContent)
    {
        FileName = Guard.NotBlank(fileName, nameof(fileName));
        FileContent = Guard.NotNull(fileContent, nameof(fileContent));
    }

    /// <summary>Creates a request from JSONL text.</summary>
    /// <param name="fileName">The file name sent with the upload.</param>
    /// <param name="fileContent">The JSONL text.</param>
    public BatchJobRequest(string fileName, string fileContent)
        : this(fileName, Encoding.UTF8.GetBytes(Guard.NotNull(fileContent, nameof(fileContent))))
    {
    }

    /// <summary>Creates a request from JSONL text with the default file name, <c>batch.jsonl</c>.</summary>
    /// <param name="fileContent">The JSONL text.</param>
    public BatchJobRequest(string fileContent)
        : this("batch.jsonl", fileContent)
    {
    }

    /// <summary>The file name.</summary>
    public string FileName { get; }

    /// <summary>The JSONL bytes.</summary>
    public byte[] FileContent { get; }

    /// <summary>A display name for the job.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Metadata to attach, as a JSON string.</summary>
    public string? Metadata { get; init; }

    /// <summary>Content retention preference.</summary>
    public ContentRetention? ContentRetention { get; init; }
}
