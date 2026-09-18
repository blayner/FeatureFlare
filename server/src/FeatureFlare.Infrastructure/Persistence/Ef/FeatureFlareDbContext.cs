using FeatureFlare.Domain.Experiments;
using FeatureFlare.Domain.Flags;
using FeatureFlare.Domain.Segments;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlare.Infrastructure.Persistence.Ef;

public sealed class FeatureFlareDbContext(DbContextOptions<FeatureFlareDbContext> options) : DbContext(options)
{
    public DbSet<FeatureFlag> Flags => Set<FeatureFlag>();

    public DbSet<Segment> Segments => Set<Segment>();

    public DbSet<Experiment> Experiments => Set<Experiment>();

    public DbSet<ExperimentEvent> ExperimentEvents => Set<ExperimentEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("featureflare");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FeatureFlareDbContext).Assembly);
    }
}
