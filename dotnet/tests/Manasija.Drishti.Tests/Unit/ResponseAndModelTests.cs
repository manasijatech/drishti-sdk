using System.Text.Json;
using FluentAssertions;
using Manasija.Drishti.Exceptions;
using Manasija.Drishti.Http;
using Manasija.Drishti.Models;
using Manasija.Drishti.Params;
using Manasija.Drishti.Tests.Fakes;

namespace Manasija.Drishti.Tests.Unit;

[TestClass]
public sealed class ResponseAndModelTests
{
    [TestMethod]
    public async Task ApiError_WithErrorEnvelope_ExposesMessageCodeAndDetail()
    {
        // Arrange
        var transport = new FakeTransport()
            .EnqueueJson(429, "{\"error\":{\"code\":\"rate_limit_exceeded\",\"message\":\"Slow down\",\"details\":null}}");
        using var client = FakeTransport.CreateClient(transport, o => o.MaxRetries = 0);

        // Act
        var act = () => client.GetNewsAsync();

        // Assert
        var error = (await act.Should().ThrowAsync<DrishtiApiException>()).Which;
        error.StatusCode.Should().Be(429);
        error.ErrorCode.Should().Be("rate_limit_exceeded");
        error.Detail.Should().Be("Slow down");
        error.Message.Should().Be("Drishti API error 429: Slow down");
    }

    [TestMethod]
    public async Task ApiError_ExposesHeadersAndRetryAfter()
    {
        // Arrange
        var transport = new FakeTransport().EnqueueJson(429, "{\"detail\":\"slow\"}", new Dictionary<string, string>
        {
            ["Retry-After"] = "60",
            ["X-RateLimit-Remaining"] = "0",
        });
        using var client = FakeTransport.CreateClient(transport, o => o.MaxRetries = 0);

        // Act
        var act = () => client.GetNewsAsync();

        // Assert
        var error = (await act.Should().ThrowAsync<DrishtiApiException>()).Which;
        error.RetryAfter.Should().Be(TimeSpan.FromSeconds(60));
        error.Headers["x-ratelimit-remaining"].Should().Be("0");
    }

    [TestMethod]
    public void ApiError_WithoutRetryAfter_ReturnsNull()
        => new DrishtiApiException(500, null, "boom").RetryAfter.Should().BeNull();

    [TestMethod]
    public async Task ApiError_WithLegacyDetail_UsesDetail()
    {
        // Arrange
        var transport = new FakeTransport().EnqueueJson(404, "{\"detail\":\"Not found\"}");
        using var client = FakeTransport.CreateClient(transport);

        // Act
        var act = () => client.GetNewsAsync();

        // Assert
        var error = (await act.Should().ThrowAsync<DrishtiApiException>()).Which;
        error.Detail.Should().Be("Not found");
        error.ErrorCode.Should().BeNull();
        error.Message.Should().Be("Drishti API error 404: {\"detail\":\"Not found\"}");
    }

    [TestMethod]
    public async Task ApiError_WithMalformedJsonBody_FallsBackToText()
    {
        // Arrange
        var transport = new FakeTransport().EnqueueJson(500, "<html>oops", null);
        using var client = FakeTransport.CreateClient(transport, o => o.MaxRetries = 0);

        // Act
        var act = () => client.GetNewsAsync();

        // Assert
        var error = (await act.Should().ThrowAsync<DrishtiApiException>()).Which;
        error.BodyJson.Should().BeNull();
        error.BodyText.Should().Be("<html>oops");
        error.Detail.Should().Be("<html>oops");
    }

    [TestMethod]
    public async Task SuccessWithInvalidJson_ThrowsDrishtiException()
    {
        // Arrange
        var transport = new FakeTransport().EnqueueJson(200, "{not json");
        using var client = FakeTransport.CreateClient(transport);

        // Act
        var act = () => client.GetNewsAsync();

        // Assert
        (await act.Should().ThrowAsync<DrishtiException>()).Which.Should().NotBeOfType<DrishtiApiException>();
    }

    [TestMethod]
    public async Task TypedModels_ReadFieldsAndTolerateMissingOrMistypedValues()
    {
        // Arrange
        var transport = new FakeTransport().EnqueueJson(200, """
            {"data":[{"id":"n1","title":"TCS wins deal","sentiment":"positive","scrip_code":532540,"date":null,"extra":"kept"}],"has_next":true}
            """);
        using var client = FakeTransport.CreateClient(transport);

        // Act
        var page = await client.GetNewsAsync(new NewsParams { Symbols = ["TCS"] });

        // Assert
        page.Should().NotBeNull();
        page!.HasNext.Should().BeTrue();
        var item = page.Data.Should().ContainSingle().Subject;
        item.Id.Should().Be("n1");
        item.Title.Should().Be("TCS wins deal");
        item.ScripCode.Should().Be("532540");
        item.Date.Should().BeNull();
        item.Link.Should().BeNull();
        item.Json.GetProperty("extra").GetString().Should().Be("kept");
    }

