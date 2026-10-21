namespace Manasija.Drishti.Exceptions;

/// <summary>
/// No response was received (connection failure, DNS error or timeout) after all retries.
/// </summary>
public sealed class DrishtiNetworkException : DrishtiException
{
    /// <summary>Creates an exception for a failed request.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The last transport error.</param>
    public DrishtiNetworkException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
