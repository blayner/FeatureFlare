using FeatureFlare.Sdk;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Initializes the client during host start-up (bounded by <c>InitTimeout</c>, so a FeatureFlare outage never
/// blocks the application from starting) and flushes queued events on graceful shutdown.
/// </summary>
internal sealed class FeatureFlareHostedService(FeatureFlareClient client, ILogger<FeatureFlareHostedService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (!client.IsReady)
        {
            logger.LogWarning("FeatureFlare started without configuration; flags will return defaults until it loads");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => client.DisposeAsync().AsTask();
}
