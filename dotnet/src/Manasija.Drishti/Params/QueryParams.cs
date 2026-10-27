using Manasija.Drishti.Http;

namespace Manasija.Drishti.Params;

/// <summary>Base type for endpoint filters that become query parameters.</summary>
public abstract record QueryParams
{
    /// <summary>Writes the set filters to <paramref name="query"/>.</summary>
    /// <param name="query">The query being built.</param>
    protected internal abstract void WriteTo(QueryBuilder query);

    /// <summary>Returns the query parameters these filters produce, in order.</summary>
    /// <returns>The name/value pairs.</returns>
    public IReadOnlyList<KeyValuePair<string, string>> ToQuery()
    {
        var builder = new QueryBuilder();
        WriteTo(builder);
        return builder.Pairs;
    }
}

/// <summary>Filters for <c>GET /v1/news</c>.</summary>
public sealed record NewsParams : QueryParams
{
    /// <summary>NSE symbols, such as <c>TCS</c>.</summary>
    public IReadOnlyList<string>? Symbols { get; init; }

    /// <summary>BSE scrip codes, such as <c>532540</c>.</summary>
    public IReadOnlyList<string>? ScripCodes { get; init; }

    /// <summary>Sectors, such as <c>Bank</c> or <c>IT</c> (case-insensitive exact match).</summary>
    public IReadOnlyList<string>? Sectors { get; init; }

    /// <summary><c>positive</c>, <c>negative</c> or <c>neutral</c>.</summary>
    public string? Sentiment { get; init; }

    /// <summary>Start date or timestamp (ISO 8601).</summary>
    public string? From { get; init; }

    /// <summary>End date or timestamp (ISO 8601).</summary>
    public string? To { get; init; }

    /// <summary>The 1-based page.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page.</summary>
    public int? Limit { get; init; }

    /// <inheritdoc />
    protected internal override void WriteTo(QueryBuilder query) => query
        .Add("symbols", Symbols).Add("scrip_codes", ScripCodes).Add("sectors", Sectors).Add("sentiment", Sentiment)
        .Add("from", From).Add("to", To).Add("page", Page).Add("limit", Limit);
}

/// <summary>Filters for <c>GET /v1/block-deals</c>.</summary>
public sealed record BlockDealsParams : QueryParams
{
    /// <summary>NSE symbols.</summary>
    public IReadOnlyList<string>? Symbols { get; init; }

    /// <summary><c>NSE</c> or <c>BSE</c>.</summary>
    public string? Exchange { get; init; }

    /// <summary>Start date (ISO 8601).</summary>
    public string? From { get; init; }

    /// <summary>End date (ISO 8601).</summary>
    public string? To { get; init; }

    /// <summary>The 1-based page.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page.</summary>
    public int? Limit { get; init; }

    /// <inheritdoc />
    protected internal override void WriteTo(QueryBuilder query) => query
        .Add("symbols", Symbols).Add("exchange", Exchange).Add("from", From).Add("to", To)
        .Add("page", Page).Add("limit", Limit);
}

/// <summary>Filters for <c>GET /v1/symbols/metadata</c>.</summary>
public sealed record SymbolsMetadataParams : QueryParams
{
    /// <summary>NSE symbols.</summary>
    public IReadOnlyList<string>? Symbols { get; init; }

    /// <summary>BSE scrip codes.</summary>
    public IReadOnlyList<string>? ScripCodes { get; init; }

    /// <inheritdoc />
    protected internal override void WriteTo(QueryBuilder query) => query
        .Add("symbols", Symbols).Add("scrip_codes", ScripCodes);
}

/// <summary>Filters for <c>GET /v1/announcements</c>.</summary>
public sealed record AnnouncementsParams : QueryParams
{
    /// <summary>NSE symbols.</summary>
    public IReadOnlyList<string>? Symbols { get; init; }

    /// <summary>BSE scrip codes.</summary>
    public IReadOnlyList<string>? ScripCodes { get; init; }

    /// <summary>Categories, from <see cref="IDrishtiClient.GetAnnouncementsCategoriesAsync"/>.</summary>
    public IReadOnlyList<string>? Categories { get; init; }

    /// <summary>Start date (ISO 8601).</summary>
    public string? From { get; init; }

    /// <summary>End date (ISO 8601).</summary>
    public string? To { get; init; }

