using System.Net;
using System.Text;
using FluentAssertions;
using Manasija.Drishti.Http;
using Moq;
using Moq.Protected;

namespace Manasija.Drishti.Tests.Unit;

[TestClass]
public sealed class HttpClientTransportTests
{
    [TestMethod]
    public void Constructor_NullClient_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new HttpClientTransport(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [TestMethod]
    public async Task SendAsync_MapsHeadersBodyAndResponse()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (request, _) =>
            {
                captured = request;
                capturedBody = await request.Content!.ReadAsStringAsync();
                var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json") };
                response.Headers.Add("X-RateLimit-Remaining", "9");
                return response;
            });
        using var httpClient = new HttpClient(handler.Object);
        var transport = new HttpClientTransport(httpClient);
        var request = new TransportRequest(
            "POST",
            new Uri("https://api.test/v1/x"),
            new Dictionary<string, string> { ["X-API-Key"] = "k", ["Content-Type"] = "application/json", ["Accept"] = "application/json" },
            "{\"a\":1}"u8.ToArray(),
            TimeSpan.FromSeconds(5));

        // Act
        var response = await transport.SendAsync(request, CancellationToken.None);

        // Assert
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.Headers.GetValues("X-API-Key").Should().Equal("k");
        captured.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
        capturedBody.Should().Be("{\"a\":1}");
        response.StatusCode.Should().Be(201);
        response.ContentType.Should().StartWith("application/json");
        response.Header("x-ratelimit-remaining").Should().Be("9");
        Encoding.UTF8.GetString(response.Body).Should().Be("{\"ok\":true}");
    }

    [TestMethod]
    public async Task SendAsync_SlowResponse_ThrowsTimeoutException()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        using var httpClient = new HttpClient(handler.Object);
        var transport = new HttpClientTransport(httpClient);
        var request = new TransportRequest("GET", new Uri("https://api.test/v1/x"), new Dictionary<string, string>(), null, TimeSpan.FromMilliseconds(50));

        // Act
        var act = () => transport.SendAsync(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [TestMethod]
    public void CreateHandler_DoesNotFollowRedirects()
    {
        // Act
        using var handler = HttpClientTransport.CreateHandler();

        // Assert
        handler.Should().BeOfType<HttpClientHandler>().Which.AllowAutoRedirect.Should().BeFalse();
    }
}
