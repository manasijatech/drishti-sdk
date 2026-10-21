namespace Manasija.Drishti.Internal;

/// <summary>Argument validation that behaves the same on every target framework.</summary>
internal static class Guard
{
    public static T NotNull<T>(T? value, string paramName)
        where T : class
        => value ?? throw new ArgumentNullException(paramName);

    public static string NotBlank(string? value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }

        if (value.Trim().Length == 0)
        {
            throw new ArgumentException(Messages.Format("ValueMustNotBeBlank", paramName), paramName);
        }

        return value;
    }

    public static TimeSpan NotNegative(TimeSpan value, string paramName)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(paramName, value, Messages.Format("ValueMustNotBeNegative", paramName));
        }

        return value;
    }

    public static int NotNegative(int value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, Messages.Format("ValueMustNotBeNegative", paramName));
        }

        return value;
    }
}
