using FeatureFlare.Application.Evaluation;
using FeatureFlare.Application.Flags;
using FeatureFlare.Application.Sdk;
using FeatureFlare.Application.Segments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FeatureFlare.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<FlagEvaluationEngine>();
        services.AddScoped<FlagService>();
        services.AddScoped<SegmentService>();
        services.AddScoped<EvaluationService>();
        services.AddScoped<SdkConfigService>();
        return services;
    }
}
