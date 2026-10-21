namespace Manasija.Drishti.Exceptions;

/// <summary>
/// A WebSocket operation failed: the socket is not connected, a subscribe was rejected or timed
/// out, or the session was closed.
/// </summary>
public sealed class DrishtiWebSocketException : DrishtiException
{
    /// <summary>Creates an exception with an optional server error code.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="code">The server error code (for example <c>forbidden</c>), when one was sent.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public DrishtiWebSocketException(string message, string? code = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>The server error code, such as <c>forbidden</c> or <c>bad_request</c>.</summary>
    public string? Code { get; }
}
