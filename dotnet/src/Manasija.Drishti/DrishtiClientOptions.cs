using System.ComponentModel.DataAnnotations;

namespace Manasija.Drishti;

/// <summary>
/// Settings for <see cref="DrishtiClient"/>. Bind them from configuration (the <c>Drishti</c>
/// section by default) or set them in code.
/// </summary>
/// <example>
/// <code language="json">
/// {
///   "Drishti": {
///     "ApiKey": "...",
///     "Timeout": "00:00:30",
///     "MaxRetries": 3
///   }
/// }
/// </code>
/// </example>
public sealed class DrishtiClientOptions
{
    /// <summary>The default configuration section name.</summary>
    public const string SectionName = "Drishti";

    /// <summary>The default <c>User-Agent</c> header.</summary>
    public const string DefaultUserAgent = "drishti-sdk-dotnet/1.0.0";

    /// <summary>Your API key, sent as <c>X-API-Key</c>. Required.</summary>
    [Required]
    [NotEmptyOrWhitespace]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The API base URL. A path prefix (for a proxy) is kept; trailing slashes are trimmed.</summary>
    [Required]
    [AbsoluteHttpUrl]
    public string BaseUrl { get; set; } = DrishtiConstants.DefaultBaseUrl;

    /// <summary>Extra headers sent with every request. They can never replace <c>X-API-Key</c>.</summary>
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many times to retry a failed request. 0 disables retries. Default 2.</summary>
    [Range(0, 100)]
    public int MaxRetries { get; set; } = 2;

    /// <summary>The delay before the first retry. Default 300 ms.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>The longest delay between retries. Default 5 s.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The factor each retry delay grows by. Default 2.</summary>
    [Range(1.0, 100.0)]
    public double RetryMultiplier { get; set; } = 2.0;

    /// <summary>The HTTP statuses that are retried. Network errors and timeouts are always retried.</summary>
    public IList<int> RetryOnStatuses { get; set; } = new List<int>(DrishtiConstants.DefaultRetryStatuses);

    /// <summary>How long to wait for each response. Default 60 s.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>The <c>User-Agent</c> header.</summary>
    [Required]
    public string UserAgent { get; set; } = DefaultUserAgent;
}

/// <summary>Rejects strings that are empty or contain only whitespace.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotEmptyOrWhitespaceAttribute : ValidationAttribute
{
    /// <summary>Creates the attribute.</summary>
    public NotEmptyOrWhitespaceAttribute()
        : base("The {0} field must not be empty or whitespace.")
    {
    }

    /// <inheritdoc />
    public override bool IsValid(object? value) => value is string text && text.Trim().Length > 0;
}

/// <summary>Requires an absolute <c>http</c> or <c>https</c> URL.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class AbsoluteHttpUrlAttribute : ValidationAttribute
{
    /// <summary>Creates the attribute.</summary>
    public AbsoluteHttpUrlAttribute()
        : base("The {0} field must be an absolute http or https URL.")
    {
    }

    /// <inheritdoc />
    public override bool IsValid(object? value)
        => value is string text
            && Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
