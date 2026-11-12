using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using FeatureFlare.Sdk.Evaluation;
using FeatureFlare.Sdk.Events;
using FeatureFlare.Sdk.Models;
using FeatureFlare.Sdk.Streaming;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;

namespace FeatureFlare.Sdk;

/// <inheritdoc cref="IFeatureFlareClient"/>
public sealed class FeatureFlareClient : IFeatureFlareClient, IAsyncDisposable, IDisposable
{
    private const int MaxExposureCache = 50_000;

    private readonly FeatureFlareOptions _options;
    private readonly HttpClient _http;
    private readonly HttpClient _streamHttp;
    private readonly bool _ownsHttpClients;
    private readonly ILogger _logger;
    private readonly ConfigStore _store;
    private readonly EventProcessor _events;
    private readonly ConcurrentDictionary<string, byte> _exposures = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Lock _initLock = new();
    private Task? _initTask;
    private Task? _backgroundTask;
    private volatile bool _streamConnected;
    private int _disposed;

    /// <summary>Standalone usage (console apps, scripts). Prefer <c>services.AddFeatureFlare()</c> in hosts.</summary>
    public FeatureFlareClient(Action<FeatureFlareOptions> configure)
        : this(CreateStandalone(configure, out var options, out var stream), stream, options, logger: null, ownsHttpClients: true)
    {
    }

    /// <summary>Used by dependency injection; <paramref name="streamHttpClient"/> must have no timeout.</summary>
    public FeatureFlareClient(
        HttpClient httpClient,
        HttpClient streamHttpClient,
        IOptions<FeatureFlareOptions> options,
        ILogger<FeatureFlareClient>? logger = null)
        : this(httpClient, streamHttpClient, options.Value, logger, ownsHttpClients: false)
    {
    }

    private FeatureFlareClient(
        HttpClient httpClient,
        HttpClient streamHttpClient,
        FeatureFlareOptions options,
        ILogger? logger,
        bool ownsHttpClients)
    {
        options.Validate();
        _options = options;
        _http = httpClient;
        _streamHttp = streamHttpClient;
        _ownsHttpClients = ownsHttpClients;
        _logger = logger ?? NullLogger.Instance;
        _store = new ConfigStore(TimeProvider.System);
        _events = new EventProcessor(
            _http, options.Endpoint("api/events"), options.ApiKey, options.MaxQueueSize, options.EventBatchSize, _logger);
    }

    public bool IsReady => _store.IsInitialized;

    public event Action<string, FlagConfig?>? FlagChanged;

    public event Action<string, ExperimentConfig?>? ExperimentChanged;

