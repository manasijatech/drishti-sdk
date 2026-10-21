using System.Text;
using System.Text.Json;
using Manasija.Drishti.Exceptions;
using Manasija.Drishti.Internal;
using Manasija.Drishti.Models;

namespace Manasija.Drishti.Batch;

/// <summary>Builds batch input files and parses batch result files (JSON Lines).</summary>
public static class BatchJsonl
{
    /// <summary>
    /// Serializes each line as one JSON object followed by <c>\n</c>. Lines are sent as given:
    /// no defaults are added.
    /// </summary>
    /// <param name="lines">Input lines, such as <see cref="DailySummaryLine"/> values, dictionaries or <see cref="JsonElement"/>s.</param>
    /// <returns>The JSONL text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lines"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="lines"/> is empty.</exception>
    public static string BuildInput(IEnumerable<object> lines)
    {
        Guard.NotNull(lines, nameof(lines));
        var builder = new StringBuilder();
        var count = 0;
        foreach (var line in lines)
        {
            builder.Append(Encoding.UTF8.GetString(DrishtiJson.SerializeToUtf8(line))).Append('\n');
            count++;
        }

        if (count == 0)
        {
            throw new ArgumentException(Messages.Get("BatchInputRequired"), nameof(lines));
        }

        return builder.ToString();
    }

    /// <summary>Parses a results file into one model per line. Blank lines are skipped.</summary>
    /// <param name="content">The JSONL text.</param>
    /// <returns>The parsed lines, in order.</returns>
    /// <exception cref="DrishtiException">A line is not valid JSON.</exception>
    public static IReadOnlyList<BatchResultLine> ParseResults(string content)
    {
        Guard.NotNull(content, nameof(content));
        var results = new List<BatchResultLine>();
        var lineNumber = 0;
        foreach (var rawLine in content.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                results.Add(new BatchResultLine(DrishtiJson.Parse(line)));
            }
            catch (JsonException ex)
            {
                throw new DrishtiException(Messages.Format("InvalidBatchResultLine", lineNumber), ex);
            }
        }

        return results;
    }
}

/// <summary>One daily-summary input line for a batch job. Unset fields are not sent.</summary>
/// <param name="CustomId">Your id for the line; returned on the matching result line.</param>
public sealed record DailySummaryLine(string CustomId)
{
    /// <summary><c>exposure</c>, <c>intraday_movements</c> or <c>news_context</c>.</summary>
    public string? Mode { get; init; }

    /// <summary>Portfolio positions.</summary>
    public IReadOnlyList<Params.PortfolioPosition>? Portfolio { get; init; }

    /// <summary>A watchlist of NSE symbols.</summary>
    public IReadOnlyList<string>? Symbols { get; init; }

    /// <summary>How many days of news to consider.</summary>
    public int? ContextWindowDays { get; init; }
}
