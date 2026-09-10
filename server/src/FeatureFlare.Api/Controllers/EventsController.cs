using FeatureFlare.Api.Auth;
using FeatureFlare.Api.Contracts;
using FeatureFlare.Application.Experiments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFlare.Api.Controllers;

/// <summary>Batch ingestion of exposure and conversion events sent by the SDKs.</summary>
[ApiController]
[Route("api/events")]
[Authorize(Policy = Policies.Sdk)]
[Produces("application/json")]
public sealed class EventsController(ExperimentEventService ingestion) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(1_048_576)]
    [ProducesResponseType<IngestionResult>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Track(TrackEventsRequest request, CancellationToken ct)
    {
        var result = await ingestion.IngestAsync(request.Events.Select(e => e.ToInput()).ToList(), ct);
        return Accepted(result);
    }
}
