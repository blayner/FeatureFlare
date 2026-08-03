using FeatureFlare.Api.Auth;
using FeatureFlare.Api.Contracts;
using FeatureFlare.Application.Evaluation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFlare.Api.Controllers;

/// <summary>
/// Remote evaluation for clients that should not see targeting rules (browsers, mobile apps).
/// Server-side SDKs download the config once and evaluate locally instead.
/// </summary>
[ApiController]
[Route("api/evaluate")]
[Authorize(Policy = Policies.Sdk)]
[Produces("application/json")]
public sealed class EvaluationController(EvaluationService evaluation) : ControllerBase
{
    [HttpPost]
    public async Task<EvaluateAllResponse> EvaluateAll(EvaluationContextDto context, CancellationToken ct)
    {
        var results = await evaluation.EvaluateAllAsync(context.ToDomain(), ct);
        return new EvaluateAllResponse(results.ToDictionary(r => r.FlagKey, FlagEvaluationDto.From, StringComparer.Ordinal));
    }

    [HttpPost("{key}")]
    public async Task<FlagEvaluationDto> Evaluate(string key, EvaluationContextDto context, CancellationToken ct) =>
        FlagEvaluationDto.From(await evaluation.EvaluateAsync(key, context.ToDomain(), ct));
}