    /// <summary>Return detail fields (long summary, extracted information).</summary>
    public bool? Detailed { get; init; }

    /// <summary>Only important (<see langword="true"/>) or only other (<see langword="false"/>) announcements.</summary>
    public bool? Important { get; init; }

    /// <summary>The 1-based page.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page.</summary>
    public int? Limit { get; init; }

    /// <inheritdoc />
    protected internal override void WriteTo(QueryBuilder query) => query
        .Add("symbols", Symbols).Add("scrip_codes", ScripCodes).Add("categories", Categories)
        .Add("from", From).Add("to", To).Add("detailed", Detailed).Add("important", Important)
        .Add("page", Page).Add("limit", Limit);
}

/// <summary>Filters for <c>GET /v1/earnings/index</c> and <c>GET /v1/concalls/index</c>.</summary>
public sealed record IndexParams : QueryParams
{
    /// <summary>NSE symbols.</summary>
    public IReadOnlyList<string>? Symbols { get; init; }

    /// <summary>BSE scrip codes.</summary>
    public IReadOnlyList<string>? ScripCodes { get; init; }

    /// <summary>Start date (ISO 8601).</summary>
    public string? From { get; init; }

    /// <summary>End date (ISO 8601).</summary>
    public string? To { get; init; }

    /// <summary>The fiscal quarter, such as <c>q4_26</c>.</summary>
    public string? Quarter { get; init; }

    /// <summary>The 1-based page.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page.</summary>
    public int? Limit { get; init; }

    /// <inheritdoc />
    protected internal override void WriteTo(QueryBuilder query) => query
        .Add("symbols", Symbols).Add("scrip_codes", ScripCodes).Add("from", From).Add("to", To)
        .Add("quarter", Quarter).Add("page", Page).Add("limit", Limit);
}

/// <summary>Filters for <c>GET /v1/earnings</c>.</summary>
public sealed record EarningsParams : QueryParams
{
    /// <summary>Earnings ids.</summary>
    public IReadOnlyList<string>? Ids { get; init; }

    /// <summary>NSE symbols.</summary>
    public IReadOnlyList<string>? Symbols { get; init; }

    /// <summary>BSE scrip codes.</summary>
    public IReadOnlyList<string>? ScripCodes { get; init; }

    /// <summary>Start date (ISO 8601).</summary>
    public string? From { get; init; }

    /// <summary>End date (ISO 8601).</summary>
    public string? To { get; init; }

    /// <summary>The fiscal quarter, such as <c>q4_26</c>.</summary>
    public string? Quarter { get; init; }

    /// <summary>Return detail fields (earnings table).</summary>
    public bool? Detailed { get; init; }

    /// <summary>The 1-based page.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page.</summary>
    public int? Limit { get; init; }

    /// <inheritdoc />
    protected internal override void WriteTo(QueryBuilder query) => query
        .Add("ids", Ids).Add("symbols", Symbols).Add("scrip_codes", ScripCodes).Add("from", From)
        .Add("to", To).Add("quarter", Quarter).Add("detailed", Detailed).Add("page", Page).Add("limit", Limit);
}

/// <summary>
/// Selects one earnings result or concall: <c>GET /v1/earnings/detail</c> and
/// <c>GET /v1/concalls/detail</c>. Set <see cref="Symbol"/> or <see cref="ScripCode"/>.
/// </summary>
/// <param name="Quarter">The fiscal quarter, such as <c>q4_26</c>.</param>
public sealed record DetailParams(string Quarter) : QueryParams
{
    /// <summary>The NSE symbol.</summary>
    public string? Symbol { get; init; }

    /// <summary>The BSE scrip code.</summary>
    public string? ScripCode { get; init; }

    /// <summary>Return detail fields.</summary>
    public bool? Detailed { get; init; }

    /// <inheritdoc />
    protected internal override void WriteTo(QueryBuilder query) => query
        .Add("quarter", Quarter).Add("symbol", Symbol).Add("scrip_code", ScripCode).Add("detailed", Detailed);
}

/// <summary>Selects one concall transcript: <c>GET /v1/concalls/transcript</c>. Set <see cref="Symbol"/> or <see cref="ScripCode"/>.</summary>
/// <param name="Quarter">The fiscal quarter, such as <c>q4_26</c>.</param>
public sealed record TranscriptParams(string Quarter) : QueryParams
{
    /// <summary>The NSE symbol.</summary>
    public string? Symbol { get; init; }

