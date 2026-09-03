using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace FeatureFlare.Sdk.Streaming;

/// <summary>
/// Keeps an SSE connection open, reconnecting with exponential backoff and full jitter.
/// Uses a dedicated <see cref="HttpClient"/> without timeouts or retry handlers: a resilience pipeline
/// with a request timeout would otherwise kill the long-lived stream every few seconds.
/// </summary>
internal sealed class SseListener(
    HttpClient httpClient,
    Uri url,
    string apiKey,
    TimeSpan initialDelay,
    TimeSpan maxDelay,
    ILogger logger)
{
    public event Func<SseMessage, Task>? MessageReceived;

    public event Func<Task>? Connected;

    public event Action<Exception?>? Disconnected;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        int? serverRetryMs = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            Exception? failure = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

                using var response = await httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                attempt = 0;
                if (Connected is { } connected)
                {
                    await connected().ConfigureAwait(false);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var parser = new SseParser();

                while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    if (parser.ProcessLine(line) is { } message && MessageReceived is { } handler)
                    {
                        await handler(message).ConfigureAwait(false);
                    }

                    serverRetryMs = parser.RetryMilliseconds ?? serverRetryMs;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                failure = ex;
                logger.LogWarning(ex, "FeatureFlare stream connection failed");
            }

            Disconnected?.Invoke(failure);

            var delay = NextDelay(attempt++, serverRetryMs);
            logger.LogDebug("Reconnecting FeatureFlare stream in {Delay}", delay);
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal TimeSpan NextDelay(int attempt, int? serverRetryMs)
    {
        var baseMs = Math.Max(initialDelay.TotalMilliseconds, serverRetryMs ?? 0);
        var capMs = Math.Min(maxDelay.TotalMilliseconds, baseMs * Math.Pow(2, Math.Min(attempt, 16)));
        var jittered = (baseMs / 2) + (Random.Shared.NextDouble() * Math.Max(0, capMs - (baseMs / 2)));
        return TimeSpan.FromMilliseconds(jittered);
    }
}
