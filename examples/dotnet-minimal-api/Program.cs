using System.Text.Json.Serialization;
using FeatureFlare.Sdk;
using FeatureFlare.Sdk.Models;

// dotnet run --project examples/dotnet-minimal-api
// curl "http://localhost:5100/checkout?user=u-1&plan=enterprise"

var builder = WebApplication.CreateBuilder(args);

// Reads the "FeatureFlare" section (see appsettings.json). Registers IFeatureFlareClient as a singleton and a
// hosted service that initializes it at start-up (bounded by InitTimeout) and flushes events on shutdown.
builder.Services.AddFeatureFlare(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

var flags = app.Services.GetRequiredService<IFeatureFlareClient>();
flags.FlagChanged += (key, flag) =>
    app.Logger.LogInformation("Flag {Key} changed (enabled: {Enabled})", key, flag?.Enabled.ToString() ?? "deleted");

static EvaluationContext ContextFrom(HttpRequest request) =>
    new(request.Query["user"].FirstOrDefault() ?? "anonymous")
    {
        { "plan", request.Query["plan"].FirstOrDefault() ?? "free" },
        { "country", request.Query["country"].FirstOrDefault() },
    };

app.MapGet("/checkout", (HttpRequest request, IFeatureFlareClient client) =>
{
    var context = ContextFrom(request);
    return Results.Ok(new
    {
        newCheckout = client.GetBooleanValue("new-checkout", false, context),
        maxItems = client.GetNumberValue("max-cart-items", 20, context),
        experimentVariant = client.GetExperimentVariant("checkout-flow", context), // records an exposure
    });
});

app.MapPost("/purchase", (HttpRequest request, decimal total, IFeatureFlareClient client) =>
{
    client.TrackConversion("checkout-flow", ContextFrom(request), value: total);
    return Results.NoContent();
});

app.MapGet("/flags", (HttpRequest request, IFeatureFlareClient client) =>
    Results.Ok(client.GetAllFlags(ContextFrom(request))));

app.MapGet("/flags/{key}/explain", (string key, HttpRequest request, IFeatureFlareClient client) =>
    Results.Ok(client.GetDetail(key, ContextFrom(request))));

app.Run();
