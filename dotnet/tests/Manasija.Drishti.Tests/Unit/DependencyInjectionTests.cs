using FluentAssertions;
using Manasija.Drishti.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Manasija.Drishti.Tests.Unit;

[TestClass]
public sealed class DependencyInjectionTests
{
    [TestMethod]
    public void AddDrishtiClient_BindsConfigurationSection()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Drishti:ApiKey"] = "config-key",
                ["Drishti:BaseUrl"] = "https://proxy.test/drishti/",
                ["Drishti:Timeout"] = "00:00:15",
                ["Drishti:MaxRetries"] = "4",
            })
            .Build();
        var services = new ServiceCollection();

        // Act
        services.AddDrishtiClient(configuration);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDrishtiClient>();
        var options = provider.GetRequiredService<IOptions<DrishtiClientOptions>>().Value;

        // Assert
        client.BaseUrl.Should().Be("https://proxy.test/drishti");
        options.Timeout.Should().Be(TimeSpan.FromSeconds(15));
        options.MaxRetries.Should().Be(4);
        provider.GetRequiredService<IDrishtiClient>().Should().BeSameAs(client);
    }

    [TestMethod]
    public void AddDrishtiClient_MissingApiKey_FailsValidation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDrishtiClient(options => options.ApiKey = "  ");
        using var provider = services.BuildServiceProvider();

        // Act
        var act = () => provider.GetRequiredService<IDrishtiClient>();

        // Assert
        act.Should().Throw<OptionsValidationException>().WithMessage("*ApiKey*");
    }

    [TestMethod]
    public void AddDrishtiClient_NullArguments_Throw()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var nullServices = () => ((IServiceCollection)null!).AddDrishtiClient(_ => { });
        var nullConfigure = () => services.AddDrishtiClient((Action<DrishtiClientOptions>)null!);
        var nullConfiguration = () => services.AddDrishtiClient((IConfiguration)null!);

        // Assert
        nullServices.Should().Throw<ArgumentNullException>().WithParameterName("services");
        nullConfigure.Should().Throw<ArgumentNullException>().WithParameterName("configure");
        nullConfiguration.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [TestMethod]
    public void AddDrishtiClient_ReturnsBuilderForTheNamedHttpClient()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder = services.AddDrishtiClient(options => options.ApiKey = "k");

        // Assert
        builder.Name.Should().Be(ServiceCollectionExtensions.HttpClientName);
    }
}
