namespace FeatureFlare.Domain.Common;

/// <summary>
/// Base type for aggregates. <see cref="Version"/> is incremented on every state change and is used
/// both as an optimistic concurrency token and as the ordering key SDKs use to discard stale updates.
/// </summary>
public abstract class AggregateRoot
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public int Version { get; protected set; } = 1;

    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; protected set; } = DateTimeOffset.UtcNow;

    protected void Touch()
    {
        Version++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
