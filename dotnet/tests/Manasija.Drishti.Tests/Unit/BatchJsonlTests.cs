using FluentAssertions;
using Manasija.Drishti.Batch;
using Manasija.Drishti.Exceptions;
using Manasija.Drishti.Params;

namespace Manasija.Drishti.Tests.Unit;

[TestClass]
public sealed class BatchJsonlTests
{
    [TestMethod]
    public void BuildInput_TypedLines_AreSnakeCaseWithoutNulls()
    {
        // Arrange
        var lines = new object[]
        {
            new DailySummaryLine("a") { Symbols = ["TCS"], Mode = "news_context" },
            new DailySummaryLine("b") { Portfolio = [new PortfolioPosition("INFY") { Exposure = 0.5 }] },
        };

        // Act
        var jsonl = BatchJsonl.BuildInput(lines);

        // Assert
        jsonl.Should().Be(
            "{\"custom_id\":\"a\",\"mode\":\"news_context\",\"symbols\":[\"TCS\"]}\n" +
            "{\"custom_id\":\"b\",\"portfolio\":[{\"symbol\":\"INFY\",\"exposure\":0.5}]}\n");
    }

    [TestMethod]
    public void BuildInput_Null_ThrowsArgumentNullException()
    {
        // Arrange
        IEnumerable<object> lines = null!;

        // Act
        var act = () => BatchJsonl.BuildInput(lines);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void ParseResults_InvalidLine_ThrowsWithLineNumber()
    {
        // Arrange
        const string content = "{\"custom_id\":\"a\"}\n{broken\n";

        // Act
        var act = () => BatchJsonl.ParseResults(content);

        // Assert
        act.Should().Throw<DrishtiException>().WithMessage("*line 2*");
    }

    [TestMethod]
    public void ParseResults_ExposesSummaryFromResponseBody()
    {
        // Arrange
        const string content = "{\"custom_id\":\"a\",\"status\":\"success\",\"response\":{\"status_code\":200,\"body\":{\"summary\":\"All good\"}}}\r\n";

        // Act
        var line = BatchJsonl.ParseResults(content).Should().ContainSingle().Subject;

        // Assert
        line.CustomId.Should().Be("a");
        line.Summary.Should().Be("All good");
        line.Error.Should().BeNull();
    }
}
