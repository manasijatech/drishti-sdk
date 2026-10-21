using System.Globalization;
using System.Resources;

namespace Manasija.Drishti.Internal;

/// <summary>Error and exception messages, read from <c>Resources/ErrorMessages.resx</c>.</summary>
internal static class Messages
{
    private static readonly ResourceManager ResourceManager =
        new("Manasija.Drishti.Resources.ErrorMessages", typeof(Messages).Assembly);

    public static string Get(string key)
        => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}

/// <summary>
/// Structured log message templates, read from <c>Resources/LogMessages.resx</c>. Templates keep
/// their <c>{Placeholder}</c> names so log sinks can index the values.
/// </summary>
internal static class LogMessages
{
    private static readonly ResourceManager ResourceManager =
        new("Manasija.Drishti.Resources.LogMessages", typeof(LogMessages).Assembly);

    public static string RetryingRequest => Get(nameof(RetryingRequest));

    public static string FollowingRedirect => Get(nameof(FollowingRedirect));

    public static string WebSocketConnected => Get(nameof(WebSocketConnected));

    public static string WebSocketConnectFailed => Get(nameof(WebSocketConnectFailed));

    public static string WebSocketReconnecting => Get(nameof(WebSocketReconnecting));

    public static string WebSocketReconnectWarning => Get(nameof(WebSocketReconnectWarning));

    public static string WebSocketSubscribed => Get(nameof(WebSocketSubscribed));

    public static string WebSocketSubscribeFailed => Get(nameof(WebSocketSubscribeFailed));

    public static string WebSocketForcingReconnect => Get(nameof(WebSocketForcingReconnect));

    public static string WebSocketHandlerFailed => Get(nameof(WebSocketHandlerFailed));

    private static string Get(string key)
        => ResourceManager.GetString(key, CultureInfo.InvariantCulture) ?? key;
}
