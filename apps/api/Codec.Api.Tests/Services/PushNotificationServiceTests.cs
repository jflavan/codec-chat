using System.Net;
using Codec.Api.Data;
using Codec.Api.Services;
using FluentAssertions;
using Lib.Net.Http.WebPush;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Codec.Api.Tests.Services;

public class PushNotificationServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IPushClient> _pushClient = new();
    private readonly PushNotificationService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public PushNotificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        var services = new ServiceCollection();
        services.AddScoped(_ => new CodecDbContext(options));
        var provider = services.BuildServiceProvider();

        _service = new PushNotificationService(
            _pushClient.Object,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PushNotificationService>.Instance);
    }

    private Models.PushSubscription CreateSubscription(bool isActive = true)
    {
        var sub = new Models.PushSubscription
        {
            Id = Guid.NewGuid(),
            UserId = _userId,
            Endpoint = $"https://push.example.com/{Guid.NewGuid()}",
            P256dh = "test-p256dh-key",
            Auth = "test-auth-key",
            IsActive = isActive
        };
        _db.PushSubscriptions.Add(sub);
        _db.SaveChanges();
        return sub;
    }

    private static PushPayload TestPayload(string type = "message") => new()
    {
        Type = type,
        Title = "Test",
        Body = "Test body"
    };

    // --- Subscription lifecycle ---

    [Fact]
    public async Task SendToUserAsync_NoSubscriptions_DoesNothing()
    {
        await _service.SendToUserAsync(_userId, TestPayload());

        _pushClient.Verify(
            c => c.RequestPushMessageDeliveryAsync(
                It.IsAny<PushSubscription>(), It.IsAny<PushMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendToUserAsync_InactiveSubscription_IsSkipped()
    {
        CreateSubscription(isActive: false);

        await _service.SendToUserAsync(_userId, TestPayload());

        _pushClient.Verify(
            c => c.RequestPushMessageDeliveryAsync(
                It.IsAny<PushSubscription>(), It.IsAny<PushMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendToUserAsync_ActiveSubscription_SendsPush()
    {
        CreateSubscription();

        await _service.SendToUserAsync(_userId, TestPayload());

        _pushClient.Verify(
            c => c.RequestPushMessageDeliveryAsync(
                It.IsAny<PushSubscription>(), It.IsAny<PushMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendToUserAsync_MultipleActiveSubscriptions_SendsToAll()
    {
        CreateSubscription();
        CreateSubscription();
        CreateSubscription();

        await _service.SendToUserAsync(_userId, TestPayload());

        _pushClient.Verify(
            c => c.RequestPushMessageDeliveryAsync(
                It.IsAny<PushSubscription>(), It.IsAny<PushMessage>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task SendToUserAsync_DoesNotSendToOtherUsersSubscriptions()
    {
        var otherUserId = Guid.NewGuid();
        _db.PushSubscriptions.Add(new Models.PushSubscription
        {
            Id = Guid.NewGuid(), UserId = otherUserId,
            Endpoint = "https://push.example.com/other",
            P256dh = "key", Auth = "auth"
        });
        await _db.SaveChangesAsync();

        await _service.SendToUserAsync(_userId, TestPayload());

        _pushClient.Verify(
            c => c.RequestPushMessageDeliveryAsync(
                It.IsAny<PushSubscription>(), It.IsAny<PushMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // --- Deactivation on 404/410 ---

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task SendToUserAsync_PushReturns404Or410_DeactivatesSubscription(HttpStatusCode statusCode)
    {
        var sub = CreateSubscription();

        _pushClient.Setup(c => c.RequestPushMessageDeliveryAsync(
                It.IsAny<PushSubscription>(), It.IsAny<PushMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PushServiceClientException("expired", statusCode));

        await _service.SendToUserAsync(_userId, TestPayload());

        _db.ChangeTracker.Clear();
        var updated = await _db.PushSubscriptions.FindAsync(sub.Id);
        updated!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SendToUserAsync_PushThrowsOtherException_SubscriptionRemainsActive()
    {
        var sub = CreateSubscription();

        _pushClient.Setup(c => c.RequestPushMessageDeliveryAsync(
                It.IsAny<PushSubscription>(), It.IsAny<PushMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("network error"));

        await _service.SendToUserAsync(_userId, TestPayload());

        _db.ChangeTracker.Clear();
        var updated = await _db.PushSubscriptions.FindAsync(sub.Id);
        updated!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SendToUserAsync_MixedResults_OnlyDeactivatesFailedOnes()
    {
        var goodSub = CreateSubscription();
        var badSub = CreateSubscription();

        _pushClient.Setup(c => c.RequestPushMessageDeliveryAsync(
                It.Is<PushSubscription>(s => s.Endpoint == badSub.Endpoint),
                It.IsAny<PushMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PushServiceClientException("gone", HttpStatusCode.Gone));

        await _service.SendToUserAsync(_userId, TestPayload());

        _db.ChangeTracker.Clear();
        (await _db.PushSubscriptions.FindAsync(goodSub.Id))!.IsActive.Should().BeTrue();
        (await _db.PushSubscriptions.FindAsync(badSub.Id))!.IsActive.Should().BeFalse();
    }

    // --- Payload urgency routing ---

    [Theory]
    [InlineData("dm", PushMessageUrgency.High)]
    [InlineData("mention", PushMessageUrgency.High)]
    [InlineData("friend_request", PushMessageUrgency.High)]
    [InlineData("call", PushMessageUrgency.High)]
    [InlineData("message", PushMessageUrgency.Normal)]
    [InlineData("server_update", PushMessageUrgency.Normal)]
    public async Task SendToUserAsync_SetsCorrectUrgency(string type, PushMessageUrgency expectedUrgency)
    {
        CreateSubscription();
        PushMessage? capturedMessage = null;

        _pushClient.Setup(c => c.RequestPushMessageDeliveryAsync(
                It.IsAny<PushSubscription>(), It.IsAny<PushMessage>(), It.IsAny<CancellationToken>()))
            .Callback<PushSubscription, PushMessage, CancellationToken>((_, msg, _) => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        await _service.SendToUserAsync(_userId, TestPayload(type));

        capturedMessage.Should().NotBeNull();
        capturedMessage!.Urgency.Should().Be(expectedUrgency);
    }

    // --- Multi-user dispatch ---

    [Fact]
    public async Task SendToUsersAsync_SendsToEachUser()
    {
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        _db.PushSubscriptions.Add(new Models.PushSubscription
        {
            Id = Guid.NewGuid(), UserId = user1,
            Endpoint = "https://push.example.com/u1",
            P256dh = "key1", Auth = "auth1"
        });
        _db.PushSubscriptions.Add(new Models.PushSubscription
        {
            Id = Guid.NewGuid(), UserId = user2,
            Endpoint = "https://push.example.com/u2",
            P256dh = "key2", Auth = "auth2"
        });
        await _db.SaveChangesAsync();

        await _service.SendToUsersAsync([user1, user2], TestPayload());

        _pushClient.Verify(
            c => c.RequestPushMessageDeliveryAsync(
                It.IsAny<PushSubscription>(), It.IsAny<PushMessage>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SendToUsersAsync_EmptyList_DoesNothing()
    {
        await _service.SendToUsersAsync([], TestPayload());

        _pushClient.Verify(
            c => c.RequestPushMessageDeliveryAsync(
                It.IsAny<PushSubscription>(), It.IsAny<PushMessage>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    public void Dispose() => _db.Dispose();
}
