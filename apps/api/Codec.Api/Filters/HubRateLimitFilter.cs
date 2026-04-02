using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Codec.Api.Filters;

/// <summary>
/// SignalR hub filter that enforces per-connection rate limits using a sliding window.
/// </summary>
public sealed class HubRateLimitFilter : IHubFilter
{
    private const int DefaultLimitPerMinute = 60;

    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var connectionId = invocationContext.Context.ConnectionId;
        var window = _windows.GetOrAdd(connectionId, _ => new SlidingWindow());

        if (!window.TryAcquire(DefaultLimitPerMinute))
            throw new HubException("Rate limit exceeded.");

        return await next(invocationContext);
    }

    public async Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        _windows.TryRemove(context.Context.ConnectionId, out _);
        await next(context, exception);
    }

    internal sealed class SlidingWindow
    {
        private readonly object _lock = new();
        private readonly Queue<DateTimeOffset> _timestamps = new();

        public bool TryAcquire(int maxPerMinute)
        {
            var now = DateTimeOffset.UtcNow;
            var cutoff = now.AddMinutes(-1);

            lock (_lock)
            {
                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                    _timestamps.Dequeue();

                if (_timestamps.Count >= maxPerMinute)
                    return false;

                _timestamps.Enqueue(now);
                return true;
            }
        }
    }
}
