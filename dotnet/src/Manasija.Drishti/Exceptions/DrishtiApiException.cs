using System.Globalization;
using System.Text.Json;
using Manasija.Drishti.Internal;

namespace Manasija.Drishti.Exceptions;

/// <summary>
/// The API answered with an error status (400 or above) after all retries were used.
/// </summary>
/// <remarks>
/// The server sends errors as <c>{"error": {"code", "message", "details"}}</c>; older routes send
/// <c>{"detail": "..."}</c>. <see cref="Detail"/> and <see cref="ErrorCode"/> read either shape.
/// </remarks>
public sealed class DrishtiApiException : DrishtiException
{
    /// <summary>Creates an exception for an error response.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="bodyJson">The parsed JSON body, when the response was JSON.</param>
    /// <param name="bodyText">The raw response text, or <see langword="null"/> when the body was empty.</param>
    public DrishtiApiException(int statusCode, JsonElement? bodyJson, string? bodyText)
        : this(statusCode, bodyJson, bodyText, null)
    {
    }

    /// <summary>Creates an exception for an error response, keeping its headers.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="bodyJson">The parsed JSON body, when the response was JSON.</param>
    /// <param name="bodyText">The raw response text, or <see langword="null"/> when the body was empty.</param>
    /// <param name="headers">The response headers.</param>
    public DrishtiApiException(int statusCode, JsonElement? bodyJson, string? bodyText, IReadOnlyDictionary<string, string>? headers)
        : base(BuildMessage(statusCode, bodyJson, bodyText))
    {
        StatusCode = statusCode;
        BodyJson = bodyJson;
        BodyText = bodyText;
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers ?? new Dictionary<string, string>())
        {
            copy[header.Key] = header.Value;
        }

        Headers = copy;
    }

    /// <summary>The HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>The parsed JSON error body, or <see langword="null"/> when the body was not JSON.</summary>
    public JsonElement? BodyJson { get; }

    /// <summary>The raw response body, or <see langword="null"/> when it was empty.</summary>
    public string? BodyText { get; }

    /// <summary>The response headers, keyed case-insensitively (for example <c>X-RateLimit-Remaining</c>).</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>
    /// How long the server asked you to wait, from the <c>Retry-After</c> header (seconds or an
    /// HTTP date). Sent with <c>429</c> responses. The client's automatic retries do not wait this
    /// long, so use it when you retry a rate-limited call yourself.
    /// </summary>
    public TimeSpan? RetryAfter
    {
        get
        {
            if (!Headers.TryGetValue("Retry-After", out var value))
            {
                return null;
            }

            if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
            {
                return TimeSpan.FromSeconds(seconds);
            }

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                var wait = date - DateTimeOffset.UtcNow;
                return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
            }

            return null;
        }
    }

    /// <summary>
    /// The most useful error text: <c>error.message</c>, else <c>detail</c>, else the raw body.
    /// </summary>
    public string? Detail => ExtractDetail(BodyJson) ?? BodyText;

    /// <summary>The machine-readable <c>error.code</c> (for example <c>rate_limit_exceeded</c>), when present.</summary>
    public string? ErrorCode
    {
        get
        {
            if (BodyJson is { ValueKind: JsonValueKind.Object } body
                && body.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var code))
            {
                return code.ValueKind == JsonValueKind.String ? code.GetString() : code.GetRawText();
            }

            return null;
        }
    }

    private static string BuildMessage(int statusCode, JsonElement? bodyJson, string? bodyText)
    {
        var detail = ExtractErrorMessage(bodyJson) ?? bodyJson?.GetRawText() ?? bodyText ?? string.Empty;
        return Messages.Format("ApiError", statusCode, detail);
    }

    private static string? ExtractErrorMessage(JsonElement? bodyJson)
    {
        if (bodyJson is { ValueKind: JsonValueKind.Object } body
            && body.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.String)
        {
            return message.GetString();
        }

        return null;
    }

    private static string? ExtractDetail(JsonElement? bodyJson)
    {
        var message = ExtractErrorMessage(bodyJson);
        if (message is not null)
        {
            return message;
        }

        if (bodyJson is { ValueKind: JsonValueKind.Object } body && body.TryGetProperty("detail", out var detail))
        {
            return detail.ValueKind == JsonValueKind.String ? detail.GetString() : detail.GetRawText();
        }

        return null;
    }
}