    /// <summary>
    /// Downloads the configuration and starts background streaming/polling. Completes when flags are
    /// available or <see cref="FeatureFlareOptions.InitTimeout"/> elapses. Safe to call multiple times.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_initLock)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            _initTask ??= InitializeCoreAsync();
        }

        return _initTask.WaitAsync(cancellationToken);
    }

    // ---------------------------------------------------------------------------------------------------
    // Flags

    public bool GetBooleanValue(string key, bool defaultValue, EvaluationContext context) =>
        GetTyped(key, defaultValue, context, FlagType.Boolean, static e => e.ValueKind switch
        {
            JsonValueKind.True => (true, true),
            JsonValueKind.False => (true, false),
            _ => (false, false),
        });

    public string GetStringValue(string key, string defaultValue, EvaluationContext context) =>
        GetTyped(key, defaultValue, context, FlagType.String, static e =>
            e.ValueKind == JsonValueKind.String ? (true, e.GetString()!) : (false, string.Empty));

    public double GetNumberValue(string key, double defaultValue, EvaluationContext context) =>
        GetTyped(key, defaultValue, context, FlagType.Number, static e =>
            e.ValueKind == JsonValueKind.Number ? (true, e.GetDouble()) : (false, 0d));

    public T GetJsonValue<T>(string key, T defaultValue, EvaluationContext context)
    {
        var detail = GetDetail(key, context);
        if (detail.Value is not { } value)
        {
            return defaultValue;
        }

        try
        {
            return value.Deserialize<T>(SdkJson.Options) ?? defaultValue;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Flag {FlagKey} value cannot be converted to {Type}", key, typeof(T).Name);
            return defaultValue;
        }
    }

    public EvaluationDetail GetDetail(string key, EvaluationContext context)
    {
        try
        {
            return FlagEvaluator.Evaluate(_store.Current, key, context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Evaluation of flag {FlagKey} failed", key);
            return EvaluationDetail.Failed(key, EvaluationReason.Error, ex.Message);
        }
    }

    public IReadOnlyDictionary<string, JsonElement> GetAllFlags(EvaluationContext context)
    {
        var snapshot = _store.Current;
        var result = new Dictionary<string, JsonElement>(snapshot.Flags.Count, StringComparer.Ordinal);
        foreach (var flag in snapshot.Flags.Values)
        {
            if (FlagEvaluator.Evaluate(snapshot, flag, context) is { Value: { } value })
            {
                result[flag.Key] = value;
            }
        }

        return result;
    }

    // ---------------------------------------------------------------------------------------------------
    // Experiments

    public string? GetExperimentVariant(string experimentKey, EvaluationContext context) =>
        GetExperimentAssignment(experimentKey, context).Variant;

    public ExperimentAssignment GetExperimentAssignment(string experimentKey, EvaluationContext context)
    {
        var assignment = FlagEvaluator.Assign(_store.Current, experimentKey, context.Key);
        if (assignment.Variant is not null && _options.TrackExposures)
        {
            RecordExposure(assignment, context.Key);
        }

        return assignment;
    }

    public T? GetExperimentPayload<T>(string experimentKey, EvaluationContext context)
    {
        var variant = GetExperimentVariant(experimentKey, context);
        if (variant is null || !_store.Current.Experiments.TryGetValue(experimentKey, out var experiment))
        {
            return default;
        }

        var payload = experiment.Variants.FirstOrDefault(v => v.Key == variant)?.Payload;
        return payload is { } element ? element.Deserialize<T>(SdkJson.Options) : default;
    }

    public void TrackConversion(string experimentKey, EvaluationContext context, decimal? value = null, string? metric = null)
    {
        var assignment = FlagEvaluator.Assign(_store.Current, experimentKey, context.Key);
        _events.Enqueue(new TrackEvent(
            TrackEvent.Conversion, experimentKey, assignment.Variant, context.Key, metric, value, DateTimeOffset.UtcNow));
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => _events.FlushAsync(cancellationToken);

    // ---------------------------------------------------------------------------------------------------
    // Lifecycle

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        if (_backgroundTask is not null)
        {
            try
            {
                await _backgroundTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _events.DisposeAsync().ConfigureAwait(false);
        _shutdown.Dispose();
        _refreshLock.Dispose();

        if (_ownsHttpClients)
        {
            _http.Dispose();
            _streamHttp.Dispose();
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    // ---------------------------------------------------------------------------------------------------
    // Internals

    private async Task InitializeCoreAsync()
    {
        var token = _shutdown.Token;
        _backgroundTask = Task.WhenAll(
            Task.Run(() => PollLoopAsync(token), token),
            Task.Run(() => _events.RunAsync(_options.FlushInterval, token), token),
            _options.EnableStreaming ? Task.Run(() => StreamLoopAsync(token), token) : Task.CompletedTask);

        try
        {
            await RefreshAsync(token).WaitAsync(_options.InitTimeout, token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("FeatureFlare not initialized after {Timeout}; serving default values", _options.InitTimeout);
        }
    }

    private T GetTyped<T>(
        string key,
        T defaultValue,
        EvaluationContext context,
        FlagType expectedType,
        Func<JsonElement, (bool Ok, T Value)> convert)
    {
        if (_store.Current.Flags.TryGetValue(key, out var flag) && flag.Type != expectedType)
        {
            _logger.LogWarning("Flag {FlagKey} is {Actual}, not {Expected}; serving default", key, flag.Type, expectedType);
            return defaultValue;
        }

        var detail = GetDetail(key, context);
        if (detail.Value is not { } value)
        {
            return defaultValue;
        }

        var (ok, converted) = convert(value);
        return ok ? converted : defaultValue;
    }

    private void RecordExposure(ExperimentAssignment assignment, string userKey)
    {
        var dedupeKey = string.Concat(assignment.ExperimentKey, "\0", assignment.Variant, "\0", userKey);
        if (_exposures.Count >= MaxExposureCache)
        {
            _exposures.Clear();
        }

        if (_exposures.TryAdd(dedupeKey, 0))
        {
            _events.Enqueue(new TrackEvent(
                TrackEvent.Exposure, assignment.ExperimentKey, assignment.Variant, userKey, null, null, DateTimeOffset.UtcNow));
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        // Concurrent refresh requests (poll tick + stream reconnect) collapse into one download.
        if (!await _refreshLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            _refreshLock.Release();
            return;
        }

        // Stream events applied after this point are newer than the snapshot we are about to request.
        var since = _store.Mark();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.Endpoint("api/sdk/config"));
            request.Headers.Authorization = new("Bearer", _options.ApiKey);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var config = await response.Content
                .ReadFromJsonAsync<SdkConfig>(SdkJson.Options, cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidOperationException("Empty configuration response.");

            var wasReady = _store.IsInitialized;
            var changed = _store.ReplaceAll(config, since);
            if (wasReady)
            {
                foreach (var key in changed)
                {
                    RaiseFlagChanged(key);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "FeatureFlare failed to download configuration");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                // While streaming is healthy it delivers every change; polling covers outages and staleness.
                if (!_streamConnected || _store.IsStale(_options.CacheTtl))
                {
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task StreamLoopAsync(CancellationToken cancellationToken)
    {
        var listener = new SseListener(
            _streamHttp,
            _options.Endpoint("api/stream"),
            _options.ApiKey,
            _options.StreamInitialReconnectDelay,
            _options.StreamMaxReconnectDelay,
            _logger);

        listener.Connected += async () =>
        {
            _streamConnected = true;
            // Changes may have been missed while disconnected: re-sync the full snapshot.
            if (_store.IsInitialized)
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
        };
        listener.Disconnected += _ => _streamConnected = false;
        listener.MessageReceived += message =>
        {
            Apply(message);
            return Task.CompletedTask;
        };

        await listener.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    internal void Apply(SseMessage message)
    {
        try
        {
            switch (message.Event)
            {
                case "flag.updated" when Deserialize<FlagConfig>(message) is { } flag:
                    if (_store.Upsert(flag)) RaiseFlagChanged(flag.Key);
                    break;
                case "flag.deleted" when Deserialize<Deletion>(message) is { } deletion:
                    if (_store.DeleteFlag(deletion)) RaiseFlagChanged(deletion.Key);
                    break;
                case "segment.updated" when Deserialize<SegmentConfig>(message) is { } segment:
                    if (_store.Upsert(segment)) RaiseFlagsUsingSegment(segment.Key);
                    break;
                case "segment.deleted" when Deserialize<Deletion>(message) is { } deletion:
                    var affected = FlagsUsingSegment(deletion.Key);
                    if (_store.DeleteSegment(deletion))
                    {
                        foreach (var key in affected) RaiseFlagChanged(key);
                    }

                    break;
                case "experiment.updated" when Deserialize<ExperimentConfig>(message) is { } experiment:
                    if (_store.Upsert(experiment)) ExperimentChanged?.Invoke(experiment.Key, experiment);
                    break;
                case "experiment.deleted" when Deserialize<Deletion>(message) is { } deletion:
                    if (_store.DeleteExperiment(deletion)) ExperimentChanged?.Invoke(deletion.Key, null);
                    break;
                default:
                    _logger.LogDebug("Ignoring stream event {Event}", message.Event);
                    break;
            }
        }
        catch (Exception ex)
        {
            // A misbehaving event handler or malformed payload must not kill the stream.
            _logger.LogWarning(ex, "Failed to apply stream event {Event}", message.Event);
        }
    }

    private static T? Deserialize<T>(SseMessage message) => JsonSerializer.Deserialize<T>(message.Data, SdkJson.Options);

    private void RaiseFlagChanged(string key) =>
        FlagChanged?.Invoke(key, _store.Current.Flags.GetValueOrDefault(key));

    private void RaiseFlagsUsingSegment(string segmentKey)
    {
        foreach (var key in FlagsUsingSegment(segmentKey))
        {
            RaiseFlagChanged(key);
        }
    }

    private List<string> FlagsUsingSegment(string segmentKey) =>
        _store.Current.Flags.Values
            .Where(f => f.Rules.Any(r => r.Clauses.Any(c => c.Operator == ClauseOperator.InSegment && c.Values.Contains(segmentKey))))
            .Select(f => f.Key)
            .ToList();

    private static HttpClient CreateStandalone(
        Action<FeatureFlareOptions> configure,
        out FeatureFlareOptions options,
        out HttpClient streamClient)
    {
        options = new FeatureFlareOptions();
        configure(options);
        options.Validate();

        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(250),
            })
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();

        var http = new HttpClient(new ResilienceHandler(pipeline)
        {
            InnerHandler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) },
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        streamClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        return http;
    }
}
