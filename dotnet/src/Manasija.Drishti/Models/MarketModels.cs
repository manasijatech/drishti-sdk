using System.Text.Json;
using System.Text.Json.Serialization;

namespace Manasija.Drishti.Models;

/// <summary>A news article with AI summaries.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class NewsItem(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The article id.</summary>
    public string? Id => GetString("id");

    /// <summary>The headline.</summary>
    public string? Title => GetString("title");

    /// <summary>A company-specific headline.</summary>
    public string? SpecificTitle => GetString("specific_title");

    /// <summary>A short AI summary.</summary>
    public string? Summary => GetString("summary");

    /// <summary>A longer AI summary.</summary>
    public string? LongSummary => GetString("long_summary");

    /// <summary>The company name.</summary>
    public string? Company => GetString("company");

    /// <summary>The publisher.</summary>
    public string? Source => GetString("source");

    /// <summary>The NSE symbol.</summary>
    public string? Symbol => GetString("symbol");

    /// <summary><c>positive</c>, <c>negative</c> or <c>neutral</c>.</summary>
    public string? Sentiment => GetString("sentiment");

    /// <summary>The article category.</summary>
    public string? ArticleType => GetString("article_type");

    /// <summary>The BSE scrip code.</summary>
    public string? ScripCode => GetString("scrip_code");

    /// <summary>The publication timestamp.</summary>
    public string? Date => GetString("date");

    /// <summary>The source article URL.</summary>
    public string? Link => GetString("link");
}

/// <summary>A block deal.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class BlockDealItem(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The deal id.</summary>
    public string? Id => GetString("id");

    /// <summary>The NSE symbol.</summary>
    public string? Symbol => GetString("symbol");

    /// <summary>The company name.</summary>
    public string? Company => GetString("company");

    /// <summary>The exchange, <c>NSE</c> or <c>BSE</c>.</summary>
    public string? Exchange => GetString("exchange");

    /// <summary>The trade value in crore rupees.</summary>
    public double? TradeValueCr => GetDouble("trade_value_cr");

    /// <summary>The number of shares traded.</summary>
    public double? Shares => GetDouble("shares");

    /// <summary>The trade price.</summary>
    public double? Price => GetDouble("price");

    /// <summary>The trade date.</summary>
    public string? Date => GetString("date");
}

/// <summary>Reference data for one listed symbol.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class SymbolMetadata(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The NSE symbol.</summary>
    public string? Symbol => GetString("symbol");

    /// <summary>The company name.</summary>
    public string? CompanyName => GetString("company_name");

    /// <summary>A logo URL.</summary>
    public string? Logo => GetString("logo");

    /// <summary>The market capitalisation.</summary>
    public double? MarketCap => GetDouble("market_cap");

    /// <summary>The sector.</summary>
    public string? Sector => GetString("sector");

    /// <summary>The basic industry.</summary>
    public string? BasicIndustry => GetString("basic_industry");

    /// <summary>The industry.</summary>
    public string? Industry => GetString("industry");

    /// <summary>The macro-economic indicator classification.</summary>
    public string? MacroEconomicIndicator => GetString("macro_economic_indicator");

    /// <summary>The BSE scrip code.</summary>
    public string? ScripCode => GetString("scrip_code");

    /// <summary>The ISIN.</summary>
    public string? Isin => GetString("isin");
}

/// <summary>A corporate announcement. Detail fields are set when the request used <c>detailed=true</c>.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class Announcement(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The announcement id.</summary>
    public string? Id => GetString("id");

    /// <summary>The NSE symbol.</summary>
    public string? Symbol => GetString("symbol");

    /// <summary>The company name.</summary>
    public string? CompanyName => GetString("company_name");

    /// <summary>An image URL.</summary>
    public string? Image => GetString("image");

    /// <summary>The filing date.</summary>
    public string? Date => GetString("date");

    /// <summary>A short AI summary.</summary>
    public string? Summary => GetString("summary");

    /// <summary>The primary category.</summary>
    public string? Category => GetString("category");

    /// <summary>A longer AI summary (detailed only).</summary>
    public string? LongSummary => GetString("long_summary");

    /// <summary>Other categories that apply (detailed only).</summary>
    public IReadOnlyList<string> RelatedCategories => GetStringList("related_categories");

    /// <summary>Structured data extracted from the filing (detailed only).</summary>
    public JsonElement? ExtractedInformation => GetElement("extracted_information");
}

/// <summary>Announcement categories, split by importance.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class AnnouncementCategories(JsonElement json) : DrishtiObject(json)
{
    /// <summary>Categories flagged important.</summary>
    public IReadOnlyList<string> Important => GetStringList("important");

    /// <summary>All other categories.</summary>
    public IReadOnlyList<string> NotImportant => GetStringList("not_important");
}

/// <summary>A market alert, such as a 52-week high or a volume spike.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class Alert(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The alert id.</summary>
    public string? Id => GetString("id");

    /// <summary>The NSE symbol.</summary>
    public string? Symbol => GetString("symbol");

    /// <summary>The alert type; see <see cref="DrishtiConstants.AlertTypes"/>. Falls back to <c>alert_type</c> on WebSocket payloads.</summary>
    public string? Type => GetString("type") ?? GetString("alert_type");

    /// <summary>Why the alert fired.</summary>
    public string? Reason => GetString("reason");

    /// <summary>When the alert fired.</summary>
    public string? Timestamp => GetString("timestamp");

    /// <summary>Delayed price context, for price alerts.</summary>
    public AlertPrice? Price => GetObject<AlertPrice>("price");

    /// <summary>The main drivers behind the alert.</summary>
    public IReadOnlyList<string> PrimaryDrivers => GetObject<JsonModel>("meta") is { } meta
        && meta.Json.TryGetProperty("primary_drivers", out var drivers)
        && drivers.ValueKind == JsonValueKind.Array
        ? drivers.EnumerateArray().Where(d => d.ValueKind == JsonValueKind.String).Select(d => d.GetString()!).ToList()
        : [];
}

/// <summary>Delayed market-price context attached to a price alert.</summary>
/// <param name="json">The JSON value.</param>
[JsonConverter(typeof(DrishtiObjectConverterFactory))]
public sealed class AlertPrice(JsonElement json) : DrishtiObject(json)
{
    /// <summary>The price.</summary>
    public double? Value => GetDouble("value");

    /// <summary>The percentage change.</summary>
    public double? ChangePercent => GetDouble("change_percent");

    /// <summary>When the price was observed.</summary>
    public string? AsOf => GetString("as_of");
}
