using FeatureFlare.Application.Abstractions;
using FeatureFlare.Infrastructure.Persistence.Ef;
using FeatureFlare.Infrastructure.Persistence.InMemory;
using FeatureFlare.Infrastructure.Streaming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FeatureFlare.Infrastructure;

public static class DependencyInjection
{
    public const string PostgresProvider = "Postgres";
    public const string InMemoryProvider = "InMemory";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? InMemoryProvider;

        if (string.Equals(provider, PostgresProvider, StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration.GetConnectionString("FeatureFlare")
                ?? throw new InvalidOperationException("ConnectionStrings:FeatureFlare is required for the Postgres provider.");

            services.AddDbContext<FeatureFlareDbContext>(options => options
                .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3)));

            services.AddScoped<IFlagRepository, EfFlagRepository>();
            services.AddScoped<ISegmentRepository, EfSegmentRepository>();
            services.AddScoped<IExperimentRepository, EfExperimentRepository>();
            services.AddScoped<IExperimentEventRepository, EfExperimentEventRepository>();
            services.AddScoped<IUnitOfWork, EfUnitOfWork>();

            services.AddHealthChecks().AddDbContextCheck<FeatureFlareDbContext>("postgres");
        }
        else
        {
            services.AddSingleton<IFlagRepository, InMemoryFlagRepository>();
            services.AddSingleton<ISegmentRepository, InMemorySegmentRepository>();
            services.AddSingleton<IExperimentRepository, InMemoryExperimentRepository>();
            services.AddSingleton<IExperimentEventRepository, InMemoryExperimentEventRepository>();
            services.AddSingleton<IUnitOfWork, InMemoryUnitOfWork>();
        }

        services.AddSingleton<ISseBroadcaster, SseBroadcaster>();
        services.AddSingleton<IChangePublisher, SseChangePublisher>();
        services.AddHostedService<SseHeartbeatService>();

        return services;
    }

    /// <summary>
    /// Creates the schema when <c>Persistence:AutoCreateSchema</c> is true (local/dev/demo). Production
    /// deployments should apply versioned migrations instead — see "Known limitations" in the README.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, IConfiguration configuration)
    {
        if (!configuration.GetValue("Persistence:AutoCreateSchema", false))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetService<FeatureFlareDbContext>();
        if (db is null)
        {
            return;
        }

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DependencyInjection));
        var created = await db.Database.EnsureCreatedAsync();
        logger.LogInformation("FeatureFlare schema initialized (created: {Created})", created);
    }
}
