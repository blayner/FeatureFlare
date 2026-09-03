using System.Text.Json;
using FeatureFlare.Sdk.Models;

namespace FeatureFlare.Sdk;

/// <summary>
/// Evaluates feature flags and experiments locally against a cached copy of the server configuration.
/// Evaluation methods never throw and never perform I/O; on any problem they return the default value.
/// </summary>
public interface IFeatureFlareClient
{
    bool IsReady { get; }

    event Action<string, FlagConfig?>? FlagChanged;

    event Action<string, ExperimentConfig?>? ExperimentChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    bool GetBooleanValue(string key, bool defaultValue, EvaluationContext context);

    string GetStringValue(string key, string defaultValue, EvaluationContext context);

    double GetNumberValue(string key, double defaultValue, EvaluationContext context);

    T GetJsonValue<T>(string key, T defaultValue, EvaluationContext context);

    EvaluationDetail GetDetail(string key, EvaluationContext context);

    IReadOnlyDictionary<string, JsonElement> GetAllFlags(EvaluationContext context);

    string? GetExperimentVariant(string experimentKey, EvaluationContext context);

    ExperimentAssignment GetExperimentAssignment(string experimentKey, EvaluationContext context);

    T? GetExperimentPayload<T>(string experimentKey, EvaluationContext context);

    void TrackConversion(string experimentKey, EvaluationContext context, decimal? value = null, string? metric = null);

    Task FlushAsync(CancellationToken cancellationToken = default);
}
