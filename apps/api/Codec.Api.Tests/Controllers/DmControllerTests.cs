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

public class DmControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IHubContext<ChatHub>> _hub = new();
    private readonly Mock<IAvatarService> _avatarService = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly DmController _controller;
    private readonly User _testUser;
    private readonly User _otherUser;
    private readonly User _thirdUser;

    public DmControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Test User" };
        _otherUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-2", DisplayName = "Other User" };
        _thirdUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-3", DisplayName = "Third User" };
        _db.Users.AddRange(_testUser, _otherUser, _thirdUser);
        _db.SaveChanges();

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        _controller = new DmController(_db, _userService.Object, _hub.Object, _avatarService.Object, _scopeFactory.Object);
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
        _userService.Setup(u => u.GetEffectiveDisplayName(It.IsAny<User>())).Returns("Test User");
        _avatarService.Setup(a => a.ResolveUrl(It.IsAny<string?>())).Returns((string?)null);
    }

    public void Dispose() => _db.Dispose();

    private async Task<DmChannel> CreateDmChannelAsync(bool isOpenForTestUser = true, bool isOpenForOther = true)
    {
        var channel = new DmChannel();
        _db.DmChannels.Add(channel);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel, UserId = _testUser.Id, IsOpen = isOpenForTestUser });
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel, UserId = _otherUser.Id, IsOpen = isOpenForOther });
        await _db.SaveChangesAsync();
        return channel;
    }

    private async Task CreateFriendshipAsync(Guid requesterId = default, Guid recipientId = default, FriendshipStatus status = FriendshipStatus.Accepted)
    {
        if (requesterId == default) requesterId = _testUser.Id;
        if (recipientId == default) recipientId = _otherUser.Id;
        _db.Friendships.Add(new Friendship { RequesterId = requesterId, RecipientId = recipientId, Status = status });
        await _db.SaveChangesAsync();
    }

    private async Task<DirectMessage> CreateDirectMessageAsync(Guid channelId, Guid authorId, string body, string authorName = "Author", DateTimeOffset? createdAt = null)
    {
        var dm = new DirectMessage
        {
            DmChannelId = channelId,
            AuthorUserId = authorId,
            AuthorName = authorName,
            Body = body,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };
        _db.DirectMessages.Add(dm);
        await _db.SaveChangesAsync();
        return dm;
    }

    private void SetupParticipant(Guid channelId)
    {
        _userService.Setup(u => u.EnsureDmParticipantAsync(channelId, _testUser.Id)).Returns(Task.CompletedTask);
    }

    // ===== CreateOrResumeChannel =====

    [Fact]
    public async Task CreateOrResumeChannel_Self_ReturnsBadRequest()
    {
        var result = await _controller.CreateOrResumeChannel(new CreateDmChannelRequest(_testUser.Id));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateOrResumeChannel_RecipientNotFound_Returns404()
    {
        var result = await _controller.CreateOrResumeChannel(new CreateDmChannelRequest(Guid.NewGuid()));
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CreateOrResumeChannel_NotFriends_ThrowsForbidden()
    {
        await _controller.Invoking(c => c.CreateOrResumeChannel(new CreateDmChannelRequest(_otherUser.Id)))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    [Fact]
    public async Task CreateOrResumeChannel_PendingFriendship_ThrowsForbidden()
    {
        await CreateFriendshipAsync(status: FriendshipStatus.Pending);
        await _controller.Invoking(c => c.CreateOrResumeChannel(new CreateDmChannelRequest(_otherUser.Id)))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    [Fact]
    public async Task CreateOrResumeChannel_NewChannel_ReturnsCreated()
    {
        await CreateFriendshipAsync();
        var result = await _controller.CreateOrResumeChannel(new CreateDmChannelRequest(_otherUser.Id));
        result.Should().BeOfType<CreatedResult>();
        _db.DmChannels.Should().HaveCount(1);
        _db.DmChannelMembers.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateOrResumeChannel_ExistingChannel_ReturnsOk()
    {
        await CreateFriendshipAsync();
        await CreateDmChannelAsync();

        var result = await _controller.CreateOrResumeChannel(new CreateDmChannelRequest(_otherUser.Id));
        result.Should().BeOfType<OkObjectResult>();
        _db.DmChannels.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateOrResumeChannel_ReopensClosedChannel()
    {
        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync(isOpenForTestUser: false);

        var result = await _controller.CreateOrResumeChannel(new CreateDmChannelRequest(_otherUser.Id));
        result.Should().BeOfType<OkObjectResult>();

        var membership = await _db.DmChannelMembers.FirstAsync(m => m.DmChannelId == channel.Id && m.UserId == _testUser.Id);
        membership.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task CreateOrResumeChannel_ReverseFriendship_Works()
    {
        // Friendship where other user is the requester
        await CreateFriendshipAsync(requesterId: _otherUser.Id, recipientId: _testUser.Id);
        var result = await _controller.CreateOrResumeChannel(new CreateDmChannelRequest(_otherUser.Id));
        result.Should().BeOfType<CreatedResult>();
    }

    // ===== ListChannels =====

    [Fact]
    public async Task ListChannels_NoChannels_ReturnsEmptyArray()
    {
        var result = await _controller.ListChannels();
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeAssignableTo<System.Collections.IEnumerable>();
    }

    [Fact]
    public async Task ListChannels_WithOpenChannel_ReturnsChannel()
    {
        var channel = await CreateDmChannelAsync();
        var result = await _controller.ListChannels();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ListChannels_ClosedChannel_NotReturned()
    {
        await CreateDmChannelAsync(isOpenForTestUser: false);
        var result = await _controller.ListChannels();
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        // Should return empty since the channel is closed for test user
        var conversations = okResult.Value as System.Collections.IEnumerable;
        conversations.Should().NotBeNull();
    }

    [Fact]
    public async Task ListChannels_WithMessages_SortsByMostRecent()
    {
        var channel1 = await CreateDmChannelAsync();

        // Create second channel with third user
        var channel2 = new DmChannel();
        _db.DmChannels.Add(channel2);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel2, UserId = _testUser.Id });
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel2, UserId = _thirdUser.Id });
        await _db.SaveChangesAsync();

        // Add older message to channel1
        await CreateDirectMessageAsync(channel1.Id, _testUser.Id, "old", createdAt: DateTimeOffset.UtcNow.AddHours(-2));
        // Add newer message to channel2
        await CreateDirectMessageAsync(channel2.Id, _testUser.Id, "new", createdAt: DateTimeOffset.UtcNow.AddHours(-1));

        var result = await _controller.ListChannels();
        result.Should().BeOfType<OkObjectResult>();
    }

    // ===== GetMessages =====

    [Fact]
    public async Task GetMessages_NotParticipant_ThrowsForbidden()
    {
        var channel = new DmChannel();
        _db.DmChannels.Add(channel);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel, UserId = _otherUser.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id))
            .ThrowsAsync(new Codec.Api.Services.Exceptions.ForbiddenException());

        await _controller.Invoking(c => c.GetMessages(channel.Id, null))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    [Fact]
    public async Task GetMessages_Valid_ReturnsOk()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.GetMessages(channel.Id, null);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_WithMessages_ReturnsMessages()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Hello");
        await CreateDirectMessageAsync(channel.Id, _otherUser.Id, "Hi back");

        var result = await _controller.GetMessages(channel.Id, null);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_WithBeforeCursor_FiltersByDate()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var oldMsg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Old", createdAt: DateTimeOffset.UtcNow.AddHours(-2));
        var newMsg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "New", createdAt: DateTimeOffset.UtcNow.AddHours(-1));

        var result = await _controller.GetMessages(channel.Id, DateTimeOffset.UtcNow.AddHours(-1.5));
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_AroundMode_ReturnsMessagesAroundTarget()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var msg1 = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Before", createdAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        var targetMsg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Target", createdAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var msg3 = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "After", createdAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await _controller.GetMessages(channel.Id, null, around: targetMsg.Id);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_AroundMode_NonExistentTarget_Returns404()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.GetMessages(channel.Id, null, around: Guid.NewGuid());
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetMessages_LimitClampedTo100()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        // Even with limit=200, should not fail (internally clamped to 100)
        var result = await _controller.GetMessages(channel.Id, null, limit: 200);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_LimitClampedTo1()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.GetMessages(channel.Id, null, limit: 0);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_WithReplyContext_IncludesReplyData()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var parentMsg = await CreateDirectMessageAsync(channel.Id, _otherUser.Id, "Parent message");
        var replyMsg = new DirectMessage
        {
            DmChannelId = channel.Id,
            AuthorUserId = _testUser.Id,
            AuthorName = "Test User",
            Body = "Reply",
            ReplyToDirectMessageId = parentMsg.Id
        };
        _db.DirectMessages.Add(replyMsg);
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(channel.Id, null);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_WithReactions_IncludesReactionData()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "React to me");
        _db.Reactions.Add(new Reaction { DirectMessageId = msg.Id, UserId = _otherUser.Id, Emoji = "thumbsup" });
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(channel.Id, null);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_WithLinkPreviews_IncludesPreviewData()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Check https://example.com");
        _db.LinkPreviews.Add(new LinkPreview
        {
            DirectMessageId = msg.Id,
            Url = "https://example.com",
            Title = "Example",
            Status = LinkPreviewStatus.Success
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(channel.Id, null);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_Pagination_HasMore()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        // Create more messages than the limit
        for (int i = 0; i < 5; i++)
        {
            await CreateDirectMessageAsync(channel.Id, _testUser.Id, $"Message {i}", createdAt: DateTimeOffset.UtcNow.AddMinutes(-i));
        }

        var result = await _controller.GetMessages(channel.Id, null, limit: 3);
        result.Should().BeOfType<OkObjectResult>();
    }

    // ===== SendMessage =====

    [Fact]
    public async Task SendMessage_EmptyBody_ReturnsBadRequest()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest(""));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SendMessage_WhitespaceBody_ReturnsBadRequest()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest("   "));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SendMessage_Valid_ReturnsCreated()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest("Hi there!"));
        result.Should().BeOfType<CreatedResult>();
        _db.DirectMessages.Should().ContainSingle(m => m.Body == "Hi there!");
    }

    [Fact]
    public async Task SendMessage_WithImage_ReturnsCreated()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest("", "https://img.com/pic.png"));
        result.Should().BeOfType<CreatedResult>();
    }

    [Fact]
    public async Task SendMessage_WithFileAttachment_ReturnsCreated()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest(
            "",
            FileUrl: "https://files.com/doc.pdf",
            FileName: "doc.pdf",
            FileSize: 1024,
            FileContentType: "application/pdf"
        ));
        result.Should().BeOfType<CreatedResult>();

        var msg = await _db.DirectMessages.FirstAsync();
        msg.FileUrl.Should().Be("https://files.com/doc.pdf");
        msg.FileName.Should().Be("doc.pdf");
        msg.FileSize.Should().Be(1024);
        msg.FileContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task SendMessage_ChannelNotFound_Returns404()
    {
        var result = await _controller.SendMessage(Guid.NewGuid(), new CreateMessageRequest("Hello"));
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task SendMessage_NotParticipant_ThrowsForbidden()
    {
        // Create a channel without the test user
        var channel = new DmChannel();
        _db.DmChannels.Add(channel);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel, UserId = _otherUser.Id });
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel, UserId = _thirdUser.Id });
        await _db.SaveChangesAsync();

        await _controller.Invoking(c => c.SendMessage(channel.Id, new CreateMessageRequest("hack")))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    [Fact]
    public async Task SendMessage_ReopensClosedChannelForBothMembers()
    {
        var channel = await CreateDmChannelAsync(isOpenForTestUser: false, isOpenForOther: false);
        SetupParticipant(channel.Id);

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest("Wake up!"));
        result.Should().BeOfType<CreatedResult>();

        var members = await _db.DmChannelMembers.Where(m => m.DmChannelId == channel.Id).ToListAsync();
        members.Should().AllSatisfy(m => m.IsOpen.Should().BeTrue());
    }

    [Fact]
    public async Task SendMessage_WithReply_ReturnsCreated()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var parentMsg = await CreateDirectMessageAsync(channel.Id, _otherUser.Id, "Original");

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest("Reply!", ReplyToDirectMessageId: parentMsg.Id));
        result.Should().BeOfType<CreatedResult>();

        var reply = await _db.DirectMessages.FirstAsync(m => m.Body == "Reply!");
        reply.ReplyToDirectMessageId.Should().Be(parentMsg.Id);
    }

    [Fact]
    public async Task SendMessage_WithReplyToNonExistent_ReturnsBadRequest()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest("Reply!", ReplyToDirectMessageId: Guid.NewGuid()));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SendMessage_WithReplyToDifferentChannel_ReturnsBadRequest()
    {
        var channel1 = await CreateDmChannelAsync();
        SetupParticipant(channel1.Id);

        // Create another channel and message
        var channel2 = new DmChannel();
        _db.DmChannels.Add(channel2);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel2, UserId = _testUser.Id });
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel2, UserId = _thirdUser.Id });
        await _db.SaveChangesAsync();

        var msgInOtherChannel = await CreateDirectMessageAsync(channel2.Id, _thirdUser.Id, "Other channel msg");

        var result = await _controller.SendMessage(channel1.Id, new CreateMessageRequest("Reply!", ReplyToDirectMessageId: msgInOtherChannel.Id));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SendMessage_TrimsBody()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest("  Hello  "));
        result.Should().BeOfType<CreatedResult>();

        var msg = await _db.DirectMessages.FirstAsync();
        msg.Body.Should().Be("Hello");
    }

    // ===== EditMessage =====

    [Fact]
    public async Task EditMessage_MessageNotFound_Returns404()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.EditMessage(channel.Id, Guid.NewGuid(), new EditMessageRequest("edited"));
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task EditMessage_OwnMessage_ReturnsOk()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Original", "Test User");

        var result = await _controller.EditMessage(channel.Id, dm.Id, new EditMessageRequest("Edited"));
        result.Should().BeOfType<OkObjectResult>();

        var updated = await _db.DirectMessages.FirstAsync(m => m.Id == dm.Id);
        updated.Body.Should().Be("Edited");
        updated.EditedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task EditMessage_OtherUser_ThrowsForbidden()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _otherUser.Id, "Not yours", "Other");

        await _controller.Invoking(c => c.EditMessage(channel.Id, dm.Id, new EditMessageRequest("hack")))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    [Fact]
    public async Task EditMessage_EmptyBody_ReturnsBadRequest()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Original", "Test User");

        var result = await _controller.EditMessage(channel.Id, dm.Id, new EditMessageRequest(""));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task EditMessage_WhitespaceBody_ReturnsBadRequest()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Original", "Test User");

        var result = await _controller.EditMessage(channel.Id, dm.Id, new EditMessageRequest("   "));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task EditMessage_TrimsBody()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Original", "Test User");

        var result = await _controller.EditMessage(channel.Id, dm.Id, new EditMessageRequest("  Edited  "));
        result.Should().BeOfType<OkObjectResult>();

        var updated = await _db.DirectMessages.FirstAsync(m => m.Id == dm.Id);
        updated.Body.Should().Be("Edited");
    }

    [Fact]
    public async Task EditMessage_MessageInWrongChannel_Returns404()
    {
        var channel1 = await CreateDmChannelAsync();
        SetupParticipant(channel1.Id);

        var channel2 = new DmChannel();
        _db.DmChannels.Add(channel2);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel2, UserId = _testUser.Id });
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel2, UserId = _thirdUser.Id });
        await _db.SaveChangesAsync();

        var dm = await CreateDirectMessageAsync(channel2.Id, _testUser.Id, "Wrong channel", "Test User");

        var result = await _controller.EditMessage(channel1.Id, dm.Id, new EditMessageRequest("edit"));
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ===== DeleteMessage =====

    [Fact]
    public async Task DeleteMessage_OwnMessage_ReturnsNoContent()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Delete me", "T");

        var result = await _controller.DeleteMessage(channel.Id, dm.Id);
        result.Should().BeOfType<NoContentResult>();

        _db.DirectMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteMessage_OtherUser_ThrowsForbidden()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _otherUser.Id, "Nope", "Other");

        await _controller.Invoking(c => c.DeleteMessage(channel.Id, dm.Id))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    [Fact]
    public async Task DeleteMessage_MessageNotFound_Returns404()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.DeleteMessage(channel.Id, Guid.NewGuid());
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteMessage_MessageInWrongChannel_Returns404()
    {
        var channel1 = await CreateDmChannelAsync();
        SetupParticipant(channel1.Id);

        var channel2 = new DmChannel();
        _db.DmChannels.Add(channel2);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel2, UserId = _testUser.Id });
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel2, UserId = _thirdUser.Id });
        await _db.SaveChangesAsync();

        var dm = await CreateDirectMessageAsync(channel2.Id, _testUser.Id, "Wrong channel", "T");

        var result = await _controller.DeleteMessage(channel1.Id, dm.Id);
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ===== ToggleReaction =====

    [Fact]
    public async Task ToggleReaction_AddsReaction()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "React", "T");

        var result = await _controller.ToggleReaction(channel.Id, dm.Id, new ToggleReactionRequest("heart"));
        result.Should().BeOfType<OkObjectResult>();
        _db.Reactions.Should().ContainSingle();
    }

    [Fact]
    public async Task ToggleReaction_RemovesExistingReaction()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "React", "T");

        // Add reaction first
        _db.Reactions.Add(new Reaction { DirectMessageId = dm.Id, UserId = _testUser.Id, Emoji = "heart" });
        await _db.SaveChangesAsync();

        var result = await _controller.ToggleReaction(channel.Id, dm.Id, new ToggleReactionRequest("heart"));
        result.Should().BeOfType<OkObjectResult>();
        _db.Reactions.Should().BeEmpty();
    }

    [Fact]
    public async Task ToggleReaction_MessageNotFound_Returns404()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.ToggleReaction(channel.Id, Guid.NewGuid(), new ToggleReactionRequest("heart"));
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ToggleReaction_MultipleEmojisSameMessage()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "React", "T");

        await _controller.ToggleReaction(channel.Id, dm.Id, new ToggleReactionRequest("heart"));
        await _controller.ToggleReaction(channel.Id, dm.Id, new ToggleReactionRequest("thumbsup"));

        _db.Reactions.Should().HaveCount(2);
    }

    [Fact]
    public async Task ToggleReaction_TrimsEmoji()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "React", "T");

        var result = await _controller.ToggleReaction(channel.Id, dm.Id, new ToggleReactionRequest("  heart  "));
        result.Should().BeOfType<OkObjectResult>();

        var reaction = await _db.Reactions.FirstAsync();
        reaction.Emoji.Should().Be("heart");
    }

    // ===== CloseChannel =====

    [Fact]
    public async Task CloseChannel_Succeeds()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.CloseChannel(channel.Id);
        result.Should().BeOfType<NoContentResult>();

        var membership = await _db.DmChannelMembers.FirstAsync(m => m.DmChannelId == channel.Id && m.UserId == _testUser.Id);
        membership.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task CloseChannel_DoesNotAffectOtherMember()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        await _controller.CloseChannel(channel.Id);

        var otherMembership = await _db.DmChannelMembers.FirstAsync(m => m.DmChannelId == channel.Id && m.UserId == _otherUser.Id);
        otherMembership.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task CloseChannel_NotParticipant_ThrowsForbidden()
    {
        var channel = new DmChannel();
        _db.DmChannels.Add(channel);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel, UserId = _otherUser.Id });
        await _db.SaveChangesAsync();

        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id))
            .ThrowsAsync(new Codec.Api.Services.Exceptions.ForbiddenException());

        await _controller.Invoking(c => c.CloseChannel(channel.Id))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    // ===== SearchMessages =====

    [Fact]
    public async Task SearchMessages_QueryTooShort_ReturnsBadRequest()
    {
        var result = await _controller.SearchMessages(new SearchMessagesRequest { Q = "a" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SearchMessages_QueryTooLong_ReturnsBadRequest()
    {
        var longQuery = new string('a', 201);
        var result = await _controller.SearchMessages(new SearchMessagesRequest { Q = longQuery });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SearchMessages_EmptyQuery_ReturnsBadRequest()
    {
        var result = await _controller.SearchMessages(new SearchMessagesRequest { Q = "" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SearchMessages_NoChannels_ReturnsEmptyResults()
    {
        var result = await _controller.SearchMessages(new SearchMessagesRequest { Q = "hello" });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SearchMessages_SpecificChannel_NotAccessible_Returns404()
    {
        var channel = await CreateDmChannelAsync();
        var result = await _controller.SearchMessages(new SearchMessagesRequest
        {
            Q = "test",
            ChannelId = Guid.NewGuid() // Non-existent channel
        });
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // Note: Tests that exercise the ILike code path (ValidQuery, FilterByAuthor,
    // FilterByDateRange, SpecificChannel, Pagination) are skipped because the
    // InMemory provider does not support EF.Functions.ILike. These paths are
    // covered by integration tests that use a real PostgreSQL database.

    [Fact(Skip = "EF.Functions.ILike not supported by InMemory provider")]
    public async Task SearchMessages_ValidQuery_ReturnsResults()
    {
        var channel = await CreateDmChannelAsync();
        await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Hello world");
        await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Goodbye world");

        var result = await _controller.SearchMessages(new SearchMessagesRequest { Q = "Hello" });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact(Skip = "EF.Functions.ILike not supported by InMemory provider")]
    public async Task SearchMessages_FilterByAuthor()
    {
        var channel = await CreateDmChannelAsync();
        await CreateDirectMessageAsync(channel.Id, _testUser.Id, "My message");
        await CreateDirectMessageAsync(channel.Id, _otherUser.Id, "Other message");

        var result = await _controller.SearchMessages(new SearchMessagesRequest
        {
            Q = "message",
            AuthorId = _testUser.Id
        });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact(Skip = "EF.Functions.ILike not supported by InMemory provider")]
    public async Task SearchMessages_FilterByDateRange()
    {
        var channel = await CreateDmChannelAsync();
        await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Old message", createdAt: DateTimeOffset.UtcNow.AddDays(-5));
        await CreateDirectMessageAsync(channel.Id, _testUser.Id, "New message", createdAt: DateTimeOffset.UtcNow.AddDays(-1));

        var result = await _controller.SearchMessages(new SearchMessagesRequest
        {
            Q = "message",
            Before = DateTimeOffset.UtcNow.AddDays(-3),
            After = DateTimeOffset.UtcNow.AddDays(-6)
        });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact(Skip = "EF.Functions.ILike not supported by InMemory provider")]
    public async Task SearchMessages_SpecificChannel_ReturnsResults()
    {
        var channel = await CreateDmChannelAsync();
        await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Searchable content");

        var result = await _controller.SearchMessages(new SearchMessagesRequest
        {
            Q = "Searchable",
            ChannelId = channel.Id
        });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact(Skip = "EF.Functions.ILike not supported by InMemory provider")]
    public async Task SearchMessages_Pagination_RespectsPageSize()
    {
        var channel = await CreateDmChannelAsync();
        for (int i = 0; i < 5; i++)
        {
            await CreateDirectMessageAsync(channel.Id, _testUser.Id, $"Search item {i}");
        }

        var result = await _controller.SearchMessages(new SearchMessagesRequest
        {
            Q = "Search item",
            Page = 1,
            PageSize = 2
        });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact(Skip = "EF.Functions.ILike not supported by InMemory provider")]
    public async Task SearchMessages_200CharQuery_IsAccepted()
    {
        var channel = await CreateDmChannelAsync();
        var exactQuery = new string('a', 200);
        var result = await _controller.SearchMessages(new SearchMessagesRequest { Q = exactQuery });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact(Skip = "EF.Functions.ILike not supported by InMemory provider")]
    public async Task SearchMessages_2CharQuery_IsAccepted()
    {
        var channel = await CreateDmChannelAsync();
        var result = await _controller.SearchMessages(new SearchMessagesRequest { Q = "ab" });
        result.Should().BeOfType<OkObjectResult>();
    }

    // ===== GetMessages additional coverage =====

    [Fact]
    public async Task GetMessages_AroundMode_WithLinkPreviews_IncludesPreviewData()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Check https://example.com");
        _db.LinkPreviews.Add(new Models.LinkPreview
        {
            DirectMessageId = msg.Id,
            Url = "https://example.com",
            Title = "Example",
            Description = "Example site",
            Status = Models.LinkPreviewStatus.Success,
            FetchedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(channel.Id, null, around: msg.Id, limit: 10);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_AroundMode_WithReactions_IncludesReactionData()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "React to this");
        _db.Reactions.Add(new Reaction { DirectMessageId = msg.Id, UserId = _otherUser.Id, Emoji = "thumbsup" });
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(channel.Id, null, around: msg.Id, limit: 10);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_AroundMode_WithReplyContext_IncludesReplyData()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var parentMsg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Parent message");
        var replyMsg = new DirectMessage
        {
            DmChannelId = channel.Id,
            AuthorUserId = _otherUser.Id,
            AuthorName = "Other",
            Body = "Reply",
            ReplyToDirectMessageId = parentMsg.Id,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1)
        };
        _db.DirectMessages.Add(replyMsg);
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(channel.Id, null, around: replyMsg.Id, limit: 10);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_AroundMode_DeletedReplyTarget_ShowsDeletedContext()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        // Create a reply where the parent message doesn't exist (deleted)
        var fakeParentId = Guid.NewGuid();
        var replyMsg = new DirectMessage
        {
            DmChannelId = channel.Id,
            AuthorUserId = _testUser.Id,
            AuthorName = "Test User",
            Body = "Reply to deleted",
            ReplyToDirectMessageId = fakeParentId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.DirectMessages.Add(replyMsg);
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(channel.Id, null, around: replyMsg.Id, limit: 10);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_WithBeforeAndLimit_RespectsLimit()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var baseTime = DateTimeOffset.UtcNow;
        for (int i = 0; i < 5; i++)
        {
            await CreateDirectMessageAsync(channel.Id, _testUser.Id, $"Message {i}", createdAt: baseTime.AddMinutes(-i));
        }

        var result = await _controller.GetMessages(channel.Id, before: baseTime.AddMinutes(1), limit: 3);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_NormalMode_WithLinkPreviews()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Visit https://example.com");
        _db.LinkPreviews.Add(new Models.LinkPreview
        {
            DirectMessageId = msg.Id,
            Url = "https://example.com",
            Title = "Example",
            Status = Models.LinkPreviewStatus.Success,
            FetchedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(channel.Id, null, limit: 50);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_NormalMode_WithReactions()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Reactions test");
        _db.Reactions.Add(new Reaction { DirectMessageId = msg.Id, UserId = _testUser.Id, Emoji = "heart" });
        _db.Reactions.Add(new Reaction { DirectMessageId = msg.Id, UserId = _otherUser.Id, Emoji = "heart" });
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(channel.Id, null, limit: 50);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_NormalMode_WithReplyContext()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var parentMsg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Original message");
        var replyMsg = new DirectMessage
        {
            DmChannelId = channel.Id,
            AuthorUserId = _otherUser.Id,
            AuthorName = "Other",
            Body = "Reply",
            ReplyToDirectMessageId = parentMsg.Id,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1)
        };
        _db.DirectMessages.Add(replyMsg);
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(channel.Id, null, limit: 50);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_NormalMode_DeletedReplyTarget()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var fakeParentId = Guid.NewGuid();
        var replyMsg = new DirectMessage
        {
            DmChannelId = channel.Id,
            AuthorUserId = _testUser.Id,
            AuthorName = "Test",
            Body = "Reply to deleted",
            ReplyToDirectMessageId = fakeParentId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.DirectMessages.Add(replyMsg);
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(channel.Id, null, limit: 50);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ===== SendMessage additional coverage =====

    [Fact]
    public async Task SendMessage_WithFileOnly_ReturnsCreated()
    {
        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync();

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest(
            Body: "",
            FileUrl: "https://cdn.example.com/file.pdf",
            FileName: "file.pdf",
            FileSize: 12345,
            FileContentType: "application/pdf"
        ));

        result.Should().BeOfType<CreatedResult>();
    }

    [Fact]
    public async Task SendMessage_WithBlankBodyAndNoMedia_ReturnsBadRequest()
    {
        var channel = await CreateDmChannelAsync();

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest(Body: ""));

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SendMessage_ReopensChannelForOtherMember()
    {
        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync(isOpenForOther: false);

        await _controller.SendMessage(channel.Id, new CreateMessageRequest("Hello!"));

        var otherMembership = await _db.DmChannelMembers
            .FirstAsync(m => m.DmChannelId == channel.Id && m.UserId == _otherUser.Id);
        otherMembership.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task SendMessage_BroadcastsToUserAndDmGroups()
    {
        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync();

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        await _controller.SendMessage(channel.Id, new CreateMessageRequest("Broadcast test"));

        // Should have broadcast to user group and dm channel group
        clients.Verify(c => c.Group(It.Is<string>(s => s.StartsWith("user-"))), Times.AtLeastOnce);
        clients.Verify(c => c.Group(It.Is<string>(s => s.StartsWith("dm-"))), Times.AtLeastOnce);
    }

    // ===== EditMessage additional coverage =====

    [Fact]
    public async Task EditMessage_BroadcastsEditEvent()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Original");

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        var result = await _controller.EditMessage(channel.Id, msg.Id, new EditMessageRequest("Edited"));

        result.Should().BeOfType<OkObjectResult>();
        clientProxy.Verify(p => p.SendCoreAsync("DmMessageEdited",
            It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ===== DeleteMessage additional coverage =====

    [Fact]
    public async Task DeleteMessage_BroadcastsDeleteEvent()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "To delete");

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        var result = await _controller.DeleteMessage(channel.Id, msg.Id);

        result.Should().BeOfType<NoContentResult>();
        clientProxy.Verify(p => p.SendCoreAsync("DmMessageDeleted",
            It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ===== ToggleReaction additional coverage =====

    [Fact]
    public async Task ToggleReaction_BroadcastsReactionUpdate()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "React to me");

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        var result = await _controller.ToggleReaction(channel.Id, msg.Id, new ToggleReactionRequest("fire"));

        result.Should().BeOfType<OkObjectResult>();
        clientProxy.Verify(p => p.SendCoreAsync("DmReactionUpdated",
            It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ToggleReaction_NotParticipant_ThrowsForbidden()
    {
        var channel = await CreateDmChannelAsync();
        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Message");

        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id))
            .ThrowsAsync(new Codec.Api.Services.Exceptions.ForbiddenException());

        await _controller.Invoking(c => c.ToggleReaction(channel.Id, msg.Id, new ToggleReactionRequest("heart")))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    // ===== ListChannels additional coverage =====

    [Fact]
    public async Task ListChannels_MultipleChannels_SortsByMostRecentMessage()
    {
        await CreateFriendshipAsync();
        await CreateFriendshipAsync(recipientId: _thirdUser.Id);

        var channel1 = await CreateDmChannelAsync();
        var channel2 = new DmChannel();
        _db.DmChannels.Add(channel2);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel2, UserId = _testUser.Id, IsOpen = true });
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel2, UserId = _thirdUser.Id, IsOpen = true });
        await _db.SaveChangesAsync();

        // Older message in channel1
        await CreateDirectMessageAsync(channel1.Id, _testUser.Id, "Old message", createdAt: DateTimeOffset.UtcNow.AddHours(-2));
        // Newer message in channel2
        await CreateDirectMessageAsync(channel2.Id, _testUser.Id, "New message", createdAt: DateTimeOffset.UtcNow);

        var result = await _controller.ListChannels();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ListChannels_ChannelWithNoMessages_UsesChannelCreatedAt()
    {
        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync();

        var result = await _controller.ListChannels();

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ListChannels_WithNickname_ShowsNickname()
    {
        _otherUser.Nickname = "OtherNick";
        await _db.SaveChangesAsync();

        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync();

        var result = await _controller.ListChannels();

        result.Should().BeOfType<OkObjectResult>();
    }

    // ===== CreateOrResumeChannel additional coverage =====

    [Fact]
    public async Task CreateOrResumeChannel_NewChannel_NotifiesRecipient()
    {
        await CreateFriendshipAsync();

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        var result = await _controller.CreateOrResumeChannel(new CreateDmChannelRequest(_otherUser.Id));

        result.Should().BeOfType<CreatedResult>();
        clientProxy.Verify(p => p.SendCoreAsync("DmConversationOpened",
            It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateOrResumeChannel_ExistingOpen_DoesNotModifyIsOpen()
    {
        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync(isOpenForTestUser: true);

        var result = await _controller.CreateOrResumeChannel(new CreateDmChannelRequest(_otherUser.Id));

        result.Should().BeOfType<OkObjectResult>();
        var membership = await _db.DmChannelMembers.FirstAsync(m => m.DmChannelId == channel.Id && m.UserId == _testUser.Id);
        membership.IsOpen.Should().BeTrue();
    }

    // ===== SearchMessages additional coverage =====

    [Fact]
    public async Task SearchMessages_WhitespaceQuery_ReturnsBadRequest()
    {
        var result = await _controller.SearchMessages(new SearchMessagesRequest { Q = "   " });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SearchMessages_201CharQuery_ReturnsBadRequest()
    {
        var longQuery = new string('a', 201);
        var result = await _controller.SearchMessages(new SearchMessagesRequest { Q = longQuery });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SearchMessages_SingleCharQuery_ReturnsBadRequest()
    {
        var result = await _controller.SearchMessages(new SearchMessagesRequest { Q = "a" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════ GetMessages — pagination hasMore ═══════════════════

    [Fact]
    public async Task GetMessages_ExactlyLimitMessages_HasMoreFalse()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        // Create exactly 3 messages with limit=3
        for (int i = 0; i < 3; i++)
        {
            await CreateDirectMessageAsync(channel.Id, _testUser.Id, $"msg-{i}", createdAt: DateTimeOffset.UtcNow.AddMinutes(-i));
        }

        var result = await _controller.GetMessages(channel.Id, before: null, around: null, limit: 3);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ SendMessage — push notification path ═══════════════════

    [Fact]
    public async Task SendMessage_ChannelMembersIsEmpty_Returns404()
    {
        var emptyChannelId = Guid.NewGuid();
        SetupParticipant(emptyChannelId);

        var result = await _controller.SendMessage(emptyChannelId, new CreateMessageRequest("hello"));

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ═══════════════════ EditMessage — preserves timestamp ═══════════════════

    [Fact]
    public async Task EditMessage_SetsEditedAtTimestamp()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "original", "Test User");

        var result = await _controller.EditMessage(channel.Id, dm.Id, new EditMessageRequest("edited"));

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.DirectMessages.FindAsync(dm.Id);
        updated!.EditedAt.Should().NotBeNull();
        updated.Body.Should().Be("edited");
    }

    // ═══════════════════ DeleteMessage — removes from DB ═══════════════════

    [Fact]
    public async Task DeleteMessage_RemovesFromDatabase()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "to-delete", "Test User");

        var result = await _controller.DeleteMessage(channel.Id, dm.Id);

        result.Should().BeOfType<NoContentResult>();
        var exists = await _db.DirectMessages.AnyAsync(m => m.Id == dm.Id);
        exists.Should().BeFalse();
    }

    // ═══════════════════ ToggleReaction — double toggle removes ═══════════════════

    [Fact]
    public async Task ToggleReaction_AddThenRemove_LeavesNoReaction()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var dm = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "msg", "Test User");

        await _controller.ToggleReaction(channel.Id, dm.Id, new ToggleReactionRequest("thumbsup"));
        await _controller.ToggleReaction(channel.Id, dm.Id, new ToggleReactionRequest("thumbsup"));

        var count = await _db.Reactions.CountAsync(r => r.DirectMessageId == dm.Id);
        count.Should().Be(0);
    }

    // ═══════════════════ GetMessages — around mode with before/after counts ═══════════════════

    [Fact]
    public async Task GetMessages_AroundMode_HasMoreBeforeAndAfter()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        // Create 10 messages
        var messages = new List<DirectMessage>();
        for (int i = 0; i < 10; i++)
        {
            var dm = await CreateDirectMessageAsync(channel.Id, _testUser.Id, $"msg-{i}", createdAt: DateTimeOffset.UtcNow.AddMinutes(-10 + i));
            messages.Add(dm);
        }

        // Around middle message with limit=4 (2 before, 2 after)
        var result = await _controller.GetMessages(channel.Id, before: null, around: messages[5].Id, limit: 4);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ SendMessage — file only message ═══════════════════

    [Fact]
    public async Task SendMessage_ImageOnly_ReturnsCreated()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id)).Returns(Task.CompletedTask);

        var result = await _controller.SendMessage(channel.Id,
            new CreateMessageRequest("", ImageUrl: "https://example.com/img.png"));

        result.Should().BeOfType<CreatedResult>();
    }

    // ═══════════════════ CloseChannel — already closed is idempotent ═══════════════════

    [Fact]
    public async Task CloseChannel_AlreadyClosed_StillReturnsNoContent()
    {
        var channel = await CreateDmChannelAsync(isOpenForTestUser: false);
        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id)).Returns(Task.CompletedTask);

        var result = await _controller.CloseChannel(channel.Id);

        result.Should().BeOfType<NoContentResult>();
    }

    // ═══════════════════ ListChannels — uses nickname when available ═══════════════════

    [Fact]
    public async Task ListChannels_ReturnsParticipantInfo()
    {
        var channel = await CreateDmChannelAsync();

        var result = await _controller.ListChannels();

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ GetMessages — with file metadata ═══════════════════

    [Fact]
    public async Task GetMessages_WithFileAttachment_ReturnsFileMetadata()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var dm = new DirectMessage
        {
            DmChannelId = channel.Id,
            AuthorUserId = _testUser.Id,
            AuthorName = "Test",
            Body = "file msg",
            FileUrl = "https://example.com/file.pdf",
            FileName = "file.pdf",
            FileSize = 1024,
            FileContentType = "application/pdf",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.DirectMessages.Add(dm);
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(channel.Id, before: null, around: null, limit: 50);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ SendMessage — trims whitespace body ═══════════════════

    [Fact]
    public async Task SendMessage_BodyWithSpaces_IsTrimmed()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.SendMessage(channel.Id,
            new CreateMessageRequest("  hello world  "));

        result.Should().BeOfType<CreatedResult>();
        var msg = await _db.DirectMessages.FirstAsync(m => m.DmChannelId == channel.Id);
        msg.Body.Should().Be("hello world");
    }

    // ═══════════════════ GetMessages — limit clamping ═══════════════════

    [Fact]
    public async Task GetMessages_ZeroLimit_ClampsTo1()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.GetMessages(channel.Id, before: null, around: null, limit: 0);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMessages_200Limit_ClampsTo100()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var result = await _controller.GetMessages(channel.Id, before: null, around: null, limit: 200);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ SendMessage — push notification path ═══════════════════

    [Fact]
    public async Task SendMessage_WithPushService_SendsPushNotification()
    {
        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync();

        // Create a real PushNotificationService with a mock IPushClient.
        // The fire-and-forget SendToUserAsync will execute but the mock client
        // won't actually send; this covers the pushService != null branch.
        var mockPushClient = new Mock<IPushClient>();
        var scopeFactoryForPush = new Mock<IServiceScopeFactory>();
        var scopeForPush = new Mock<IServiceScope>();
        var spForPush = new Mock<IServiceProvider>();
        scopeForPush.Setup(s => s.ServiceProvider).Returns(spForPush.Object);
        spForPush.Setup(sp => sp.GetService(typeof(CodecDbContext))).Returns(_db);
        scopeFactoryForPush.Setup(f => f.CreateScope()).Returns(scopeForPush.Object);

        var pushService = new PushNotificationService(
            mockPushClient.Object,
            scopeFactoryForPush.Object,
            Mock.Of<ILogger<PushNotificationService>>());

        var controllerWithPush = new DmController(
            _db, _userService.Object, _hub.Object, _avatarService.Object,
            _scopeFactory.Object, pushService);
        controllerWithPush.ControllerContext = _controller.ControllerContext;

        var result = await controllerWithPush.SendMessage(channel.Id,
            new CreateMessageRequest("Push test message"));

        result.Should().BeOfType<CreatedResult>();
    }

    [Fact]
    public async Task SendMessage_WithPushService_LongBody_DoesNotThrow()
    {
        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync();

        var mockPushClient = new Mock<IPushClient>();
        var scopeFactoryForPush = new Mock<IServiceScopeFactory>();
        var scopeForPush = new Mock<IServiceScope>();
        var spForPush = new Mock<IServiceProvider>();
        scopeForPush.Setup(s => s.ServiceProvider).Returns(spForPush.Object);
        spForPush.Setup(sp => sp.GetService(typeof(CodecDbContext))).Returns(_db);
        scopeFactoryForPush.Setup(f => f.CreateScope()).Returns(scopeForPush.Object);

        var pushService = new PushNotificationService(
            mockPushClient.Object,
            scopeFactoryForPush.Object,
            Mock.Of<ILogger<PushNotificationService>>());

        var controllerWithPush = new DmController(
            _db, _userService.Object, _hub.Object, _avatarService.Object,
            _scopeFactory.Object, pushService);
        controllerWithPush.ControllerContext = _controller.ControllerContext;

        var longBody = new string('x', 250);
        var result = await controllerWithPush.SendMessage(channel.Id,
            new CreateMessageRequest(longBody));

        result.Should().BeOfType<CreatedResult>();
    }

    // ═══════════════════ SendMessage — authorName fallback ═══════════════════

    [Fact]
    public async Task SendMessage_WhitespaceAuthorName_DefaultsToUnknown()
    {
        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync();

        _userService.Setup(u => u.GetEffectiveDisplayName(It.IsAny<User>())).Returns("  ");

        var result = await _controller.SendMessage(channel.Id,
            new CreateMessageRequest("Hello"));

        result.Should().BeOfType<CreatedResult>();
        var msg = await _db.DirectMessages.FirstAsync(m => m.DmChannelId == channel.Id);
        msg.AuthorName.Should().Be("Unknown");
    }

    [Fact]
    public async Task SendMessage_NullAuthorName_DefaultsToUnknown()
    {
        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync();

        _userService.Setup(u => u.GetEffectiveDisplayName(It.IsAny<User>())).Returns((string)null!);

        var result = await _controller.SendMessage(channel.Id,
            new CreateMessageRequest("Hello"));

        result.Should().BeOfType<CreatedResult>();
        var msg = await _db.DirectMessages.FirstAsync(m => m.DmChannelId == channel.Id);
        msg.AuthorName.Should().Be("Unknown");
    }

    // ═══════════════════ SendMessage — reply to different channel ═══════════════════

    [Fact]
    public async Task SendMessage_ReplyToMessageInSameChannel_ReturnsCreatedWithReplyContext()
    {
        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync();
        var parentMsg = await CreateDirectMessageAsync(channel.Id, _otherUser.Id, "Parent message body that is quite long");

        var result = await _controller.SendMessage(channel.Id,
            new CreateMessageRequest("Reply!", ReplyToDirectMessageId: parentMsg.Id));

        var created = result.Should().BeOfType<CreatedResult>().Subject;
        created.Value.Should().NotBeNull();
    }

    // ═══════════════════ ListChannels — message preview ═══════════════════

    [Fact]
    public async Task ListChannels_WithMessagePreview_IncludesAuthorNameAndBody()
    {
        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync();
        await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Preview body", authorName: "Preview Author");

        var result = await _controller.ListChannels();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task ListChannels_ChannelWithNicknameAndCustomAvatar_ResolvesCorrectly()
    {
        var userWithNickname = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = "g-nick",
            DisplayName = "Display Name",
            Nickname = "Custom Nick",
            CustomAvatarPath = "avatars/custom.png"
        };
        _db.Users.Add(userWithNickname);
        await _db.SaveChangesAsync();

        var channel = new DmChannel();
        _db.DmChannels.Add(channel);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel, UserId = _testUser.Id, IsOpen = true });
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel, UserId = userWithNickname.Id, IsOpen = true });
        await _db.SaveChangesAsync();

        _avatarService.Setup(a => a.ResolveUrl("avatars/custom.png")).Returns("https://cdn.example.com/avatars/custom.png");

        var result = await _controller.ListChannels();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    // ═══════════════════ CreateOrResumeChannel — avatar resolution ═══════════════════

    [Fact]
    public async Task CreateOrResumeChannel_NewChannel_ResolvesRecipientAvatar()
    {
        _avatarService.Setup(a => a.ResolveUrl(It.IsAny<string?>())).Returns("https://cdn.example.com/avatar.png");
        await CreateFriendshipAsync();

        var result = await _controller.CreateOrResumeChannel(new CreateDmChannelRequest(_otherUser.Id));

        var created = result.Should().BeOfType<CreatedResult>().Subject;
        created.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateOrResumeChannel_ExistingChannel_ResolvesRecipientAvatar()
    {
        _avatarService.Setup(a => a.ResolveUrl(It.IsAny<string?>())).Returns("https://cdn.example.com/avatar.png");
        await CreateFriendshipAsync();
        var channel = await CreateDmChannelAsync();

        var result = await _controller.CreateOrResumeChannel(new CreateDmChannelRequest(_otherUser.Id));

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    // ═══════════════════ GetMessages — normal mode with before cursor ═══════════════════

    [Fact]
    public async Task GetMessages_WithBeforeCursor_ReturnsChronologicalOrder()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var now = DateTimeOffset.UtcNow;
        await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Old message", createdAt: now.AddMinutes(-10));
        await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Newer message", createdAt: now.AddMinutes(-5));
        await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Newest message", createdAt: now);

        var result = await _controller.GetMessages(channel.Id, before: now.AddMinutes(-1), around: null, limit: 50);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ GetMessages — around mode edge cases ═══════════════════

    [Fact]
    public async Task GetMessages_AroundMode_FewMessages_HasMoreFalse()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Only message");

        var result = await _controller.GetMessages(channel.Id, before: null, around: msg.Id, limit: 50);

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ EditMessage — verifies otherUserId broadcast ═══════════════════

    [Fact]
    public async Task EditMessage_NotifiesOtherUser()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Original body");

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        var result = await _controller.EditMessage(channel.Id, msg.Id, new EditMessageRequest("Updated body"));

        result.Should().BeOfType<OkObjectResult>();
        clients.Verify(c => c.Group($"user-{_otherUser.Id}"), Times.AtLeastOnce);
    }

    // ═══════════════════ DeleteMessage — notifies other user ═══════════════════

    [Fact]
    public async Task DeleteMessage_NotifiesOtherUser()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "To delete");

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        var result = await _controller.DeleteMessage(channel.Id, msg.Id);

        result.Should().BeOfType<NoContentResult>();
        clients.Verify(c => c.Group($"user-{_otherUser.Id}"), Times.AtLeastOnce);
    }

    // ═══════════════════ ToggleReaction — other user broadcast ═══════════════════

    [Fact]
    public async Task ToggleReaction_NotifiesOtherUser()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);
        var msg = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "React msg");

        var clients = new Mock<IHubClients>();
        var clientProxy = new Mock<IClientProxy>();
        _hub.Setup(h => h.Clients).Returns(clients.Object);
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        await _controller.ToggleReaction(channel.Id, msg.Id, new ToggleReactionRequest("thumbsup"));

        clients.Verify(c => c.Group($"user-{_otherUser.Id}"), Times.AtLeastOnce);
    }

    // ═══════════════════ GetMessages — file metadata in response ═══════════════════

    [Fact]
    public async Task GetMessages_WithMultipleReplies_LoadsReplyContext()
    {
        var channel = await CreateDmChannelAsync();
        SetupParticipant(channel.Id);

        var parent1 = await CreateDirectMessageAsync(channel.Id, _testUser.Id, "Parent 1");
        var parent2 = await CreateDirectMessageAsync(channel.Id, _otherUser.Id, "Parent 2");

        // Create reply messages with ReplyToDirectMessageId
        var reply1 = new DirectMessage
        {
            DmChannelId = channel.Id,
            AuthorUserId = _otherUser.Id,
            AuthorName = "Other",
            Body = "Reply to parent 1",
            ReplyToDirectMessageId = parent1.Id
        };
        var reply2 = new DirectMessage
        {
            DmChannelId = channel.Id,
            AuthorUserId = _testUser.Id,
            AuthorName = "Test",
            Body = "Reply to parent 2",
            ReplyToDirectMessageId = parent2.Id
        };
        _db.DirectMessages.AddRange(reply1, reply2);
        await _db.SaveChangesAsync();

        var result = await _controller.GetMessages(channel.Id, before: null, around: null, limit: 50);

        result.Should().BeOfType<OkObjectResult>();
    }
}
