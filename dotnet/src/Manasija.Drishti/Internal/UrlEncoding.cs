using System.Text;

namespace Manasija.Drishti.Internal;

/// <summary>
/// Percent-encoding that matches the other SDKs exactly on every runtime, independent of how a
/// given framework version implements <see cref="Uri.EscapeDataString(string)"/>.
/// </summary>
internal static class UrlEncoding
{
    private const string Hex = "0123456789ABCDEF";

    /// <summary>RFC 3986 component encoding, like JS <c>encodeURIComponent</c> plus <c>!'()*</c>.</summary>
    public static string EncodeComponent(string value) => Encode(value, IsUnreserved, spaceAsPlus: false);

    /// <summary><c>application/x-www-form-urlencoded</c>, like JS <c>URLSearchParams</c>.</summary>
    public static string EncodeForm(string value) => Encode(value, IsFormSafe, spaceAsPlus: true);

    private static string Encode(string value, Func<byte, bool> isSafe, bool spaceAsPlus)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var builder = new StringBuilder(bytes.Length * 3);
        foreach (var b in bytes)
        {
            if (isSafe(b))
            {
                builder.Append((char)b);
            }
            else if (spaceAsPlus && b == (byte)' ')
            {
                builder.Append('+');
            }
            else
            {
                builder.Append('%').Append(Hex[b >> 4]).Append(Hex[b & 0xF]);
            }
        }

        return builder.ToString();
    }

    private static bool IsAlphaNumeric(byte b)
        => (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'0' && b <= (byte)'9');

    private static bool IsUnreserved(byte b)
        => IsAlphaNumeric(b) || b == (byte)'-' || b == (byte)'.' || b == (byte)'_' || b == (byte)'~';

    private static bool IsFormSafe(byte b)
        => IsAlphaNumeric(b) || b == (byte)'-' || b == (byte)'.' || b == (byte)'_' || b == (byte)'*';
}
