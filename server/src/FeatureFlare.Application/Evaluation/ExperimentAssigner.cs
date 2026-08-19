using FeatureFlare.Domain.Experiments;

namespace FeatureFlare.Application.Evaluation;

public enum AssignmentReason
{
    Assigned,
    NotRunning,
    NotInTraffic,
}

public sealed record ExperimentAssignment(string ExperimentKey, string? Variant, AssignmentReason Reason)
{
    public bool InExperiment => Reason == AssignmentReason.Assigned;
}

/// <summary>
/// Deterministic variant assignment, identical to the SDK implementations:
/// traffic gate uses salt "{salt}.traffic", variant split uses "{salt}".
/// </summary>
public static class ExperimentAssigner
{
    public static ExperimentAssignment Assign(Experiment experiment, string userKey)
    {
        if (experiment.Status != ExperimentStatus.Running)
        {
            return new ExperimentAssignment(experiment.Key, null, AssignmentReason.NotRunning);
        }

        var trafficBucket = Bucketing.Bucket(experiment.Key, experiment.Salt + ".traffic", userKey);
        if (trafficBucket >= experiment.TrafficAllocation)
        {
            return new ExperimentAssignment(experiment.Key, null, AssignmentReason.NotInTraffic);
        }

        var bucket = Bucketing.Bucket(experiment.Key, experiment.Salt, userKey);
        var index = Bucketing.Pick(experiment.Variants, v => v.Weight, bucket);
        return new ExperimentAssignment(experiment.Key, experiment.Variants[index].Key, AssignmentReason.Assigned);
    }
}
