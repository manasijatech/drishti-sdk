using System.Globalization;
using System.Text.Json;

namespace Manasija.Drishti.Tests.Fakes;

/// <summary>Structural JSON equality: object key order is ignored and numbers compare by value.</summary>
internal static class JsonAssert
{
    public static void Equal(JsonElement expected, JsonElement actual, string context)
    {
        var difference = FindDifference(expected, actual, "$");
        if (difference is not null)
        {
            throw new AssertFailedException($"{context}: {difference}\nexpected: {expected.GetRawText()}\nactual:   {actual.GetRawText()}");
        }
    }

    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string? FindDifference(JsonElement expected, JsonElement actual, string path)
    {
        if (expected.ValueKind is JsonValueKind.True or JsonValueKind.False && actual.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return expected.GetBoolean() == actual.GetBoolean() ? null : $"{path} differs";
        }

        if (expected.ValueKind != actual.ValueKind)
        {
            return $"{path} is {actual.ValueKind}, expected {expected.ValueKind}";
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedNames = expected.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                var actualNames = actual.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                if (!expectedNames.SequenceEqual(actualNames))
                {
                    return $"{path} has keys [{string.Join(", ", actualNames)}], expected [{string.Join(", ", expectedNames)}]";
                }

                foreach (var name in expectedNames)
                {
                    var difference = FindDifference(expected.GetProperty(name), actual.GetProperty(name), $"{path}.{name}");
                    if (difference is not null)
                    {
                        return difference;
                    }
                }

                return null;
            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToList();
                var actualItems = actual.EnumerateArray().ToList();
                if (expectedItems.Count != actualItems.Count)
                {
                    return $"{path} has {actualItems.Count} items, expected {expectedItems.Count}";
                }

                for (var i = 0; i < expectedItems.Count; i++)
                {
                    var difference = FindDifference(expectedItems[i], actualItems[i], $"{path}[{i}]");
                    if (difference is not null)
                    {
                        return difference;
                    }
                }

                return null;
            case JsonValueKind.Number:
                return decimal.Parse(expected.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture)
                    == decimal.Parse(actual.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture)
                    ? null
                    : $"{path} is {actual.GetRawText()}, expected {expected.GetRawText()}";
            case JsonValueKind.String:
                return expected.GetString() == actual.GetString() ? null : $"{path} is \"{actual.GetString()}\", expected \"{expected.GetString()}\"";
            default:
                return null;
        }
    }
}
