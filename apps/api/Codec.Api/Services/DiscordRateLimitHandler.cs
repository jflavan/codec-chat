using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.RateLimiting;

namespace Codec.Api.Services;

/// <summary>
/// DelegatingHandler that respects Discord's rate limit system:
/// - Global rate limit: 50 requests/second across all endpoints
/// - Per-route limits: tracked via X-RateLimit-Bucket, proactively delays when remaining hits 0
/// - 429 responses: retries after Retry-After delay (up to 5 retries)
/// </summary>
public class DiscordRateLimitHandler : DelegatingHandler
{
    private const int MaxRetries = 5;

    // Global: 50 req/sec as per Discord docs
    private static readonly TokenBucketRateLimiter _globalLimiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 50,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        TokensPerPeriod = 50,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 100
    });

    // Per-bucket: tracks when each bucket's limit resets
    private static readonly ConcurrentDictionary<string, DateTimeOffset> _bucketResetTimes = new();
    private static long _requestCount;
    private const int EvictionInterval = 100;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = null!;

        // Periodically evict expired entries to prevent unbounded growth
        if (Interlocked.Increment(ref _requestCount) % EvictionInterval == 0)
            EvictExpiredEntries();

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            // Wait for per-bucket reset if this bucket was exhausted
            var bucketKey = $"{request.Method}:{request.RequestUri?.AbsolutePath}";
            if (_bucketResetTimes.TryGetValue(bucketKey, out var resetTime))
            {
                var delay = resetTime - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
            }

            // Wait for global rate limit token, retrying until a lease is acquired
            RateLimitLease lease;
            while (true)
            {
                lease = await _globalLimiter.AcquireAsync(1, cancellationToken);
                if (lease.IsAcquired)
                    break;
                lease.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }

            try
            {
                // Clone the request so retries work (content stream is consumed after first send)
                using var clonedRequest = await CloneRequestAsync(request, cancellationToken);
                response = await base.SendAsync(clonedRequest, cancellationToken);
            }
            finally
            {
                lease.Dispose();
            }

            // Track per-bucket limits from response headers
            TrackBucketLimits(response);

            if ((int)response.StatusCode != 429)
                return response;

            if (attempt == MaxRetries)
                break;

            var retryAfter = ParseRetryAfter(response);
            response.Dispose();
            if (retryAfter > TimeSpan.Zero)
                await Task.Delay(retryAfter, cancellationToken);
        }

        return response;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is not null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync(ct);
            var newContent = new ByteArrayContent(contentBytes);
            foreach (var header in request.Content.Headers)
                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            clone.Content = newContent;
        }

        return clone;
    }

    private static void TrackBucketLimits(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-RateLimit-Bucket", out var bucketValues))
            return;

        var bucket = bucketValues.FirstOrDefault();
        if (bucket is null) return;

        // If remaining is 0, record when this bucket resets
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues) &&
            remainingValues.FirstOrDefault() == "0" &&
            response.Headers.TryGetValues("X-RateLimit-Reset-After", out var resetValues))
        {
            var resetStr = resetValues.FirstOrDefault();
            if (resetStr is not null &&
                double.TryParse(resetStr, CultureInfo.InvariantCulture, out var resetAfterSecs))
            {
                // Key by request path so SendAsync can look it up before the next call
                if (response.RequestMessage?.RequestUri?.AbsolutePath is { } path)
                {
                    var key = $"{response.RequestMessage.Method}:{path}";
                    _bucketResetTimes[key] = DateTimeOffset.UtcNow.AddSeconds(resetAfterSecs);
                }
            }
        }
    }

    private static void EvictExpiredEntries()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _bucketResetTimes)
        {
            if (kvp.Value < now)
                _bucketResetTimes.TryRemove(kvp);
        }
    }

    private static TimeSpan ParseRetryAfter(HttpResponseMessage response)
    {
        // Prefer the JSON body's retry_after for precision, fall back to header
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var value = values.FirstOrDefault();
            if (value is not null &&
                double.TryParse(value, CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
        return TimeSpan.FromSeconds(1);
    }
}
