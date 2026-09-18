using FeatureFlare.Application.Abstractions;
using FeatureFlare.Application.Common;
using FeatureFlare.Domain.Experiments;
using FeatureFlare.Domain.Flags;
using FeatureFlare.Domain.Segments;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FeatureFlare.Infrastructure.Persistence.Ef;

internal sealed class EfFlagRepository(FeatureFlareDbContext db) : IFlagRepository
{
    public Task<FeatureFlag?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        db.Flags.SingleOrDefaultAsync(f => f.Key == key, cancellationToken);

    public async Task<IReadOnlyList<FeatureFlag>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Flags.OrderBy(f => f.Key).ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        db.Flags.AnyAsync(f => f.Key == key, cancellationToken);

    public void Add(FeatureFlag flag) => db.Flags.Add(flag);

    public void Remove(FeatureFlag flag) => db.Flags.Remove(flag);
}

internal sealed class EfSegmentRepository(FeatureFlareDbContext db) : ISegmentRepository
{
    public Task<Segment?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        db.Segments.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);

    public async Task<IReadOnlyList<Segment>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Segments.OrderBy(s => s.Key).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> FindMissingKeysAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var wanted = keys.Distinct(StringComparer.Ordinal).ToList();
        var existing = await db.Segments
            .Where(s => wanted.Contains(s.Key))
            .Select(s => s.Key)
            .ToListAsync(cancellationToken);

        return wanted.Except(existing, StringComparer.Ordinal).ToList();
    }

    public void Add(Segment segment) => db.Segments.Add(segment);

    public void Remove(Segment segment) => db.Segments.Remove(segment);
}

internal sealed class EfExperimentRepository(FeatureFlareDbContext db) : IExperimentRepository
{
    public Task<Experiment?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        db.Experiments.SingleOrDefaultAsync(e => e.Key == key, cancellationToken);

    public async Task<IReadOnlyList<Experiment>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Experiments.OrderBy(e => e.Key).ToListAsync(cancellationToken);

    public void Add(Experiment experiment) => db.Experiments.Add(experiment);

    public void Remove(Experiment experiment) => db.Experiments.Remove(experiment);
}

internal sealed class EfExperimentEventRepository(FeatureFlareDbContext db) : IExperimentEventRepository
{
    public async Task AddRangeAsync(IReadOnlyCollection<ExperimentEvent> events, CancellationToken cancellationToken = default)
    {
        // Analytics events are independent from configuration changes, so they are committed on their own.
        db.ExperimentEvents.AddRange(events);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<VariantAggregate>> AggregateAsync(
        string experimentKey,
        string metric,
        CancellationToken cancellationToken = default)
    {
        var exposures = db.ExperimentEvents
            .AsNoTracking()
            .Where(e => e.ExperimentKey == experimentKey && e.Kind == ExperimentEventKind.Exposure);

        var exposed = await exposures
            .GroupBy(e => e.Variant)
            .Select(g => new { Variant = g.Key, Users = g.Select(e => e.UserKey).Distinct().LongCount() })
            .ToListAsync(cancellationToken);

        // Only conversions from users exposed to the same variant count (translated to an EXISTS subquery).
        var converted = await db.ExperimentEvents
            .AsNoTracking()
            .Where(c => c.ExperimentKey == experimentKey
                        && c.Kind == ExperimentEventKind.Conversion
                        && c.Metric == metric
                        && exposures.Any(x => x.Variant == c.Variant && x.UserKey == c.UserKey))
            .GroupBy(c => c.Variant)
            .Select(g => new
            {
                Variant = g.Key,
                Users = g.Select(e => e.UserKey).Distinct().LongCount(),
                Total = g.Sum(e => e.Value ?? 0m),
            })
            .ToListAsync(cancellationToken);

        var conversions = converted.ToDictionary(c => c.Variant, StringComparer.Ordinal);
        return exposed
            .Select(e => conversions.TryGetValue(e.Variant, out var c)
                ? new VariantAggregate(e.Variant, e.Users, c.Users, c.Total)
                : new VariantAggregate(e.Variant, e.Users, 0, 0))
            .ToList();
    }
}

internal sealed class EfUnitOfWork(FeatureFlareDbContext db) : IUnitOfWork
{
    private const string UniqueViolation = "23505";

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var entry = ex.Entries.FirstOrDefault();
            throw new ConcurrencyException(entry?.Metadata.ClrType.Name ?? "Resource", entry?.Property("Key").CurrentValue?.ToString() ?? "?");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Two concurrent creates with the same key: the unique index is the real guard.
            throw new ConflictException("A resource with the same key already exists.");
        }
    }
}
