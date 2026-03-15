using Codec.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace Codec.Api.Tests.Services;

public class MessageCacheServiceTests
{
    private readonly Mock<ILogger<MessageCacheService>> _logger = new();
    private readonly Mock<IDistributedCache> _cache = new();
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _redisDb = new();

    public MessageCacheServiceTests()
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_redisDb.Object);
    }

    // --- No cache (null) ---

    [Fact]
    public async Task GetMessagesAsync_NullCache_ReturnsNull()
    {
        var svc = new MessageCacheService(_logger.Object);
        var result = await svc.GetMessagesAsync(Guid.NewGuid(), null, 50);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetMessagesAsync_NullCache_DoesNotThrow()
    {
        var svc = new MessageCacheService(_logger.Object);
        await svc.Invoking(s => s.SetMessagesAsync(Guid.NewGuid(), null, 50, "{}"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvalidateChannelAsync_NullCacheAndRedis_DoesNotThrow()
    {
        var svc = new MessageCacheService(_logger.Object);
        await svc.Invoking(s => s.InvalidateChannelAsync(Guid.NewGuid()))
            .Should().NotThrowAsync();
    }

    // --- With cache ---

    [Fact]
    public async Task GetMessagesAsync_CacheHit_ReturnsCachedValue()
    {
        var channelId = Guid.NewGuid();
        var key = $"channel:{channelId}:messages:latest:50";

        _cache.Setup(c => c.GetAsync(key, default)).ReturnsAsync(System.Text.Encoding.UTF8.GetBytes("cached-json"));

        var svc = new MessageCacheService(_logger.Object, _cache.Object);
        var result = await svc.GetMessagesAsync(channelId, null, 50);
        result.Should().Be("cached-json");
    }

    [Fact]
    public async Task GetMessagesAsync_CacheMiss_ReturnsNull()
    {
        var channelId = Guid.NewGuid();
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), default)).ReturnsAsync((byte[]?)null);

        var svc = new MessageCacheService(_logger.Object, _cache.Object);
        var result = await svc.GetMessagesAsync(channelId, null, 50);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetMessagesAsync_CacheException_ReturnsNull()
    {
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), default)).ThrowsAsync(new RedisException("conn failed"));

        var svc = new MessageCacheService(_logger.Object, _cache.Object);
        var result = await svc.GetMessagesAsync(Guid.NewGuid(), null, 50);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetMessagesAsync_WritesCacheAndTracksKey()
    {
        var channelId = Guid.NewGuid();

        _redisDb.Setup(d => d.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _redisDb.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var svc = new MessageCacheService(_logger.Object, _cache.Object, _redis.Object);
        await svc.SetMessagesAsync(channelId, null, 50, "{\"messages\":[]}");

        _cache.Verify(c => c.SetAsync(
            It.Is<string>(k => k.Contains(channelId.ToString())),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            default), Times.Once);
    }

    [Fact]
    public async Task InvalidateChannelAsync_DeletesTrackedKeys()
    {
        var channelId = Guid.NewGuid();
        var trackingKey = $"channel:{channelId}:message-keys";
        var cachedKeys = new RedisValue[] { "key1", "key2" };

        _redisDb.Setup(d => d.SetMembersAsync(trackingKey, It.IsAny<CommandFlags>())).ReturnsAsync(cachedKeys);
        _redisDb.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>())).ReturnsAsync(3);

        var svc = new MessageCacheService(_logger.Object, _cache.Object, _redis.Object);
        await svc.InvalidateChannelAsync(channelId);

        _redisDb.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey[]>(keys => keys.Length == 3), // 2 cached + 1 tracking key
            It.IsAny<CommandFlags>()), Times.Once);
    }
}
