using FeatureFlare.Api.Auth;
using FeatureFlare.Application.Sdk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFlare.Api.Controllers;

[ApiController]
[Route("api/sdk")]
[Authorize(Policy = Policies.Sdk)]
[Produces("application/json")]
public sealed class SdkController(SdkConfigService configService) : ControllerBase
{
    /// <summary>Full configuration (flags + segments) for local evaluation in server-side SDKs.</summary>
    [HttpGet("config")]
    [ResponseCache(NoStore = true)]
    public Task<SdkConfig> GetConfig(CancellationToken ct) => configService.GetAsync(ct);
}