    [TestMethod]
    public void Models_SerializeBackToTheOriginalJson()
    {
        // Arrange
        var json = JsonAssert.Parse("{\"id\":\"a1\",\"symbol\":\"TCS\",\"meta\":{\"primary_drivers\":[\"volume\"]},\"price\":{\"value\":3500.5}}");
        var alert = new Alert(json);

        // Act
        var serialized = JsonSerializer.Serialize(alert);
        var roundTripped = JsonSerializer.Deserialize<Alert>(serialized);

        // Assert
        JsonAssert.Equal(json, JsonAssert.Parse(serialized), "serialized");
        roundTripped!.PrimaryDrivers.Should().Equal("volume");
        roundTripped.Price!.Value.Should().Be(3500.5);
    }

    [TestMethod]
    public void Deserialize_MapsSnakeCaseToYourOwnType()
    {
        // Arrange
        var item = new BlockDealItem(JsonAssert.Parse("{\"trade_value_cr\":12.5,\"symbol\":\"TCS\"}"));

        // Act
        var mine = item.Deserialize<MyDeal>();

        // Assert
        mine!.TradeValueCr.Should().Be(12.5);
        mine.Symbol.Should().Be("TCS");
    }

    [TestMethod]
    public void BatchJob_IsTerminal_IgnoresCase()
    {
        new BatchJob(JsonAssert.Parse("{\"status\":\"SUCCEEDED\"}")).IsTerminal.Should().BeTrue();
        new BatchJob(JsonAssert.Parse("{\"status\":\"running\"}")).IsTerminal.Should().BeFalse();
        new BatchJob(JsonAssert.Parse("{}")).IsTerminal.Should().BeFalse();
    }

    [TestMethod]
    public async Task Earnings_ExistingTableIsNotOverwritten()
    {
        // Arrange
        var transport = new FakeTransport().EnqueueJson(200, "{\"id\":\"e1\",\"earnings_table\":null,\"earnings_table_extraction\":{\"eps\":5}}");
        using var client = FakeTransport.CreateClient(transport);

        // Act
        var earnings = await client.GetEarningsDetailAsync(new DetailParams("q4_26") { Symbol = "TCS" });

        // Assert
        earnings!.EarningsTable.Should().BeNull();
    }

    [TestMethod]
    public async Task DetailEndpoints_RequireSymbolOrScripCode()
    {
        // Arrange
        using var client = FakeTransport.CreateClient(new FakeTransport());

        // Act
        var act = () => client.GetConcallsDetailAsync(new DetailParams("q4_26"));

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task AttachmentLookups_RequireAtLeastOneId()
    {
        // Arrange
        using var client = FakeTransport.CreateClient(new FakeTransport());

        // Act
        var empty = () => client.GetEarningsAttachmentsAsync([]);
        var nullIds = () => client.GetEarningsAttachmentsAsync(null!);

        // Assert
        await empty.Should().ThrowAsync<ArgumentException>();
        await nullIds.Should().ThrowAsync<ArgumentNullException>();
    }

    [TestMethod]
    public async Task WaitForBatchJob_TimesOutWithDrishtiException()
    {
        // Arrange
        var transport = new FakeTransport();
        for (var i = 0; i < 5; i++)
        {
            transport.EnqueueJson(200, "{\"id\":\"job_1\",\"status\":\"running\"}");
        }

        using var client = FakeTransport.CreateClient(transport);

        // Act
        var act = () => client.WaitForBatchJobCompletionAsync("job_1", TimeSpan.Zero, TimeSpan.Zero);

        // Assert
        await act.Should().ThrowAsync<DrishtiException>().WithMessage("*job_1*");
        transport.Requests.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task SubmitAndWait_WithoutJobId_Throws()
    {
        // Arrange
        var transport = new FakeTransport().EnqueueJson(200, "{\"status\":\"pending\"}");
        using var client = FakeTransport.CreateClient(transport);

        // Act
        var act = () => client.SubmitBatchJobAndWaitAsync(new BatchJobRequest("{}\n"), TimeSpan.Zero);

        // Assert
        await act.Should().ThrowAsync<DrishtiException>().WithMessage("*id*");
    }

    [TestMethod]
    public async Task SubmitBatchJob_UsesDefaultFileNameAndJsonlContentType()
    {
        // Arrange
        var transport = new FakeTransport();
        using var client = FakeTransport.CreateClient(transport);

        // Act
        await client.PostBatchJobsAsync(new BatchJobRequest("{}\n"));

        // Assert
        var body = System.Text.Encoding.UTF8.GetString(transport.Requests.Single().Body!);
        body.Should().Contain("filename=\"batch.jsonl\"").And.Contain("Content-Type: application/jsonl");
    }

    private sealed class MyDeal
    {
        public string? Symbol { get; set; }

        public double TradeValueCr { get; set; }
    }
}
