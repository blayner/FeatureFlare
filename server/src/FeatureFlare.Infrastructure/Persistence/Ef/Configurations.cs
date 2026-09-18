using FeatureFlare.Domain.Experiments;
using FeatureFlare.Domain.Flags;
using FeatureFlare.Domain.Segments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFlare.Infrastructure.Persistence.Ef;

internal sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("flags");
        builder.HasKey(f => f.Id);
        builder.HasIndex(f => f.Key).IsUnique();

        builder.Property(f => f.Key).HasMaxLength(64).IsRequired();
        builder.Property(f => f.Name).HasMaxLength(200).IsRequired();
        builder.Property(f => f.Description).HasMaxLength(2000);
        builder.Property(f => f.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(f => f.Salt).HasMaxLength(64).IsRequired();
        builder.Property(f => f.OffVariation).HasMaxLength(64).IsRequired();
        builder.Property(f => f.Version).IsConcurrencyToken();
        builder.Property(f => f.Fallthrough).HasJsonConversion();

        builder.Ignore(f => f.Variations);
        builder.Ignore(f => f.Rules);
        builder.Property<List<Variation>>("_variations").HasColumnName("variations").HasJsonConversion();
        builder.Property<List<TargetingRule>>("_rules").HasColumnName("rules").HasJsonConversion();
    }
}

internal sealed class SegmentConfiguration : IEntityTypeConfiguration<Segment>
{
    public void Configure(EntityTypeBuilder<Segment> builder)
    {
        builder.ToTable("segments");
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.Key).IsUnique();

        builder.Property(s => s.Key).HasMaxLength(64).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(2000);
        builder.Property(s => s.Version).IsConcurrencyToken();

        builder.Ignore(s => s.Included);
        builder.Ignore(s => s.Excluded);
        builder.Ignore(s => s.Rules);

        // Explicit membership lists map to native text[] columns.
        builder.Property<List<string>>("_included").HasColumnName("included");
        builder.Property<List<string>>("_excluded").HasColumnName("excluded");
        builder.Property<List<SegmentRule>>("_rules").HasColumnName("rules").HasJsonConversion();
    }
}

internal sealed class ExperimentConfiguration : IEntityTypeConfiguration<Experiment>
{
    public void Configure(EntityTypeBuilder<Experiment> builder)
    {
        builder.ToTable("experiments");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.Key).IsUnique();

        builder.Property(e => e.Key).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Hypothesis).HasMaxLength(2000);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.Salt).HasMaxLength(64).IsRequired();
        builder.Property(e => e.ControlVariant).HasMaxLength(64).IsRequired();
        builder.Property(e => e.PrimaryMetric).HasMaxLength(64).IsRequired();
        builder.Property(e => e.WinningVariant).HasMaxLength(64);
        builder.Property(e => e.Version).IsConcurrencyToken();

        builder.Ignore(e => e.Variants);
        builder.Property<List<ExperimentVariant>>("_variants").HasColumnName("variants").HasJsonConversion();
    }
}

internal sealed class ExperimentEventConfiguration : IEntityTypeConfiguration<ExperimentEvent>
{
    public void Configure(EntityTypeBuilder<ExperimentEvent> builder)
    {
        builder.ToTable("experiment_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ExperimentKey).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Variant).HasMaxLength(64).IsRequired();
        builder.Property(e => e.UserKey).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.Metric).HasMaxLength(64);
        builder.Property(e => e.Value).HasPrecision(18, 4);

        // Covers both aggregation queries (exposures per variant, conversions per metric).
        builder.HasIndex(e => new { e.ExperimentKey, e.Kind, e.Variant, e.UserKey });
        builder.HasIndex(e => new { e.ExperimentKey, e.Metric });
    }
}
