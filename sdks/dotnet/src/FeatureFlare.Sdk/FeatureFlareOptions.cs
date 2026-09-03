namespace FeatureFlare.Sdk;

public sealed class FeatureFlareOptions
{
    public const string SectionName = "FeatureFlare";

    /// <summary>SDK key (read-only). Never configure an admin key in an application.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>Receive configuration changes in real time over SSE.</summary>
    public bool EnableStreaming { get; set; } = true;

    /// <summary>Polling interval used when streaming is disabled or disconnected.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Max age of the local snapshot before a refresh is forced, even while streaming.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(10);

    public int MaxQueueSize { get; set; } = 10_000;

    public int EventBatchSize { get; set; } = 100;

    /// <summary>Record an exposure the first time a user is assigned to an experiment variant.</summary>
    public bool TrackExposures { get; set; } = true;

    /// <summary>How long <c>InitializeAsync</c> (and host start-up) waits for the first config download.</summary>
    public TimeSpan InitTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan StreamInitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan StreamMaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("FeatureFlare: ApiKey is required.");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException($"FeatureFlare: BaseUrl '{BaseUrl}' is not an absolute URI.");
        }
    }

    internal Uri Endpoint(string relative) => new(new Uri(BaseUrl.TrimEnd('/') + "/"), relative);
}
