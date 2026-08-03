using FeatureFlare.Application.Abstractions;
using FeatureFlare.Infrastructure.Persistence.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FeatureFlare.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IFlagRepository, InMemoryFlagRepository>();
        services.AddSingleton<ISegmentRepository, InMemorySegmentRepository>();
        services.AddSingleton<IUnitOfWork, InMemoryUnitOfWork>();
        return services;
    }
}
