using System.ComponentModel.DataAnnotations;

namespace Manasija.Drishti.WebSocket;

/// <summary>Settings for <see cref="DrishtiWebSocketSession"/>. Defaults match every Drishti SDK.</summary>
public sealed class DrishtiWebSocketOptions
{
    /// <summary>Your API key. Required.</summary>
    [Required]
    [NotEmptyOrWhitespace]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The REST base URL; the WebSocket URL is derived from it.</summary>
    [Required]
    [AbsoluteHttpUrl]
    public string BaseUrl { get; set; } = DrishtiConstants.DefaultBaseUrl;

    /// <summary>Extra headers for the opening handshake.</summary>
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The first reconnect delay. Default 1 s.</summary>
    public TimeSpan ReconnectInitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest reconnect delay. Default 30 s.</summary>
    public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The factor each reconnect delay grows by. Default 2.</summary>
    [Range(1.0, 100.0)]
    public double ReconnectBackoffMultiplier { get; set; } = 2.0;

    /// <summary>Random spread applied to each delay, as a fraction of it. Default 0.2 (±20%).</summary>
    [Range(0.0, 1.0)]
    public double ReconnectJitterRatio { get; set; } = 0.2;

    /// <summary>Warn after this many failed attempts at the maximum delay. Default 10.</summary>
    [Range(1, int.MaxValue)]
    public int ReconnectWarnAfterAttempts { get; set; } = 10;

    /// <summary>Reconnect when nothing arrives for this long. Default 90 s; zero disables the watchdog.</summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>How often the watchdog checks for silence. Default 15 s.</summary>
    public TimeSpan HeartbeatCheckInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How long to wait for a subscribe acknowledgement. Default 10 s.</summary>
    public TimeSpan SubscribeAckTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Subscribe attempts per connection before giving up. Default 10.</summary>
    [Range(1, int.MaxValue)]
    public int SubscribeMaxAttempts { get; set; } = 10;

    /// <summary>The first delay between subscribe attempts. Default 1 s.</summary>
    public TimeSpan SubscribeRetryInitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest delay between subscribe attempts. Default 30 s.</summary>
    public TimeSpan SubscribeRetryMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The factor each subscribe delay grows by. Default 2.</summary>
    [Range(1.0, 100.0)]
    public double SubscribeRetryBackoffMultiplier { get; set; } = 2.0;
}
