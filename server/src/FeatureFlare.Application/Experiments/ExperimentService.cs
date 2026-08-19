using FeatureFlare.Application.Abstractions;
using FeatureFlare.Application.Common;
using FeatureFlare.Domain.Experiments;

namespace FeatureFlare.Application.Experiments;

public sealed record CreateExperimentCommand(
    string Key,
    string Name,
    string? Hypothesis,
    IReadOnlyList<ExperimentVariant> Variants,
    string ControlVariant,
    string PrimaryMetric,
    int TrafficAllocation = Experiment.FullTraffic);

public sealed record RedesignExperimentCommand(
    string Key,
    string Name,
    string? Hypothesis,
    IReadOnlyList<ExperimentVariant> Variants,
    string ControlVariant,
    string PrimaryMetric,
    int? ExpectedVersion);

public sealed class ExperimentService(
    IExperimentRepository experiments,
    IUnitOfWork unitOfWork,
    IChangePublisher publisher,
    TimeProvider clock)
{
    public Task<IReadOnlyList<Experiment>> ListAsync(CancellationToken ct = default) => experiments.ListAsync(ct);

    public async Task<Experiment> GetAsync(string key, CancellationToken ct = default) =>
        await experiments.GetByKeyAsync(key, ct) ?? throw new NotFoundException("Experiment", key);

    public async Task<Experiment> CreateAsync(CreateExperimentCommand command, CancellationToken ct = default)
    {
        if (await experiments.GetByKeyAsync(command.Key, ct) is not null)
        {
            throw new ConflictException($"Experiment '{command.Key}' already exists.");
        }

        var experiment = Experiment.Create(
            command.Key,
            command.Name,
            command.Hypothesis,
            command.Variants,
            command.ControlVariant,
            command.PrimaryMetric,
            command.TrafficAllocation);

        experiments.Add(experiment);
        return await CommitAsync(experiment, ct);
    }

    public async Task<Experiment> RedesignAsync(RedesignExperimentCommand command, CancellationToken ct = default)
    {
        var experiment = await GetAsync(command.Key, ct);
        if (command.ExpectedVersion is not null && command.ExpectedVersion != experiment.Version)
        {
            throw new ConcurrencyException("Experiment", experiment.Key, command.ExpectedVersion, experiment.Version);
        }

        experiment.Redesign(command.Name, command.Hypothesis, command.Variants, command.ControlVariant, command.PrimaryMetric);
        return await CommitAsync(experiment, ct);
    }

    public Task<Experiment> StartAsync(string key, CancellationToken ct = default) =>
        MutateAsync(key, e => e.Start(clock.GetUtcNow()), ct);

    public Task<Experiment> PauseAsync(string key, CancellationToken ct = default) =>
        MutateAsync(key, e => e.Pause(), ct);

    public Task<Experiment> ConcludeAsync(string key, string? winningVariant, CancellationToken ct = default) =>
        MutateAsync(key, e => e.Conclude(clock.GetUtcNow(), winningVariant), ct);

    public Task<Experiment> SetTrafficAsync(string key, int trafficAllocation, CancellationToken ct = default) =>
        MutateAsync(key, e => e.SetTrafficAllocation(trafficAllocation), ct);

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var experiment = await GetAsync(key, ct);
        if (experiment.Status == ExperimentStatus.Running)
        {
            throw new ConflictException("Pause or conclude the experiment before deleting it.");
        }

        experiments.Remove(experiment);
        await unitOfWork.SaveChangesAsync(ct);
        await publisher.ExperimentDeletedAsync(experiment.Key, experiment.Version + 1, ct);
    }

    private async Task<Experiment> MutateAsync(string key, Action<Experiment> mutation, CancellationToken ct)
    {
        var experiment = await GetAsync(key, ct);
        mutation(experiment);
        return await CommitAsync(experiment, ct);
    }

    private async Task<Experiment> CommitAsync(Experiment experiment, CancellationToken ct)
    {
        await unitOfWork.SaveChangesAsync(ct);
        await publisher.ExperimentUpdatedAsync(experiment, ct);
        return experiment;
    }
}
