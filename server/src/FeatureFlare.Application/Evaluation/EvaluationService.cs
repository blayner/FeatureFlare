using FeatureFlare.Application.Abstractions;
using FeatureFlare.Domain.Evaluation;
using FeatureFlare.Domain.Segments;

namespace FeatureFlare.Application.Evaluation;

/// <summary>
/// Server-side evaluation for clients that cannot (or should not) download flag rules, e.g. untrusted
/// browsers. SDKs running in trusted environments evaluate locally instead.
/// </summary>
public sealed class EvaluationService(
    IFlagRepository flags,
    ISegmentRepository segments,
    FlagEvaluationEngine engine)
{
    public async Task<IReadOnlyList<FlagEvaluation>> EvaluateAllAsync(
        EvaluationContext context,
        CancellationToken ct = default)
    {
        var allFlags = await flags.ListAsync(ct);
        var segmentMap = await LoadSegmentsAsync(ct);

        return allFlags
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(flag => engine.Evaluate(flag, context, segmentMap))
            .ToList();
    }

    public async Task<FlagEvaluation> EvaluateAsync(
        string flagKey,
        EvaluationContext context,
        CancellationToken ct = default)
    {
        var flag = await flags.GetByKeyAsync(flagKey, ct);
        if (flag is null)
        {
            return FlagEvaluation.NotFound(flagKey);
        }

        var segmentMap = await LoadSegmentsAsync(ct);
        return engine.Evaluate(flag, context, segmentMap);
    }

    private async Task<IReadOnlyDictionary<string, Segment>> LoadSegmentsAsync(CancellationToken ct) =>
        (await segments.ListAsync(ct)).ToDictionary(s => s.Key, StringComparer.Ordinal);
}
