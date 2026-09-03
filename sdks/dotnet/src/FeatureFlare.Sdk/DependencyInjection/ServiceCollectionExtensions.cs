using FeatureFlare.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class FeatureFlareServiceCollectionExtensions
{
    internal const string HttpClientName = "FeatureFlare";
    internal const string StreamHttpClientName = "FeatureFlare.Stream";

    /// <summary>Registers <see cref="IFeatureFlareClient"/> bound to the "FeatureFlare" configuration section.</summary>
    public static IServiceCollection AddFeatureFlare(this IServiceCollection services, IConfiguration configuration) =>
        services.AddFeatureFlare(options => configuration.GetSection(FeatureFlareOptions.SectionName).Bind(options));

    /// <summary>
    /// Registers a singleton <see cref="IFeatureFlareClient"/> plus a hosted service that initializes it
    /// on start-up and flushes pending events on shutdown.
    /// </summary>
    public static IServiceCollection AddFeatureFlare(this IServiceCollection services, Action<FeatureFlareOptions> configure)
    {
        services.AddOptions<FeatureFlareOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "FeatureFlare:ApiKey is required.")
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _), "FeatureFlare:BaseUrl must be an absolute URI.")
            .ValidateOnStart();

        // Regular API calls: standard resilience pipeline (retry with jitter, circuit breaker, timeouts).
        services.AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler();

        // The SSE stream is a single long-lived request: no request timeout and no retry handler — the
        // listener owns reconnection and backoff.
        services.AddHttpClient(StreamHttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        services.TryAddSingleton(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new FeatureFlareClient(
                factory.CreateClient(HttpClientName),
                factory.CreateClient(StreamHttpClientName),
                sp.GetRequiredService<IOptions<FeatureFlareOptions>>(),
                sp.GetService<ILogger<FeatureFlareClient>>());
        });
        services.TryAddSingleton<IFeatureFlareClient>(sp => sp.GetRequiredService<FeatureFlareClient>());
        services.AddHostedService<FeatureFlareHostedService>();

        return services;
    }
}
