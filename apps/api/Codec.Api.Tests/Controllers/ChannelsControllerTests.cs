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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class ChannelsControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IHubContext<ChatHub>> _hub = new();
    private readonly Mock<IAvatarService> _avatarService = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly MessageCacheService _messageCache;
    private readonly AuditService _auditService;
    private readonly ChannelsController _controller;
    private readonly User _testUser;
    private readonly Server _testServer;
    private readonly Channel _testChannel;

    public ChannelsControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Test User" };
        _testServer = new Server { Name = "Test Server" };
        _testChannel = new Channel { Server = _testServer, Name = "general" };

        _db.Users.Add(_testUser);
        _db.Servers.Add(_testServer);
        _db.Channels.Add(_testChannel);
        _db.ServerMembers.Add(new ServerMember { Server = _testServer, UserId = _testUser.Id, Role = ServerRole.Member });
        _db.SaveChanges();

        _messageCache = new MessageCacheService(new Mock<ILogger<MessageCacheService>>().Object);
        _auditService = new AuditService(_db);

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        _controller = new ChannelsController(_db, _userService.Object, _hub.Object, _avatarService.Object, _scopeFactory.Object, _messageCache);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", "g-1"), new Claim("name", "Test User")
                ], "Bearer"))
            }
        };

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));
        _userService.Setup(u => u.EnsureMemberAsync(_testServer.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = _testServer.Id, UserId = _testUser.Id });
        _userService.Setup(u => u.GetEffectiveDisplayName(It.IsAny<User>())).Returns("Test User");
        _avatarService.Setup(a => a.ResolveUrl(It.IsAny<string?>())).Returns((string?)null);
    }

    public void Dispose() => _db.Dispose();

    // --- GetMessages ---

    [Fact]
    public async Task GetMessages_ChannelNotFound_Returns404()
    {
        var result = await _controller.GetMessages(Guid.NewGuid());
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetMessages_ValidChannel_ReturnsOk()
    {
        var result = await _controller.GetMessages(_testChannel.Id);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_WithMessages_ReturnsMessages()
    {
        _db.Messages.Add(new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "Test", Body = "Hello" });
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(_testChannel.Id);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_ClampsLimit()
    {
        var result = await _controller.GetMessages(_testChannel.Id, limit: 500);
        result.Should().BeOfType<OkObjectResult>();
    }

    // --- PostMessage ---

    [Fact]
    public async Task PostMessage_EmptyBodyAndImage_ReturnsBadRequest()
    {
        var result = await _controller.PostMessage(_testChannel.Id, new CreateMessageRequest(""));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task PostMessage_ChannelNotFound_Returns404()
    {
        var result = await _controller.PostMessage(Guid.NewGuid(), new CreateMessageRequest("hello"));
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task PostMessage_ValidMessage_ReturnsCreated()
    {
        var result = await _controller.PostMessage(_testChannel.Id, new CreateMessageRequest("Hello world"));
        result.Should().BeOfType<CreatedResult>();
        _db.Messages.Should().ContainSingle(m => m.Body == "Hello world");
    }

    [Fact]
    public async Task PostMessage_WithImage_ReturnsCreated()
    {
        var result = await _controller.PostMessage(_testChannel.Id, new CreateMessageRequest("", "https://example.com/img.png"));
        result.Should().BeOfType<CreatedResult>();
    }

    [Fact]
    public async Task PostMessage_InvalidReplyTo_ReturnsBadRequest()
    {
        var result = await _controller.PostMessage(_testChannel.Id, new CreateMessageRequest("reply", ReplyToMessageId: Guid.NewGuid()));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task PostMessage_ReplyToDifferentChannel_ReturnsBadRequest()
    {
        var otherChannel = new Channel { Server = _testServer, Name = "other" };
        _db.Channels.Add(otherChannel);
        var msg = new Message { Channel = otherChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Original" };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        var result = await _controller.PostMessage(_testChannel.Id, new CreateMessageRequest("reply", ReplyToMessageId: msg.Id));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // --- DeleteMessage ---

    [Fact]
    public async Task DeleteMessage_ChannelNotFound_Returns404()
    {
        var result = await _controller.DeleteMessage(Guid.NewGuid(), Guid.NewGuid(), _auditService);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteMessage_MessageNotFound_Returns404()
    {
        var result = await _controller.DeleteMessage(_testChannel.Id, Guid.NewGuid(), _auditService);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteMessage_OwnMessage_ReturnsNoContent()
    {
        var msg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Delete me" };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        var result = await _controller.DeleteMessage(_testChannel.Id, msg.Id, _auditService);
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteMessage_OtherUserMessage_ThrowsForbidden()
    {
        var otherUser = new User { GoogleSubject = "g-other", DisplayName = "Other" };
        _db.Users.Add(otherUser);
        var msg = new Message { Channel = _testChannel, AuthorUserId = otherUser.Id, AuthorName = "Other", Body = "Not yours" };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        await _controller.Invoking(c => c.DeleteMessage(_testChannel.Id, msg.Id, _auditService))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    // --- EditMessage ---

    [Fact]
    public async Task EditMessage_ChannelNotFound_Returns404()
    {
        var result = await _controller.EditMessage(Guid.NewGuid(), Guid.NewGuid(), new EditMessageRequest("edited"));
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task EditMessage_MessageNotFound_Returns404()
    {
        var result = await _controller.EditMessage(_testChannel.Id, Guid.NewGuid(), new EditMessageRequest("edited"));
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task EditMessage_OwnMessage_ReturnsOk()
    {
        var msg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Original" };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        var result = await _controller.EditMessage(_testChannel.Id, msg.Id, new EditMessageRequest("Edited body"));
        result.Should().BeOfType<OkObjectResult>();

        var updated = await _db.Messages.FindAsync(msg.Id);
        updated!.Body.Should().Be("Edited body");
        updated.EditedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task EditMessage_OtherUserMessage_ThrowsForbidden()
    {
        var other = new User { GoogleSubject = "g-other2", DisplayName = "Other" };
        _db.Users.Add(other);
        var msg = new Message { Channel = _testChannel, AuthorUserId = other.Id, AuthorName = "Other", Body = "Not yours" };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        await _controller.Invoking(c => c.EditMessage(_testChannel.Id, msg.Id, new EditMessageRequest("hack")))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    // --- ToggleReaction ---

    [Fact]
    public async Task ToggleReaction_ChannelNotFound_Returns404()
    {
        var result = await _controller.ToggleReaction(Guid.NewGuid(), Guid.NewGuid(), new ToggleReactionRequest("👍"));
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ToggleReaction_MessageNotFound_Returns404()
    {
        var result = await _controller.ToggleReaction(_testChannel.Id, Guid.NewGuid(), new ToggleReactionRequest("👍"));
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ToggleReaction_AddsReaction_ReturnsOk()
    {
        var msg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "React to me" };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        var result = await _controller.ToggleReaction(_testChannel.Id, msg.Id, new ToggleReactionRequest("👍"));
        result.Should().BeOfType<OkObjectResult>();
        _db.Reactions.Should().ContainSingle(r => r.MessageId == msg.Id && r.Emoji == "👍");
    }

    [Fact]
    public async Task ToggleReaction_RemovesExistingReaction()
    {
        var msg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "React" };
        _db.Messages.Add(msg);
        _db.Reactions.Add(new Reaction { MessageId = msg.Id, UserId = _testUser.Id, Emoji = "👍" });
        await _db.SaveChangesAsync();

        var result = await _controller.ToggleReaction(_testChannel.Id, msg.Id, new ToggleReactionRequest("👍"));
        result.Should().BeOfType<OkObjectResult>();
        _db.Reactions.Should().BeEmpty();
    }

    // --- PurgeChannelMessages (requires global admin) ---

    [Fact]
    public async Task PurgeChannelMessages_ChannelNotFound_Returns404()
    {
        var result = await _controller.PurgeChannelMessages(Guid.NewGuid(), _auditService);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task PurgeChannelMessages_NotGlobalAdmin_ThrowsForbidden()
    {
        await _controller.Invoking(c => c.PurgeChannelMessages(_testChannel.Id, _auditService))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    // --- GetMessages around mode ---

    [Fact]
    public async Task GetMessages_AroundMode_TargetNotFound_Returns404()
    {
        var result = await _controller.GetMessages(_testChannel.Id, around: Guid.NewGuid());
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetMessages_AroundMode_ReturnsMessages()
    {
        var msg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Target" };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(_testChannel.Id, around: msg.Id);
        result.Should().BeOfType<OkObjectResult>();
    }

    // --- Pin messages ---

    private void SetupAdminUser()
    {
        _userService.Setup(u => u.EnsureAdminAsync(_testServer.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = _testServer.Id, UserId = _testUser.Id, Role = ServerRole.Admin });
    }

    [Fact]
    public async Task GetPinnedMessages_EmptyChannel_ReturnsEmptyList()
    {
        var result = await _controller.GetPinnedMessages(_testChannel.Id);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetPinnedMessages_ChannelNotFound_Returns404()
    {
        var result = await _controller.GetPinnedMessages(Guid.NewGuid());
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task PinMessage_Success_Returns201()
    {
        SetupAdminUser();
        var msg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Pin me" };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        var result = await _controller.PinMessage(_testChannel.Id, msg.Id, _auditService);
        result.Should().BeOfType<CreatedResult>();

        _db.PinnedMessages.Should().ContainSingle(p => p.MessageId == msg.Id && p.ChannelId == _testChannel.Id);
    }

    [Fact]
    public async Task PinMessage_CreatesSystemMessage()
    {
        SetupAdminUser();
        var msg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Pin me" };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        await _controller.PinMessage(_testChannel.Id, msg.Id, _auditService);

        _db.Messages.Should().Contain(m => m.MessageType == MessageType.PinNotification && m.AuthorName == "System");
    }

    [Fact]
    public async Task PinMessage_CreatesAuditLogEntry()
    {
        SetupAdminUser();
        var msg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Pin me" };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        await _controller.PinMessage(_testChannel.Id, msg.Id, _auditService);

        _db.AuditLogEntries.Should().ContainSingle(a => a.Action == AuditAction.MessagePinned);
    }

    [Fact]
    public async Task PinMessage_ChannelNotFound_Returns404()
    {
        SetupAdminUser();
        var result = await _controller.PinMessage(Guid.NewGuid(), Guid.NewGuid(), _auditService);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task PinMessage_MessageNotFound_Returns404()
    {
        SetupAdminUser();
        var result = await _controller.PinMessage(_testChannel.Id, Guid.NewGuid(), _auditService);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task PinMessage_AlreadyPinned_Returns400()
    {
        SetupAdminUser();
        var msg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Pin me" };
        _db.Messages.Add(msg);
        _db.PinnedMessages.Add(new PinnedMessage { MessageId = msg.Id, ChannelId = _testChannel.Id, PinnedByUserId = _testUser.Id });
        await _db.SaveChangesAsync();

        var result = await _controller.PinMessage(_testChannel.Id, msg.Id, _auditService);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task PinMessage_ExceedsLimit_Returns400()
    {
        SetupAdminUser();
        for (var i = 0; i < 50; i++)
        {
            var m = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = $"Msg {i}" };
            _db.Messages.Add(m);
            _db.PinnedMessages.Add(new PinnedMessage { MessageId = m.Id, ChannelId = _testChannel.Id, PinnedByUserId = _testUser.Id });
        }
        var extraMsg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "51st" };
        _db.Messages.Add(extraMsg);
        await _db.SaveChangesAsync();

        var result = await _controller.PinMessage(_testChannel.Id, extraMsg.Id, _auditService);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task PinMessage_RequiresAdmin_ThrowsForbidden()
    {
        _userService.Setup(u => u.EnsureAdminAsync(_testServer.Id, _testUser.Id, false))
            .ThrowsAsync(new Codec.Api.Services.Exceptions.ForbiddenException());

        var msg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Pin me" };
        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        await FluentActions.Invoking(() => _controller.PinMessage(_testChannel.Id, msg.Id, _auditService))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    [Fact]
    public async Task UnpinMessage_Success_Returns204()
    {
        SetupAdminUser();
        var msg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Unpin me" };
        _db.Messages.Add(msg);
        _db.PinnedMessages.Add(new PinnedMessage { MessageId = msg.Id, ChannelId = _testChannel.Id, PinnedByUserId = _testUser.Id });
        await _db.SaveChangesAsync();

        var result = await _controller.UnpinMessage(_testChannel.Id, msg.Id, _auditService);
        result.Should().BeOfType<NoContentResult>();

        _db.PinnedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task UnpinMessage_NotPinned_Returns404()
    {
        SetupAdminUser();
        var result = await _controller.UnpinMessage(_testChannel.Id, Guid.NewGuid(), _auditService);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UnpinMessage_CreatesAuditLogEntry()
    {
        SetupAdminUser();
        var msg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Unpin me" };
        _db.Messages.Add(msg);
        _db.PinnedMessages.Add(new PinnedMessage { MessageId = msg.Id, ChannelId = _testChannel.Id, PinnedByUserId = _testUser.Id });
        await _db.SaveChangesAsync();

        await _controller.UnpinMessage(_testChannel.Id, msg.Id, _auditService);

        _db.AuditLogEntries.Should().ContainSingle(a => a.Action == AuditAction.MessageUnpinned);
    }

    [Fact]
    public async Task GetPinnedMessages_ReturnsPinnedMessages()
    {
        var msg = new Message { Channel = _testChannel, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Pinned" };
        _db.Messages.Add(msg);
        _db.PinnedMessages.Add(new PinnedMessage { MessageId = msg.Id, ChannelId = _testChannel.Id, PinnedByUserId = _testUser.Id });
        await _db.SaveChangesAsync();

        var result = await _controller.GetPinnedMessages(_testChannel.Id);
        result.Should().BeOfType<OkObjectResult>();
    }
}
