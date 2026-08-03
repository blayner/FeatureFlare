using FeatureFlare.Api.Auth;
using FeatureFlare.Api.Contracts;
using FeatureFlare.Application.Segments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFlare.Api.Controllers;

[ApiController]
[Route("api/segments")]
[Authorize(Policy = Policies.Admin)]
[Produces("application/json")]
public sealed class SegmentsController(SegmentService segments) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<SegmentResponse>> List(CancellationToken ct) =>
        (await segments.ListAsync(ct)).Select(SegmentResponse.From);

    [HttpGet("{key}")]
    public async Task<SegmentResponse> Get(string key, CancellationToken ct) =>
        SegmentResponse.From(await segments.GetAsync(key, ct));

    [HttpPost]
    [ProducesResponseType<SegmentResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateSegmentRequest request, CancellationToken ct)
    {
        var segment = await segments.CreateAsync(request.ToCommand(), ct);
        return CreatedAtAction(nameof(Get), new { key = segment.Key }, SegmentResponse.From(segment));
    }

    [HttpPut("{key}")]
    public async Task<SegmentResponse> Update(string key, UpdateSegmentRequest request, CancellationToken ct) =>
        SegmentResponse.From(await segments.UpdateAsync(request.ToCommand(key), ct));

    [HttpDelete("{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string key, CancellationToken ct)
    {
        await segments.DeleteAsync(key, ct);
        return NoContent();
    }
}