    /// <summary>The BSE scrip code.</summary>
    public string? ScripCode { get; init; }

    /// <inheritdoc />
    protected internal override void WriteTo(QueryBuilder query) => query
        .Add("quarter", Quarter).Add("symbol", Symbol).Add("scrip_code", ScripCode);
}

/// <summary>Filters for <c>GET /v1/earnings/upcoming</c>.</summary>
public sealed record UpcomingEarningsParams : QueryParams
{
    /// <summary>NSE symbols.</summary>
    public IReadOnlyList<string>? Symbols { get; init; }

    /// <summary>BSE scrip codes.</summary>
    public IReadOnlyList<string>? ScripCodes { get; init; }

    /// <summary>The 1-based page.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page.</summary>
    public int? Limit { get; init; }

    /// <inheritdoc />
    protected internal override void WriteTo(QueryBuilder query) => query
        .Add("symbols", Symbols).Add("scrip_codes", ScripCodes).Add("page", Page).Add("limit", Limit);
}

/// <summary>Filters for <c>GET /v1/concalls</c>.</summary>
public sealed record ConcallsParams : QueryParams
{
    /// <summary>NSE symbols.</summary>
    public IReadOnlyList<string>? Symbols { get; init; }

    /// <summary>BSE scrip codes.</summary>
    public IReadOnlyList<string>? ScripCodes { get; init; }

    /// <summary>Start date (ISO 8601).</summary>
    public string? From { get; init; }

    /// <summary>End date (ISO 8601).</summary>
    public string? To { get; init; }

    /// <summary>Return the expanded analysis.</summary>
    public bool? Detailed { get; init; }

    /// <summary>The 1-based page.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page.</summary>
    public int? Limit { get; init; }

    /// <inheritdoc />
    protected internal override void WriteTo(QueryBuilder query) => query
        .Add("symbols", Symbols).Add("scrip_codes", ScripCodes).Add("from", From).Add("to", To)
        .Add("detailed", Detailed).Add("page", Page).Add("limit", Limit);
}

/// <summary>Filters for <c>GET /v1/concalls/upcoming</c>.</summary>
public sealed record UpcomingConcallsParams : QueryParams
{
    /// <summary>NSE symbols.</summary>
    public IReadOnlyList<string>? Symbols { get; init; }

    /// <summary>BSE scrip codes.</summary>
    public IReadOnlyList<string>? ScripCodes { get; init; }

    /// <summary>Return the intimation attachment.</summary>
    public bool? Detailed { get; init; }

    /// <summary>The 1-based page.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page.</summary>
    public int? Limit { get; init; }

    /// <inheritdoc />
    protected internal override void WriteTo(QueryBuilder query) => query
        .Add("symbols", Symbols).Add("scrip_codes", ScripCodes).Add("detailed", Detailed)
        .Add("page", Page).Add("limit", Limit);
}

/// <summary>Filters for <c>GET /v1/alerts</c>.</summary>
public sealed record AlertsParams : QueryParams
{
    /// <summary>NSE symbols.</summary>
    public IReadOnlyList<string>? Symbols { get; init; }

    /// <summary>BSE scrip codes.</summary>
    public IReadOnlyList<string>? ScripCodes { get; init; }

    /// <summary>Alert types; see <see cref="DrishtiConstants.AlertTypes"/>.</summary>
    public IReadOnlyList<string>? Types { get; init; }

    /// <summary>Start date (ISO 8601).</summary>
    public string? From { get; init; }

    /// <summary>End date (ISO 8601).</summary>
    public string? To { get; init; }

    /// <summary>Only important alerts.</summary>
    public bool? Important { get; init; }

    /// <summary>The 1-based page.</summary>
    public int? Page { get; init; }

    /// <summary>Items per page.</summary>
    public int? Limit { get; init; }

    /// <inheritdoc />
    protected internal override void WriteTo(QueryBuilder query) => query
        .Add("symbols", Symbols).Add("scrip_codes", ScripCodes).Add("type", Types).Add("from", From)
        .Add("to", To).Add("important", Important).Add("page", Page).Add("limit", Limit);
}
