using System.Globalization;
using FluentAssertions;
using Manasija.Drishti.Http;
using Manasija.Drishti.Internal;
using Manasija.Drishti.Params;
using Manasija.Drishti.Tests.Fakes;

namespace Manasija.Drishti.Tests.Unit;

[TestClass]
[DoNotParallelize]
public sealed class QueryAndUrlTests
{
    [TestMethod]
    public void QueryBuilder_OmitsNullsAndEmptyLists_JoinsListsWithCommas()
    {
        // Arrange
        var builder = new QueryBuilder();

        // Act
        builder.Add("a", (string?)null).Add("b", Array.Empty<string>()).Add("c", ["X", "Y"]).Add("d", (long?)null);

        // Assert
        builder.Pairs.Select(p => $"{p.Key}={p.Value}").Should().Equal("c=X,Y");
    }

    [TestMethod]
    public void QueryBuilder_UsesLowerCaseBooleansAndInvariantNumbers()
    {
        // Arrange
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var builder = new QueryBuilder();

            // Act
            builder.Add("t", true).Add("f", false).AddValue("d", 1.5).AddValue("n", 1234567).AddValue("flags", new object[] { true, 2.5 });

            // Assert
            builder.Pairs.Select(p => $"{p.Key}={p.Value}").Should().Equal("t=true", "f=false", "d=1.5", "n=1234567", "flags=true,2.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    public void QueryParams_ToQuery_FollowsDeclaredOrder()
    {
        // Arrange
        var parameters = new NewsParams { Limit = 5, Symbols = ["TCS"], Sentiment = "positive" };

        // Act
        var query = parameters.ToQuery();

        // Assert
        query.Select(p => p.Key).Should().Equal("symbols", "sentiment", "limit");
    }

    [TestMethod]
    [DataRow("A/B C", "A%2FB%20C")]
    [DataRow("a+b&c=d", "a%2Bb%26c%3Dd")]
    [DataRow("safe-._~", "safe-._~")]
    [DataRow("!*'()", "%21%2A%27%28%29")]
    [DataRow("₹", "%E2%82%B9")]
    public void EncodeComponent_MatchesOtherSdks(string input, string expected)
        => UrlEncoding.EncodeComponent(input).Should().Be(expected);

    [TestMethod]
    [DataRow("k 1+2&x", "k+1%2B2%26x")]
    [DataRow("a*b-c.d_e", "a*b-c.d_e")]
    [DataRow("~", "%7E")]
    public void EncodeForm_MatchesUrlSearchParams(string input, string expected)
        => UrlEncoding.EncodeForm(input).Should().Be(expected);

    [TestMethod]
    public async Task RequestAsync_ReplacesColonAndBracePlaceholders()
    {
        // Arrange
        var transport = new FakeTransport();
        using var client = FakeTransport.CreateClient(transport);

        // Act
        await client.RequestAsync(
            "get",
            "v1/things/{id}/items/:item",
            query: [new("tags", new[] { "a", "b" }), new("on", true), new("skip", null)],
            pathParams: new Dictionary<string, object?> { ["id"] = "x y", ["item"] = 7 });

        // Assert
        var request = transport.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be("GET");
        request.Uri.OriginalString.Should().Be("https://api.test/v1/things/x%20y/items/7?tags=a%2Cb&on=true");
    }

    [TestMethod]
    public async Task RequestAsync_SerializesBodyAsSnakeCaseJsonWithoutNulls()
    {
        // Arrange
        var transport = new FakeTransport();
        using var client = FakeTransport.CreateClient(transport);

        // Act
        await client.PostDailySummaryAsync(new DailySummaryRequest
        {
            Portfolio = [new PortfolioPosition("TCS") { Exposure = 0.5 }],
            ContextWindowDays = 3,
        });

        // Assert
        var request = transport.Requests.Should().ContainSingle().Subject;
        request.Header("Content-Type").Should().Be("application/json");
        System.Text.Encoding.UTF8.GetString(request.Body!).Should().Be("{\"portfolio\":[{\"symbol\":\"TCS\",\"exposure\":0.5}],\"context_window_days\":3}");
    }
}
