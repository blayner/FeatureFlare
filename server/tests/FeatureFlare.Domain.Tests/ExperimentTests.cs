using FeatureFlare.Application.Evaluation;
using FeatureFlare.Application.Experiments;
using FeatureFlare.Domain.Common;
using FeatureFlare.Domain.Experiments;

namespace FeatureFlare.Domain.Tests;

public sealed class ExperimentTests
{
    [Fact]
    public void Lifecycle_draft_running_paused_concluded()
    {
        var experiment = TestData.RunningExperiment();
        experiment.Status.ShouldBe(ExperimentStatus.Running);
        var startedAt = experiment.StartedAt;

        experiment.Pause();
        experiment.Start(DateTimeOffset.UtcNow.AddHours(1));
        experiment.StartedAt.ShouldBe(startedAt); // restart keeps the original start

        experiment.Conclude(DateTimeOffset.UtcNow, "treatment");
        experiment.Status.ShouldBe(ExperimentStatus.Concluded);
        experiment.WinningVariant.ShouldBe("treatment");

        Should.Throw<DomainException>(() => experiment.Start(DateTimeOffset.UtcNow));
        Should.Throw<DomainException>(() => experiment.SetTrafficAllocation(5_000));
    }

    [Fact]
    public void Design_is_frozen_after_start()
    {
        var experiment = TestData.RunningExperiment();

        Should.Throw<DomainException>(() => experiment.Redesign(
            "x", null, [new ExperimentVariant("a", 5_000), new ExperimentVariant("b", 5_000)], "a", "purchase"));
    }

    [Fact]
    public void Variant_weights_must_sum_to_100_percent()
    {
        Should.Throw<DomainException>(() => Experiment.Create(
            "exp", "Exp", null, [new ExperimentVariant("a", 5_000), new ExperimentVariant("b", 4_999)], "a", "purchase"));
    }

    [Fact]
    public void Assignment_follows_weights()
    {
        var experiment = TestData.RunningExperiment(Experiment.FullTraffic, ("control", 5_000), ("b", 3_000), ("c", 2_000));

        var counts = Enumerable.Range(0, 30_000)
            .Select(i => ExperimentAssigner.Assign(experiment, $"user-{i}").Variant!)
            .GroupBy(v => v)
            .ToDictionary(g => g.Key, g => g.Count() / 30_000.0);

        counts["control"].ShouldBeInRange(0.48, 0.52);
        counts["b"].ShouldBeInRange(0.28, 0.32);
        counts["c"].ShouldBeInRange(0.18, 0.22);
    }

    [Fact]
    public void Ramping_traffic_up_only_adds_users_and_never_moves_existing_ones()
    {
        var experiment = TestData.RunningExperiment(traffic: 1_000);
        var users = Enumerable.Range(0, 10_000).Select(i => $"user-{i}").ToList();

        var before = users.ToDictionary(u => u, u => ExperimentAssigner.Assign(experiment, u).Variant);
        experiment.SetTrafficAllocation(5_000);
        var after = users.ToDictionary(u => u, u => ExperimentAssigner.Assign(experiment, u).Variant);

        foreach (var (user, variant) in before.Where(kv => kv.Value is not null))
        {
            after[user].ShouldBe(variant);
        }

        after.Count(kv => kv.Value is not null).ShouldBeGreaterThan(before.Count(kv => kv.Value is not null) * 4);
    }

    [Fact]
    public void Not_running_experiments_assign_nobody()
    {
        var experiment = TestData.RunningExperiment();
        experiment.Pause();

        ExperimentAssigner.Assign(experiment, "u1").Reason.ShouldBe(AssignmentReason.NotRunning);
    }
}

public sealed class StatisticsTests
{
    [Fact]
    public void Erf_matches_reference_values()
    {
        Statistics.Erf(0).ShouldBe(0, 1e-7);
        Statistics.Erf(1).ShouldBe(0.8427007929, 1e-6);
        Statistics.Erf(-1).ShouldBe(-0.8427007929, 1e-6);
        Statistics.NormalCdf(1.959963985).ShouldBe(0.975, 1e-6);
    }

    [Fact]
    public void Detects_a_large_difference_as_significant()
    {
        var result = Statistics.CompareProportions(10_000, 1_000, 10_000, 1_200)!;

        result.AbsoluteDifference.ShouldBe(0.02, 1e-9);
        result.RelativeLift!.Value.ShouldBe(0.2, 1e-9);
        result.PValue.ShouldBeLessThan(0.001);
        result.ConfidenceIntervalLow.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Small_samples_are_not_significant()
    {
        var result = Statistics.CompareProportions(50, 5, 50, 7)!;

        result.PValue.ShouldBeGreaterThan(0.05);
        result.ConfidenceIntervalLow.ShouldBeLessThan(0);
    }

    [Fact]
    public void Returns_null_without_data()
    {
        Statistics.CompareProportions(0, 0, 100, 10).ShouldBeNull();
    }
}
