using FeatureFlare.Api.Auth;
using FeatureFlare.Api.Errors;
using FeatureFlare.Application;
using FeatureFlare.Application.Serialization;
using FeatureFlare.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options => FeatureFlareJson.Configure(options.JsonSerializerOptions));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddAuthentication(ApiKeyDefaults.Scheme)
    .AddScheme<ApiKeyOptions, ApiKeyAuthenticationHandler>(
        ApiKeyDefaults.Scheme,
        options => builder.Configuration.GetSection("Auth").Bind(options));

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Admin, policy => policy.RequireClaim(ApiKeyDefaults.KeyTypeClaim, ApiKeyDefaults.AdminKeyType))
    .AddPolicy(Policies.Sdk, policy => policy.RequireClaim(
        ApiKeyDefaults.KeyTypeClaim, ApiKeyDefaults.SdkKeyType, ApiKeyDefaults.AdminKeyType));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();

// Built-in OpenAPI document generation (Microsoft.AspNetCore.OpenApi), served at /openapi/v1.json.
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new()
    {
        Title = "FeatureFlare API",
        Version = "v1",
        Description = "Send an admin key (management) or SDK key (evaluation/streaming) in the X-Api-Key header.",
    };
    return Task.CompletedTask;
}));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

await app.Services.InitializeDatabaseAsync(app.Configuration);
await app.RunAsync();

public partial class Program;
