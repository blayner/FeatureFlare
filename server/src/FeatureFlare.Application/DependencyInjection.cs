using FeatureFlare.Application.Evaluation;
using FeatureFlare.Application.Flags;
using FeatureFlare.Application.Segments;
using Microsoft.Extensions.DependencyInjection;

namespace FeatureFlare.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<FlagEvaluationEngine>();
        services.AddScoped<FlagService>();
        services.AddScoped<SegmentService>();
        services.AddScoped<EvaluationService>();
        return services;
    }
}
