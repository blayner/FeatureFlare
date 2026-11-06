# FeatureFlare.Sdk (.NET)

.NET 10 SDK for FeatureFlare: local flag evaluation, experiment assignment, real-time updates over SSE, and
batched event tracking.

## Install

```bash
dotnet add package FeatureFlare.Sdk
```

## ASP.NET Core / Generic Host

```jsonc
// appsettings.json
{
  "FeatureFlare": {
    "ApiKey": "<sdk key>",
    "BaseUrl": "https://flags.example.com",
    "EnableStreaming": true
  }
}
```

```csharp
builder.Services.AddFeatureFlare(builder.Configuration);

app.MapGet("/checkout", (IFeatureFlareClient flags, HttpContext http) =>
{
    var context = new EvaluationContext(http.User.FindFirstValue("sub") ?? "anonymous")
    {
        { "plan", "pro" },
        { "country", "BR" },
        { "appVersion", "2.4.1" },
    };

    return flags.GetBooleanValue("new-checkout", false, context) ? Results.Ok("new") : Results.Ok("old");
});
```

`AddFeatureFlare` registers:

- `IFeatureFlareClient` / `FeatureFlareClient` as a **singleton** (it holds the cached configuration).
- A hosted service that initializes the client at start-up. Start-up waits at most `InitTimeout`, so a
  FeatureFlare outage never prevents your app from starting. On shutdown it flushes queued events.
- Two named `HttpClient`s:
  - `FeatureFlare`: regular calls, with `AddStandardResilienceHandler()` (Polly v8: retry with jitter,
    circuit breaker, timeouts).
  - `FeatureFlare.Stream`: the SSE connection, with **no** timeout and no retry handler. A resilience
    pipeline with a request timeout would kill the long-lived stream; the listener owns reconnection
    (exponential backoff with full jitter, floored by the server's `retry:` hint).

## Standalone

```csharp
await using var client = new FeatureFlareClient(o =>
{
    o.ApiKey = "<sdk key>";
    o.BaseUrl = "https://flags.example.com";
});
await client.InitializeAsync();
```

## API

```csharp
bool   GetBooleanValue(string key, bool defaultValue, EvaluationContext context);
string GetStringValue(string key, string defaultValue, EvaluationContext context);
double GetNumberValue(string key, double defaultValue, EvaluationContext context);
T      GetJsonValue<T>(string key, T defaultValue, EvaluationContext context);
EvaluationDetail GetDetail(string key, EvaluationContext context);          // value + reason + rule id
IReadOnlyDictionary<string, JsonElement> GetAllFlags(EvaluationContext context);

string? GetExperimentVariant(string experimentKey, EvaluationContext context); // records an exposure
T?      GetExperimentPayload<T>(string experimentKey, EvaluationContext context);
void    TrackConversion(string experimentKey, EvaluationContext context, decimal? value = null, string? metric = null);

event Action<string, FlagConfig?>? FlagChanged;
event Action<string, ExperimentConfig?>? ExperimentChanged;
```

Evaluation methods never throw and never perform I/O.

## Thread safety

The configuration is an immutable snapshot (`ImmutableDictionary`) behind a volatile reference. Reads are
lock-free, and each evaluation sees one consistent snapshot even while an update is being applied. Writes
(stream events, polls) are serialized and copy-on-write, and versioned upserts discard stale data.

## Options

| Option | Default | |
|--------|---------|---|
| `ApiKey` | required | SDK key |
| `BaseUrl` | `http://localhost:5000` | |
| `EnableStreaming` | `true` | SSE updates |
| `RefreshInterval` | 30 s | Polling while the stream is disconnected or disabled |
| `CacheTtl` | 5 min | Forces a refresh when the snapshot is older than this |
| `FlushInterval` | 10 s | Event batch interval |
| `MaxQueueSize` / `EventBatchSize` | 10 000 / 100 | Bounded event buffer, oldest events dropped first |
| `TrackExposures` | `true` | Record exposures automatically |
| `InitTimeout` | 5 s | Max wait in `InitializeAsync` and at host start-up |
