using System.Security.Claims;
using Codec.Api.Controllers;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class PushSubscriptionsControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly PushSubscriptionsController _controller;
    private readonly User _testUser;

    public PushSubscriptionsControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = "google-test",
            DisplayName = "Test User",
            Email = "test@test.com",
            AvatarUrl = "https://example.com/pic.jpg"
        };

        _controller = new PushSubscriptionsController(_db, _userService.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", "google-test"),
                    new Claim("name", "Test User"),
                    new Claim("email", "test@test.com")
                ], "Bearer"))
            }
        };

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_testUser, false));
    }

    // --- Subscribe ---

    [Fact]
    public async Task Subscribe_NewEndpoint_CreatesSubscription()
    {
        var request = new CreatePushSubscriptionRequest
        {
            Endpoint = "https://push.example.com/sub1",
            P256dh = "p256dh-key",
            Auth = "auth-key"
        };

        var result = await _controller.Subscribe(request);

        result.Should().BeOfType<OkResult>();
        var sub = await _db.PushSubscriptions.SingleAsync();
        sub.UserId.Should().Be(_testUser.Id);
        sub.Endpoint.Should().Be(request.Endpoint);
        sub.P256dh.Should().Be(request.P256dh);
        sub.Auth.Should().Be(request.Auth);
        sub.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Subscribe_ExistingEndpoint_UpdatesAndReactivates()
    {
        // Pre-existing deactivated subscription
        var existing = new Models.PushSubscription
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            Endpoint = "https://push.example.com/sub1",
            P256dh = "old-p256dh",
            Auth = "old-auth",
            IsActive = false
        };
        _db.PushSubscriptions.Add(existing);
        await _db.SaveChangesAsync();

        var request = new CreatePushSubscriptionRequest
        {
            Endpoint = "https://push.example.com/sub1",
            P256dh = "new-p256dh",
            Auth = "new-auth"
        };

        var result = await _controller.Subscribe(request);

        result.Should().BeOfType<OkResult>();
        var subs = await _db.PushSubscriptions.ToListAsync();
        subs.Should().HaveCount(1);
        subs[0].P256dh.Should().Be("new-p256dh");
        subs[0].Auth.Should().Be("new-auth");
        subs[0].IsActive.Should().BeTrue();
        subs[0].UserId.Should().Be(_testUser.Id);
    }

    [Fact]
    public async Task Subscribe_ExistingEndpointDifferentUser_ReassignsToCurrentUser()
    {
        var otherUserId = Guid.NewGuid();
        _db.PushSubscriptions.Add(new Models.PushSubscription
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Endpoint = "https://push.example.com/sub1",
            P256dh = "old-key",
            Auth = "old-auth"
        });
        await _db.SaveChangesAsync();

        var request = new CreatePushSubscriptionRequest
        {
            Endpoint = "https://push.example.com/sub1",
            P256dh = "new-key",
            Auth = "new-auth"
        };

        var result = await _controller.Subscribe(request);

        result.Should().BeOfType<OkResult>();
        var sub = await _db.PushSubscriptions.SingleAsync();
        sub.UserId.Should().Be(_testUser.Id);
    }

    // --- Unsubscribe ---

    [Fact]
    public async Task Unsubscribe_ExistingSubscription_ReturnsNoContent()
    {
        _db.PushSubscriptions.Add(new Models.PushSubscription
        {
            Id = Guid.NewGuid(),
            UserId = _testUser.Id,
            Endpoint = "https://push.example.com/sub1",
            P256dh = "key",
            Auth = "auth"
        });
        await _db.SaveChangesAsync();

        var result = await _controller.Unsubscribe(new UnsubscribeRequest
        {
            Endpoint = "https://push.example.com/sub1"
        });

        result.Should().BeOfType<NoContentResult>();
        (await _db.PushSubscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Unsubscribe_NonExistentEndpoint_ReturnsNotFound()
    {
        var result = await _controller.Unsubscribe(new UnsubscribeRequest
        {
            Endpoint = "https://push.example.com/nonexistent"
        });

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Unsubscribe_OtherUsersSubscription_ReturnsNotFound()
    {
        var otherUserId = Guid.NewGuid();
        _db.PushSubscriptions.Add(new Models.PushSubscription
        {
            Id = Guid.NewGuid(),
            UserId = otherUserId,
            Endpoint = "https://push.example.com/sub1",
            P256dh = "key",
            Auth = "auth"
        });
        await _db.SaveChangesAsync();

        var result = await _controller.Unsubscribe(new UnsubscribeRequest
        {
            Endpoint = "https://push.example.com/sub1"
        });

        result.Should().BeOfType<NotFoundResult>();
        // Other user's subscription should still exist
        (await _db.PushSubscriptions.CountAsync()).Should().Be(1);
    }

    // --- GetVapidKey ---

    [Fact]
    public void GetVapidKey_Configured_ReturnsPublicKey()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vapid:PublicKey"] = "BEl62iUYgUivxIkv69yViEuiBIa-Ib9-SkvMeAtA3LFgDzkOs-qy19yRGiD7lA2Cz1vLYPSKELB_xyFd_jy0vQ"
            })
            .Build();

        var result = _controller.GetVapidKey(config);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void GetVapidKey_NotConfigured_ReturnsNotFound()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var result = _controller.GetVapidKey(config);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void GetVapidKey_EmptyKey_ReturnsNotFound()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vapid:PublicKey"] = ""
            })
            .Build();

        var result = _controller.GetVapidKey(config);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    public void Dispose() => _db.Dispose();
}
