using System.Net.Http.Json;
using System.Threading.Channels;
using FeatureFlare.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace FeatureFlare.Sdk.Events;

/// <summary>
/// Buffers exposure/conversion events and posts them in batches. Enqueueing is non-blocking and the buffer
/// is bounded (oldest events are dropped), so tracking can never slow down or exhaust the host process.
/// </summary>
internal sealed class EventProcessor : IAsyncDisposable
{
    private readonly Channel<TrackEvent> _channel;
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly int _batchSize;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private long _dropped;

    public EventProcessor(HttpClient httpClient, Uri endpoint, string apiKey, int capacity, int batchSize, ILogger logger)
    {
        _httpClient = httpClient;
        _endpoint = endpoint;
        _apiKey = apiKey;
        _batchSize = batchSize;
        _logger = logger;
        _channel = Channel.CreateBounded<TrackEvent>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            },
            _ => Interlocked.Increment(ref _dropped));
    }

    public int Pending => _channel.Reader.Count;

    public void Enqueue(TrackEvent trackEvent) => _channel.Writer.TryWrite(trackEvent);

    public async Task RunAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Final flush happens in DisposeAsync.
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var batch = new List<TrackEvent>(_batchSize);
            while (_channel.Reader.TryRead(out var item))
            {
                batch.Add(item);
                if (batch.Count == _batchSize)
                {
                    await SendAsync(batch, cancellationToken).ConfigureAwait(false);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await SendAsync(batch, cancellationToken).ConfigureAwait(false);
            }

            var dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
            {
                _logger.LogWarning("FeatureFlare dropped {Count} events because the queue was full", dropped);
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await FlushAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("FeatureFlare could not flush {Count} pending events before shutdown", Pending);
        }

        _flushLock.Dispose();
    }

    private async Task SendAsync(List<TrackEvent> batch, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(new { events = batch }, options: SdkJson.Options),
            };
            request.Headers.Authorization = new("Bearer", _apiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Transient failures were already retried by the resilience pipeline; analytics data is
            // best-effort, so drop the batch rather than growing memory without bound.
            _logger.LogWarning(ex, "FeatureFlare failed to send {Count} events", batch.Count);
        }
    }
}
