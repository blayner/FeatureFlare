using FeatureFlare.Application.Abstractions;
using FeatureFlare.Infrastructure.Persistence.InMemory;
using FeatureFlare.Infrastructure.Streaming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FeatureFlare.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IFlagRepository, InMemoryFlagRepository>();
        services.AddSingleton<ISegmentRepository, InMemorySegmentRepository>();
        services.AddSingleton<IExperimentRepository, InMemoryExperimentRepository>();
        services.AddSingleton<IExperimentEventRepository, InMemoryExperimentEventRepository>();
        services.AddSingleton<IUnitOfWork, InMemoryUnitOfWork>();

        services.AddSingleton<ISseBroadcaster, SseBroadcaster>();
        services.AddSingleton<IChangePublisher, SseChangePublisher>();
        services.AddHostedService<SseHeartbeatService>();

        return services;
    }
}
