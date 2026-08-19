using FeatureFlare.Domain.Experiments;
using FeatureFlare.Domain.Flags;
using FeatureFlare.Domain.Segments;

namespace FeatureFlare.Application.Abstractions;

public interface IFlagRepository
{
    Task<FeatureFlag?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureFlag>> ListAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    void Add(FeatureFlag flag);

    void Remove(FeatureFlag flag);
}

public interface ISegmentRepository
{
    Task<Segment?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Segment>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> FindMissingKeysAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    void Add(Segment segment);

    void Remove(Segment segment);
}

public interface IExperimentRepository
{
    Task<Experiment?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Experiment>> ListAsync(CancellationToken cancellationToken = default);

    void Add(Experiment experiment);

    void Remove(Experiment experiment);
}

public interface IUnitOfWork
{
    /// <summary>Persists pending changes. Throws <see cref="Common.ConcurrencyException"/> on version conflicts.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
