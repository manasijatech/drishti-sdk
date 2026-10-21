namespace Manasija.Drishti.Exceptions;

/// <summary>
/// Base type for every error the SDK raises on purpose. Catch it to handle all SDK failures;
/// catch a subclass for API, network or WebSocket failures only.
/// </summary>
public class DrishtiException : Exception
{
    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public DrishtiException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying cause.</param>
    public DrishtiException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
