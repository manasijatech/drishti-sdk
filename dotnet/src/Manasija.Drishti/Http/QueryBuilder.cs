using System.Globalization;

namespace Manasija.Drishti.Http;

/// <summary>
/// Builds query parameters with the serialization rules every Drishti SDK shares: null values and
/// empty lists are omitted, lists are comma-joined, booleans are lower-case and numbers use the
/// invariant culture.
/// </summary>
public sealed class QueryBuilder
{
    private readonly List<KeyValuePair<string, string>> _pairs = [];

    /// <summary>The parameters added so far, in order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Pairs => _pairs;

    /// <summary>Adds a string value unless it is null.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public QueryBuilder Add(string name, string? value)
    {
        if (value is not null)
        {
            _pairs.Add(new KeyValuePair<string, string>(name, value));
        }

        return this;
    }

    /// <summary>Adds an integer unless it is null.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public QueryBuilder Add(string name, long? value)
        => Add(name, value?.ToString(CultureInfo.InvariantCulture));

    /// <summary>Adds a boolean as <c>true</c> or <c>false</c> unless it is null.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public QueryBuilder Add(string name, bool? value)
        => Add(name, value is null ? null : value.Value ? "true" : "false");

    /// <summary>Adds a list as one comma-joined value, unless it is null or empty.</summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public QueryBuilder Add(string name, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return this;
        }

        var list = values.Where(v => v is not null).ToList();
        return list.Count == 0 ? this : Add(name, string.Join(",", list));
    }

    /// <summary>
    /// Adds an arbitrary value: strings, numbers, booleans and lists follow the rules above;
    /// anything else uses its invariant-culture string form.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public QueryBuilder AddValue(string name, object? value) => value switch
    {
        null => this,
        string s => Add(name, s),
        bool b => Add(name, b),
        IEnumerable<string> list => Add(name, list),
        System.Collections.IEnumerable items => Add(name, items.Cast<object?>().Where(i => i is not null).Select(i => FormatScalar(i!))),
        _ => Add(name, FormatScalar(value)),
    };

    private static string FormatScalar(object value) => value switch
    {
        bool b => b ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
