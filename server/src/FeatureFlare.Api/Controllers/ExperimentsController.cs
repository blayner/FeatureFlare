using FeatureFlare.Api.Auth;
using FeatureFlare.Api.Contracts;
using FeatureFlare.Application.Experiments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFlare.Api.Controllers;

[ApiController]
[Route("api/experiments")]
[Authorize(Policy = Policies.Admin)]
[Produces("application/json")]
public sealed class ExperimentsController(ExperimentService experiments) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<ExperimentResponse>> List(CancellationToken ct) =>
        (await experiments.ListAsync(ct)).Select(ExperimentResponse.From);

    [HttpGet("{key}")]
    public async Task<ExperimentResponse> Get(string key, CancellationToken ct) =>
        ExperimentResponse.From(await experiments.GetAsync(key, ct));

    [HttpPost]
    [ProducesResponseType<ExperimentResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateExperimentRequest request, CancellationToken ct)
    {
        var experiment = await experiments.CreateAsync(request.ToCommand(), ct);
        return CreatedAtAction(nameof(Get), new { key = experiment.Key }, ExperimentResponse.From(experiment));
    }

    /// <summary>Changes variants/metric. Only allowed while the experiment is a draft.</summary>
    [HttpPut("{key}")]
    public async Task<ExperimentResponse> Redesign(string key, RedesignExperimentRequest request, CancellationToken ct) =>
        ExperimentResponse.From(await experiments.RedesignAsync(request.ToCommand(key), ct));

    [HttpPost("{key}/start")]
    public async Task<ExperimentResponse> Start(string key, CancellationToken ct) =>
        ExperimentResponse.From(await experiments.StartAsync(key, ct));

    [HttpPost("{key}/pause")]
    public async Task<ExperimentResponse> Pause(string key, CancellationToken ct) =>
        ExperimentResponse.From(await experiments.PauseAsync(key, ct));

    [HttpPost("{key}/conclude")]
    public async Task<ExperimentResponse> Conclude(string key, ConcludeExperimentRequest request, CancellationToken ct) =>
        ExperimentResponse.From(await experiments.ConcludeAsync(key, request.WinningVariant, ct));

    [HttpPut("{key}/traffic")]
    public async Task<ExperimentResponse> SetTraffic(string key, SetTrafficRequest request, CancellationToken ct) =>
        ExperimentResponse.From(await experiments.SetTrafficAsync(key, request.TrafficAllocation, ct));

    [HttpDelete("{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
    {
        await experiments.DeleteAsync(key, ct);
        return NoContent();
    }
}
