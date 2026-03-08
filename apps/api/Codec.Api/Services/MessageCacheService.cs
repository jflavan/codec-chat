using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace Codec.Api.Services;

public class MessageCacheService
{
    private readonly IDistributedCache? _cache;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<MessageCacheService> _logger;

    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MessageCacheService(ILogger<MessageCacheService> logger, IDistributedCache? cache = null, IConnectionMultiplexer? redis = null)
    {
        _cache = cache;
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Try to get cached message response for a channel page.
    /// Returns null on cache miss or if caching is unavailable.
    /// </summary>
    public async Task<string?> GetMessagesAsync(Guid channelId, DateTimeOffset? before, int limit)
    {
        if (_cache is null) return null;

        var key = BuildKey(channelId, before, limit);
        try
        {
            var cached = await _cache.GetStringAsync(key);
            if (cached is not null)
            {
                _logger.LogDebug("Cache hit for channel {ChannelId} (before={Before}, limit={Limit})", channelId, before, limit);
            }
            else
            {
                _logger.LogDebug("Cache miss for channel {ChannelId} (before={Before}, limit={Limit})", channelId, before, limit);
            }
            return cached;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis read failed for channel {ChannelId}", channelId);
            return null;
        }
    }

    /// <summary>
    /// Cache a serialized message response for a channel page.
    /// </summary>
    public async Task SetMessagesAsync(Guid channelId, DateTimeOffset? before, int limit, string jsonResponse)
    {
        if (_cache is null) return;

        var key = BuildKey(channelId, before, limit);
        var trackingKey = TrackingKey(channelId);

        try
        {
            await _cache.SetStringAsync(key, jsonResponse, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = DefaultExpiry
            });

            // Track this key in a Redis set for bulk invalidation.
            if (_redis is not null)
            {
                var db = _redis.GetDatabase();
                await db.SetAddAsync(trackingKey, key);
                await db.KeyExpireAsync(trackingKey, DefaultExpiry);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis write failed for channel {ChannelId}", channelId);
        }
    }

    /// <summary>
    /// Invalidate all cached message pages for a channel.
    /// </summary>
    public async Task InvalidateChannelAsync(Guid channelId)
    {
        if (_redis is null && _cache is null) return;

        var trackingKey = TrackingKey(channelId);

        try
        {
            if (_redis is not null)
            {
                var db = _redis.GetDatabase();
                var keys = await db.SetMembersAsync(trackingKey);

                if (keys.Length > 0)
                {
                    var redisKeys = keys.Select(k => (RedisKey)k.ToString()).Append((RedisKey)trackingKey).ToArray();
                    await db.KeyDeleteAsync(redisKeys);
                    _logger.LogInformation("Invalidated {Count} cached pages for channel {ChannelId}", keys.Length, channelId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis invalidation failed for channel {ChannelId}", channelId);
        }
    }

    private static string BuildKey(Guid channelId, DateTimeOffset? before, int limit)
    {
        var beforeStr = before?.ToUnixTimeMilliseconds().ToString() ?? "latest";
        return $"channel:{channelId}:messages:{beforeStr}:{limit}";
    }

    private static string TrackingKey(Guid channelId) => $"channel:{channelId}:message-keys";
}
