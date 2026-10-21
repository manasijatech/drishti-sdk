using FluentAssertions;
using Manasija.Drishti.Exceptions;
using Manasija.Drishti.Http;
using Manasija.Drishti.Tests.Fakes;
using Moq;

namespace Manasija.Drishti.Tests.Unit;

[TestClass]
public sealed class RetryAndRedirectTests
{
    private static readonly IReadOnlyDictionary<string, string> Json = new Dictionary<string, string> { ["Content-Type"] = "application/json" };

    [TestMethod]
    public async Task NetworkErrors_ExhaustRetries_ThrowDrishtiNetworkException()
    {
        // Arrange
        var transport = new Mock<IDrishtiTransport>();
        transport.Setup(t => t.SendAsync(It.IsAny<TransportRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("refused"));
        using var client = new DrishtiClient(Options(maxRetries: 2), transport.Object);

        // Act
        var act = () => client.GetAccountAsync();

        // Assert
        var error = await act.Should().ThrowAsync<DrishtiNetworkException>();
        error.Which.InnerException.Should().BeOfType<HttpRequestException>();
        error.Which.Message.Should().Contain("GET /v1/account");
        transport.Verify(t => t.SendAsync(It.IsAny<TransportRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [TestMethod]
    public async Task Timeouts_AreRetriedLikeNetworkErrors()
    {
        // Arrange
        var transport = new Mock<IDrishtiTransport>();
        transport.SetupSequence(t => t.SendAsync(It.IsAny<TransportRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("slow"))
            .ReturnsAsync(new TransportResponse(200, Json, "{\"data\":{}}"u8.ToArray()));
        using var client = new DrishtiClient(Options(maxRetries: 1), transport.Object);

        // Act
        var account = await client.GetAccountAsync();

        // Assert
        account.Should().NotBeNull();
        transport.Verify(t => t.SendAsync(It.IsAny<TransportRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task CallerCancellation_IsNotRetried()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var transport = new Mock<IDrishtiTransport>();
        transport.Setup(t => t.SendAsync(It.IsAny<TransportRequest>(), It.IsAny<CancellationToken>()))
            .Returns<TransportRequest, CancellationToken>((_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new TransportResponse(200, Json, null));
            });
        using var client = new DrishtiClient(Options(maxRetries: 5), transport.Object);

        // Act
        var act = () => client.GetAccountAsync(cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        transport.Verify(t => t.SendAsync(It.IsAny<TransportRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CustomRetryStatuses_ReplaceDefaults()
    {
        // Arrange
        var transport = new FakeTransport().EnqueueJson(503, "{}");
        using var client = FakeTransport.CreateClient(transport, o => o.RetryOnStatuses = [418]);

        // Act
        var act = () => client.GetAccountAsync();

        // Assert
        (await act.Should().ThrowAsync<DrishtiApiException>()).Which.StatusCode.Should().Be(503);
        transport.Requests.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task CrossOriginRedirect_DropsApiKeyAndCustomHeaders()
    {
        // Arrange
        var transport = new FakeTransport()
            .EnqueueRedirect(302, "https://files.test/doc.pdf?sig=abc")
            .Enqueue(new TransportResponse(200, new Dictionary<string, string> { ["Content-Type"] = "application/pdf" }, "%PDF"u8.ToArray()));
        using var client = FakeTransport.CreateClient(transport, o => o.Headers["X-Team"] = "ops");

        // Act
        var pdf = await client.GetAnnouncementCitationPdfAsync("A1");

        // Assert
        pdf.Should().Equal("%PDF"u8.ToArray());
        transport.Requests.Should().HaveCount(2);
        var followUp = transport.Requests[1];
        followUp.Uri.Should().Be(new Uri("https://files.test/doc.pdf?sig=abc"));
        followUp.Header("X-API-Key").Should().BeNull();
        followUp.Header("X-Team").Should().BeNull();
        followUp.Header("Accept").Should().Be("*/*");
    }

    [TestMethod]
    public async Task SameOriginRedirect_KeepsHeaders()
    {
        // Arrange
        var transport = new FakeTransport().EnqueueRedirect(307, "/v1/account/moved").EnqueueJson(200, "{\"data\":{}}");
        using var client = FakeTransport.CreateClient(transport, o => o.Headers["X-Team"] = "ops");

        // Act
        await client.GetAccountAsync();

        // Assert
        var followUp = transport.Requests[1];
        followUp.Uri.Should().Be(new Uri("https://api.test/v1/account/moved"));
        followUp.Header("X-API-Key").Should().Be("test-key");
        followUp.Header("X-Team").Should().Be("ops");
    }

    [TestMethod]
    public async Task HttpsToHttpRedirect_IsRefused()
    {
        // Arrange
        var transport = new FakeTransport().EnqueueRedirect(302, "http://files.test/doc.pdf");
        using var client = FakeTransport.CreateClient(transport);

        // Act
        var act = () => client.GetAnnouncementCitationPdfAsync("A1");

        // Assert
        await act.Should().ThrowAsync<DrishtiException>().WithMessage("*HTTPS*");
        transport.Requests.Should().HaveCount(1);
    }

    [TestMethod]
    [DataRow(303, "POST", "GET", false)]
    [DataRow(302, "POST", "GET", false)]
    [DataRow(307, "POST", "POST", true)]
    public async Task Redirects_SwitchToGetLikeBrowsers(int status, string method, string expectedMethod, bool keepsBody)
    {
        // Arrange
        var transport = new FakeTransport().EnqueueRedirect(status, "/v1/next").EnqueueJson(200, "{}");
        using var client = FakeTransport.CreateClient(transport);

        // Act
        await client.RequestAsync(method, "/v1/start", body: new { a = 1 });

        // Assert
        var followUp = transport.Requests[1];
        followUp.Method.Should().Be(expectedMethod);
        (followUp.Body is not null).Should().Be(keepsBody);
        (followUp.Header("Content-Type") is not null).Should().Be(keepsBody);
    }

    [TestMethod]
    public async Task Redirects_StopAfterFiveHops()
    {
        // Arrange
        var transport = new FakeTransport();
        for (var i = 0; i < 10; i++)
        {
            transport.EnqueueRedirect(302, $"/v1/hop{i}");
        }

        using var client = FakeTransport.CreateClient(transport);

        // Act
        var response = await client.RequestAsync("GET", "/v1/start");

        // Assert
        response.StatusCode.Should().Be(302);
        transport.Requests.Should().HaveCount(6);
    }

    private static DrishtiClientOptions Options(int maxRetries) => new()
    {
        ApiKey = "test-key",
        BaseUrl = "https://api.test",
        MaxRetries = maxRetries,
        InitialRetryDelay = TimeSpan.Zero,
        MaxRetryDelay = TimeSpan.Zero,
    };
}
