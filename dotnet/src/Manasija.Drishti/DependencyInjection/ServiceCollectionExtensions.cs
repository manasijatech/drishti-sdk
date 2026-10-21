using Manasija.Drishti.Http;
using Manasija.Drishti.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Manasija.Drishti.DependencyInjection;

/// <summary>Registers <see cref="IDrishtiClient"/> with <c>Microsoft.Extensions.DependencyInjection</c>.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>The name of the <see cref="HttpClient"/> the client uses, for further configuration.</summary>
    public const string HttpClientName = "Manasija.Drishti";

    /// <summary>
    /// Registers a singleton <see cref="IDrishtiClient"/> whose options are bound from
    /// <paramref name="configuration"/> and validated at startup.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddDrishtiClient(builder.Configuration);
    /// // appsettings.json: { "Drishti": { "ApiKey": "..." } }
    /// </code>
    /// </example>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root or section parent.</param>
    /// <param name="sectionName">The section holding <see cref="DrishtiClientOptions"/>; default <c>Drishti</c>.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> for the underlying HTTP client, to add handlers.</returns>
    public static IHttpClientBuilder AddDrishtiClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = DrishtiClientOptions.SectionName)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotBlank(sectionName, nameof(sectionName));
        services.AddOptions<DrishtiClientOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return AddCore(services);
    }

    /// <summary>Registers a singleton <see cref="IDrishtiClient"/> configured in code.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the options.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> for the underlying HTTP client.</returns>
    public static IHttpClientBuilder AddDrishtiClient(this IServiceCollection services, Action<DrishtiClientOptions> configure)
    {
        Guard.NotNull(services, nameof(services));
        Guard.NotNull(configure, nameof(configure));
        services.AddOptions<DrishtiClientOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        return AddCore(services);
    }

    private static IHttpClientBuilder AddCore(IServiceCollection services)
    {
        var builder = services
            .AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(HttpClientTransport.CreateHandler)
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);

        services.TryAddSingleton<IDrishtiClient>(provider => new DrishtiClient(
            provider.GetRequiredService<IOptions<DrishtiClientOptions>>().Value,
            new HttpClientFactoryTransport(provider.GetRequiredService<IHttpClientFactory>(), HttpClientName),
            provider.GetService<ILoggerFactory>()));
        return builder;
    }

    /// <summary>
    /// Asks the factory for a client per request, so pooled handlers are rotated and DNS changes
    /// are picked up while the <see cref="DrishtiClient"/> itself stays a singleton.
    /// </summary>
    private sealed class HttpClientFactoryTransport(IHttpClientFactory factory, string name) : IDrishtiTransport
    {
        public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
            => new HttpClientTransport(factory.CreateClient(name)).SendAsync(request, cancellationToken);
    }
}
