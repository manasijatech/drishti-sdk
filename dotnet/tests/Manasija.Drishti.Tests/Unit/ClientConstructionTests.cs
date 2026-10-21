using FluentAssertions;
using Manasija.Drishti.Http;
using Manasija.Drishti.Tests.Fakes;
using Moq;

namespace Manasija.Drishti.Tests.Unit;

[TestClass]
[DoNotParallelize]
public sealed class ClientConstructionTests
{
    [TestMethod]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        DrishtiClientOptions options = null!;

        // Act
        var act = () => new DrishtiClient(options);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void Constructor_BlankApiKey_ThrowsArgumentException(string? apiKey)
    {
        // Arrange
        var options = new DrishtiClientOptions { ApiKey = apiKey! };

        // Act
        var act = () => new DrishtiClient(options, Mock.Of<IDrishtiTransport>());

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("ApiKey");
    }

    [TestMethod]
    [DataRow("not a url")]
    [DataRow("ftp://api.test")]
    [DataRow("/relative")]
    public void Constructor_InvalidBaseUrl_ThrowsArgumentException(string baseUrl)
    {
        // Arrange
        var options = new DrishtiClientOptions { ApiKey = "k", BaseUrl = baseUrl };

        // Act
        var act = () => new DrishtiClient(options, Mock.Of<IDrishtiTransport>());

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("BaseUrl");
    }

    [TestMethod]
    public void Constructor_NonPositiveTimeout_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new DrishtiClientOptions { ApiKey = "k", Timeout = TimeSpan.Zero };

        // Act
        var act = () => new DrishtiClient(options, Mock.Of<IDrishtiTransport>());

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestMethod]
    public void Constructor_NegativeMaxRetries_FailsValidation()
    {
        // Arrange
        var options = new DrishtiClientOptions { ApiKey = "k", MaxRetries = -1 };

        // Act
        var act = () => new DrishtiClient(options, Mock.Of<IDrishtiTransport>());

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*MaxRetries*");
    }

    [TestMethod]
    public void BaseUrl_TrailingSlashesAndWhitespace_AreTrimmed()
    {
        // Arrange
        var options = new DrishtiClientOptions { ApiKey = "k", BaseUrl = " https://api.test/proxy/// " };

        // Act
        using var client = new DrishtiClient(options, Mock.Of<IDrishtiTransport>());

        // Assert
        client.BaseUrl.Should().Be("https://api.test/proxy");
    }

    [TestMethod]
    public void Defaults_MatchSharedSdkDefaults()
    {
        // Arrange and act
        var options = new DrishtiClientOptions();

        // Assert
        options.BaseUrl.Should().Be("https://developers.manasija.in");
        options.MaxRetries.Should().Be(2);
        options.InitialRetryDelay.Should().Be(TimeSpan.FromMilliseconds(300));
        options.MaxRetryDelay.Should().Be(TimeSpan.FromSeconds(5));
        options.RetryMultiplier.Should().Be(2.0);
        options.Timeout.Should().Be(TimeSpan.FromSeconds(60));
        options.RetryOnStatuses.Should().Equal(408, 409, 425, 429, 500, 502, 503, 504);
    }

    [TestMethod]
    public void FromEnvironment_MissingApiKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var previous = Environment.GetEnvironmentVariable(DrishtiConstants.ApiKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(DrishtiConstants.ApiKeyEnvironmentVariable, null);
        try
        {
            // Act
            var act = DrishtiClient.FromEnvironment;

            // Assert
            act.Should().Throw<InvalidOperationException>().WithMessage("*DRISHTI_API_KEY*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(DrishtiConstants.ApiKeyEnvironmentVariable, previous);
        }
    }

    [TestMethod]
    public void FromEnvironment_ReadsKeyAndBaseUrl()
    {
        // Arrange
        var previousKey = Environment.GetEnvironmentVariable(DrishtiConstants.ApiKeyEnvironmentVariable);
        var previousUrl = Environment.GetEnvironmentVariable(DrishtiConstants.BaseUrlEnvironmentVariable);
        Environment.SetEnvironmentVariable(DrishtiConstants.ApiKeyEnvironmentVariable, "env-key");
        Environment.SetEnvironmentVariable(DrishtiConstants.BaseUrlEnvironmentVariable, "https://staging.test/");
        try
        {
            // Act
            using var client = DrishtiClient.FromEnvironment();

            // Assert
            client.BaseUrl.Should().Be("https://staging.test");
        }
        finally
        {
            Environment.SetEnvironmentVariable(DrishtiConstants.ApiKeyEnvironmentVariable, previousKey);
            Environment.SetEnvironmentVariable(DrishtiConstants.BaseUrlEnvironmentVariable, previousUrl);
        }
    }

    [TestMethod]
    public async Task Requests_SendUserAgentAndOmitContentTypeWithoutBody()
    {
        // Arrange
        var transport = new FakeTransport();
        using var client = FakeTransport.CreateClient(transport);

        // Act
        await client.GetAccountAsync();

        // Assert
        var request = transport.Requests.Should().ContainSingle().Subject;
        request.Header("User-Agent").Should().Be(DrishtiClientOptions.DefaultUserAgent);
        request.Header("Content-Type").Should().BeNull();
        request.Body.Should().BeNull();
        request.Timeout.Should().Be(TimeSpan.FromSeconds(60));
    }

    [TestMethod]
    public async Task PerRequestHeaders_CannotOverrideApiKey()
    {
        // Arrange
        var transport = new FakeTransport();
        using var client = FakeTransport.CreateClient(transport);

        // Act
        await client.RequestAsync("GET", "/v1/account", headers: new Dictionary<string, string> { ["x-api-key"] = "spoofed", ["X-Trace"] = "t1" });

        // Assert
        var request = transport.Requests.Should().ContainSingle().Subject;
        request.Header("X-API-Key").Should().Be("test-key");
        request.Header("X-Trace").Should().Be("t1");
        request.Headers.Keys.Count(k => k.Equals("X-API-Key", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }
}
