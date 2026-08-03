using FeatureFlare.Api.Auth;
using FeatureFlare.Api.Contracts;
using FeatureFlare.Application.Evaluation;
using FeatureFlare.Application.Flags;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFlare.Api.Controllers;

[ApiController]
[Route("api/flags")]
[Authorize(Policy = Policies.Admin)]
[Produces("application/json")]
public sealed class FlagsController(FlagService flags, EvaluationService evaluation) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<FlagResponse>> List(CancellationToken ct) =>
        (await flags.ListAsync(ct)).Select(FlagResponse.From);

    [HttpGet("{key}")]
    public async Task<FlagResponse> Get(string key, CancellationToken ct) =>
        FlagResponse.From(await flags.GetAsync(key, ct));

    [HttpPost]
    [ProducesResponseType<FlagResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(CreateFlagRequest request, CancellationToken ct)
    {
        var flag = await flags.CreateAsync(request.ToCommand(), ct);
        return CreatedAtAction(nameof(Get), new { key = flag.Key }, FlagResponse.From(flag));
    }

    [HttpPut("{key}")]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    public async Task<FlagResponse> Update(string key, UpdateFlagRequest request, CancellationToken ct) =>
        FlagResponse.From(await flags.UpdateAsync(request.ToCommand(key), ct));

    [HttpPatch("{key}/toggle")]
    public async Task<FlagResponse> Toggle(string key, ToggleFlagRequest request, CancellationToken ct) =>
        FlagResponse.From(await flags.ToggleAsync(new ToggleFlagCommand(key, request.Enabled, request.ExpectedVersion), ct));

    [HttpDelete("{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
    {
        await flags.DeleteAsync(new DeleteFlagCommand(key), ct);
        return NoContent();
    }

    /// <summary>Debug endpoint: explains how a flag evaluates for a given context.</summary>
    [HttpPost("{key}/evaluate")]
    public async Task<FlagEvaluationDto> Evaluate(string key, EvaluationContextDto context, CancellationToken ct) =>
        FlagEvaluationDto.From(await evaluation.EvaluateAsync(key, context.ToDomain(), ct));
}
