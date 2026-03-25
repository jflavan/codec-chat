using System.Security.Claims;
using Codec.Api.Controllers;
using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class ServersControllerWebhookTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly ServersController _controller;
    private readonly User _testUser;
    private readonly Server _testServer;
    private readonly AuditService _auditService;

    public ServersControllerWebhookTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Test User" };
        _testServer = new Server { Id = Guid.NewGuid(), Name = "Test Server" };
        _db.Users.Add(_testUser);
        _db.Servers.Add(_testServer);
        _db.SaveChanges();

        _auditService = new AuditService(_db);
        var messageCache = new MessageCacheService(new Mock<ILogger<MessageCacheService>>().Object);

        var hub = new Mock<IHubContext<ChatHub>>();
        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);
        clients.Setup(c => c.All).Returns(clientProxy.Object);

        var webhookService = new WebhookService(
            new Mock<IServiceScopeFactory>().Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<ILogger<WebhookService>>().Object);

        _controller = new ServersController(
            _db, _userService.Object,
            new Mock<IAvatarService>().Object,
            new Mock<ICustomEmojiService>().Object,
            hub.Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<IConfiguration>().Object,
            messageCache,
            webhookService);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", "g-1"), new Claim("name", "Test User")
                ], "Bearer")),
                RequestServices = BuildServiceProvider()
            }
        };

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_testUser, false));
        _userService.Setup(u => u.EnsureAdminAsync(_testServer.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = _testServer.Id, UserId = _testUser.Id });
        _userService.Setup(u => u.GetEffectiveDisplayName(It.IsAny<User>())).Returns("Test User");
    }

    public void Dispose() => _db.Dispose();

    private IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_auditService);
        return services.BuildServiceProvider();
    }

    private Webhook SeedWebhook(
        string name = "Test Hook",
        string url = "https://example.com/hook",
        string eventTypes = "MessageCreated",
        string? secret = null,
        bool isActive = true)
    {
        var webhook = new Webhook
        {
            Id = Guid.NewGuid(),
            ServerId = _testServer.Id,
            Name = name,
            Url = url,
            Secret = secret,
            EventTypes = eventTypes,
            IsActive = isActive,
            CreatedByUserId = _testUser.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Webhooks.Add(webhook);
        _db.SaveChanges();
        return webhook;
    }

    // --- CreateWebhook ---

    [Fact]
    public async Task CreateWebhook_ReturnsCreated_WithValidRequest()
    {
        var request = new CreateWebhookRequest
        {
            Name = "My Hook",
            Url = "https://example.com/webhook",
            EventTypes = ["MessageCreated", "MemberJoined"],
            Secret = "secret123"
        };

        var result = await _controller.CreateWebhook(_testServer.Id, request, _auditService);

        result.Should().BeOfType<CreatedResult>();
        var created = (CreatedResult)result;
        var value = created.Value;
        value.Should().NotBeNull();

        // Verify webhook was saved
        var webhook = await _db.Webhooks.FirstOrDefaultAsync(w => w.ServerId == _testServer.Id);
        webhook.Should().NotBeNull();
        webhook!.Name.Should().Be("My Hook");
        webhook.Url.Should().Be("https://example.com/webhook");
        webhook.EventTypes.Should().Be("MessageCreated,MemberJoined");
        webhook.Secret.Should().Be("secret123");
        webhook.IsActive.Should().BeTrue();
        webhook.CreatedByUserId.Should().Be(_testUser.Id);
    }

    [Fact]
    public async Task CreateWebhook_ReturnsBadRequest_ForInvalidEventTypes()
    {
        var request = new CreateWebhookRequest
        {
            Name = "Bad Hook",
            Url = "https://example.com/webhook",
            EventTypes = ["MessageCreated", "InvalidEvent"]
        };

        var result = await _controller.CreateWebhook(_testServer.Id, request, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateWebhook_TrimsNameAndUrl()
    {
        var request = new CreateWebhookRequest
        {
            Name = "  Padded Name  ",
            Url = "  https://example.com/hook  ",
            EventTypes = ["MessageCreated"]
        };

        await _controller.CreateWebhook(_testServer.Id, request, _auditService);

        var webhook = await _db.Webhooks.FirstAsync(w => w.ServerId == _testServer.Id);
        webhook.Name.Should().Be("Padded Name");
        webhook.Url.Should().Be("https://example.com/hook");
    }

    [Fact]
    public async Task CreateWebhook_CreatesAuditLogEntry()
    {
        var request = new CreateWebhookRequest
        {
            Name = "Audited Hook",
            Url = "https://example.com/webhook",
            EventTypes = ["MessageCreated"]
        };

        await _controller.CreateWebhook(_testServer.Id, request, _auditService);

        var audit = await _db.AuditLogEntries.FirstOrDefaultAsync(
            a => a.ServerId == _testServer.Id && a.Action == AuditAction.WebhookCreated);
        audit.Should().NotBeNull();
        audit!.Details.Should().Be("Audited Hook");
    }

    [Fact]
    public async Task CreateWebhook_ResponseDoesNotExposeSecret()
    {
        var request = new CreateWebhookRequest
        {
            Name = "Secret Hook",
            Url = "https://example.com/webhook",
            EventTypes = ["MessageCreated"],
            Secret = "super-secret"
        };

        var result = await _controller.CreateWebhook(_testServer.Id, request, _auditService);

        var created = (CreatedResult)result;
        // The response should contain HasSecret=true but not the actual secret value
        var json = System.Text.Json.JsonSerializer.Serialize(created.Value);
        // Anonymous types use PascalCase by default
        json.Should().Contain("\"HasSecret\":true");
        json.Should().NotContain("super-secret");
    }

    // --- GetWebhooks ---

    [Fact]
    public async Task GetWebhooks_ReturnsOk_WithWebhookList()
    {
        SeedWebhook(name: "Hook A", eventTypes: "MessageCreated");
        SeedWebhook(name: "Hook B", eventTypes: "MemberJoined,ChannelCreated");

        var result = await _controller.GetWebhooks(_testServer.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetWebhooks_ReturnsEmptyList_WhenNoWebhooks()
    {
        var result = await _controller.GetWebhooks(_testServer.Id);

        result.Should().BeOfType<OkObjectResult>();
        var json = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)result).Value);
        json.Should().Be("[]");
    }

    // --- UpdateWebhook ---

    [Fact]
    public async Task UpdateWebhook_ReturnsOk_WhenValid()
    {
        var webhook = SeedWebhook();
        var request = new UpdateWebhookRequest
        {
            Name = "Updated Name",
            IsActive = false
        };

        var result = await _controller.UpdateWebhook(_testServer.Id, webhook.Id, request, _auditService);

        result.Should().BeOfType<OkObjectResult>();

        var updated = await _db.Webhooks.FindAsync(webhook.Id);
        updated!.Name.Should().Be("Updated Name");
        updated.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateWebhook_ReturnsNotFound_WhenWebhookMissing()
    {
        var request = new UpdateWebhookRequest { Name = "Ghost" };

        var result = await _controller.UpdateWebhook(_testServer.Id, Guid.NewGuid(), request, _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UpdateWebhook_ReturnsBadRequest_ForInvalidEventTypes()
    {
        var webhook = SeedWebhook();
        var request = new UpdateWebhookRequest
        {
            EventTypes = ["MessageCreated", "BogusEvent"]
        };

        var result = await _controller.UpdateWebhook(_testServer.Id, webhook.Id, request, _auditService);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateWebhook_UpdatesEventTypes()
    {
        var webhook = SeedWebhook(eventTypes: "MessageCreated");
        var request = new UpdateWebhookRequest
        {
            EventTypes = ["MemberJoined", "ChannelDeleted"]
        };

        await _controller.UpdateWebhook(_testServer.Id, webhook.Id, request, _auditService);

        var updated = await _db.Webhooks.FindAsync(webhook.Id);
        updated!.EventTypes.Should().Be("MemberJoined,ChannelDeleted");
    }

    [Fact]
    public async Task UpdateWebhook_PartialUpdate_OnlyChangesProvidedFields()
    {
        var webhook = SeedWebhook(name: "Original", url: "https://original.com/hook", eventTypes: "MessageCreated");
        var request = new UpdateWebhookRequest { Name = "Changed" };

        await _controller.UpdateWebhook(_testServer.Id, webhook.Id, request, _auditService);

        var updated = await _db.Webhooks.FindAsync(webhook.Id);
        updated!.Name.Should().Be("Changed");
        updated.Url.Should().Be("https://original.com/hook");
        updated.EventTypes.Should().Be("MessageCreated");
    }

    [Fact]
    public async Task UpdateWebhook_CreatesAuditLogEntry()
    {
        var webhook = SeedWebhook(name: "Before");
        var request = new UpdateWebhookRequest { Name = "After" };

        await _controller.UpdateWebhook(_testServer.Id, webhook.Id, request, _auditService);

        var audit = await _db.AuditLogEntries.FirstOrDefaultAsync(
            a => a.ServerId == _testServer.Id && a.Action == AuditAction.WebhookUpdated);
        audit.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateWebhook_ReturnsNotFound_WhenWebhookBelongsToDifferentServer()
    {
        var otherServer = new Server { Id = Guid.NewGuid(), Name = "Other" };
        _db.Servers.Add(otherServer);
        var webhook = new Webhook
        {
            Id = Guid.NewGuid(),
            ServerId = otherServer.Id,
            Name = "Other Hook",
            Url = "https://other.com/hook",
            EventTypes = "MessageCreated",
            CreatedByUserId = _testUser.Id
        };
        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync();

        var request = new UpdateWebhookRequest { Name = "Hacked" };
        var result = await _controller.UpdateWebhook(_testServer.Id, webhook.Id, request, _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // --- DeleteWebhook ---

    [Fact]
    public async Task DeleteWebhook_ReturnsNoContent_WhenExists()
    {
        var webhook = SeedWebhook();

        var result = await _controller.DeleteWebhook(_testServer.Id, webhook.Id, _auditService);

        result.Should().BeOfType<NoContentResult>();

        var deleted = await _db.Webhooks.FindAsync(webhook.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteWebhook_ReturnsNotFound_WhenMissing()
    {
        var result = await _controller.DeleteWebhook(_testServer.Id, Guid.NewGuid(), _auditService);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteWebhook_CreatesAuditLogEntry()
    {
        var webhook = SeedWebhook(name: "Doomed Hook");

        await _controller.DeleteWebhook(_testServer.Id, webhook.Id, _auditService);

        var audit = await _db.AuditLogEntries.FirstOrDefaultAsync(
            a => a.ServerId == _testServer.Id && a.Action == AuditAction.WebhookDeleted);
        audit.Should().NotBeNull();
        audit!.Details.Should().Be("Doomed Hook");
    }

    // --- GetWebhookDeliveries ---

    [Fact]
    public async Task GetWebhookDeliveries_ReturnsOk_WithLogs()
    {
        var webhook = SeedWebhook();
        _db.WebhookDeliveryLogs.Add(new WebhookDeliveryLog
        {
            Id = Guid.NewGuid(),
            WebhookId = webhook.Id,
            EventType = "MessageCreated",
            Payload = "{}",
            StatusCode = 200,
            Success = true,
            Attempt = 1,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetWebhookDeliveries(_testServer.Id, webhook.Id);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetWebhookDeliveries_ReturnsNotFound_WhenWebhookMissing()
    {
        var result = await _controller.GetWebhookDeliveries(_testServer.Id, Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetWebhookDeliveries_CapsLimitAt100()
    {
        var webhook = SeedWebhook();
        for (var i = 0; i < 110; i++)
        {
            _db.WebhookDeliveryLogs.Add(new WebhookDeliveryLog
            {
                Id = Guid.NewGuid(),
                WebhookId = webhook.Id,
                EventType = "MessageCreated",
                Payload = "{}",
                Success = true,
                Attempt = 1,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-i)
            });
        }
        await _db.SaveChangesAsync();

        var result = await _controller.GetWebhookDeliveries(_testServer.Id, webhook.Id, limit: 200);

        result.Should().BeOfType<OkObjectResult>();
        var json = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)result).Value);
        var items = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement[]>(json);
        items!.Length.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task GetWebhookDeliveries_ReturnsEmptyList_WhenNoLogs()
    {
        var webhook = SeedWebhook();

        var result = await _controller.GetWebhookDeliveries(_testServer.Id, webhook.Id);

        result.Should().BeOfType<OkObjectResult>();
        var json = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)result).Value);
        json.Should().Be("[]");
    }

    // --- Authorization checks ---

    [Fact]
    public async Task CreateWebhook_ThrowsWhenNotAdmin()
    {
        _userService.Setup(u => u.EnsureAdminAsync(_testServer.Id, _testUser.Id, false))
            .ThrowsAsync(new UnauthorizedAccessException("Not admin"));

        var request = new CreateWebhookRequest
        {
            Name = "Hook",
            Url = "https://example.com/webhook",
            EventTypes = ["MessageCreated"]
        };

        var act = () => _controller.CreateWebhook(_testServer.Id, request, _auditService);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task DeleteWebhook_ThrowsWhenNotAdmin()
    {
        _userService.Setup(u => u.EnsureAdminAsync(_testServer.Id, _testUser.Id, false))
            .ThrowsAsync(new UnauthorizedAccessException("Not admin"));

        var act = () => _controller.DeleteWebhook(_testServer.Id, Guid.NewGuid(), _auditService);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
