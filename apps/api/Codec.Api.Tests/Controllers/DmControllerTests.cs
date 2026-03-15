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

    public DmControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Test User" };
        _otherUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-2", DisplayName = "Other User" };
        _db.Users.AddRange(_testUser, _otherUser);
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
            .ReturnsAsync(_db.Users.First(u => u.Id == _testUser.Id));
        _userService.Setup(u => u.GetEffectiveDisplayName(It.IsAny<User>())).Returns("Test User");
        _avatarService.Setup(a => a.ResolveUrl(It.IsAny<string?>())).Returns((string?)null);
    }

    public void Dispose() => _db.Dispose();

    private async Task<DmChannel> CreateDmChannelAsync()
    {
        var channel = new DmChannel();
        _db.DmChannels.Add(channel);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel, UserId = _testUser.Id });
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = channel, UserId = _otherUser.Id });
        await _db.SaveChangesAsync();
        return channel;
    }

    private async Task CreateFriendshipAsync()
    {
        _db.Friendships.Add(new Friendship { RequesterId = _testUser.Id, RecipientId = _otherUser.Id, Status = FriendshipStatus.Accepted });
        await _db.SaveChangesAsync();
    }

    // --- CreateOrResumeChannel ---

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
    public async Task CreateOrResumeChannel_NewChannel_ReturnsCreated()
    {
        await CreateFriendshipAsync();
        var result = await _controller.CreateOrResumeChannel(new CreateDmChannelRequest(_otherUser.Id));
        result.Should().BeOfType<CreatedResult>();
        _db.DmChannels.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateOrResumeChannel_ExistingChannel_ReturnsOk()
    {
        await CreateFriendshipAsync();
        await CreateDmChannelAsync();

        var result = await _controller.CreateOrResumeChannel(new CreateDmChannelRequest(_otherUser.Id));
        result.Should().BeOfType<OkObjectResult>();
        _db.DmChannels.Should().HaveCount(1); // no new channel created
    }

    // --- ListChannels ---

    [Fact]
    public async Task ListChannels_ReturnsOk()
    {
        var result = await _controller.ListChannels();
        result.Should().BeOfType<OkObjectResult>();
    }

    // --- GetMessages ---

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
        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id)).Returns(Task.CompletedTask);

        var result = await _controller.GetMessages(channel.Id, null);
        result.Should().BeOfType<OkObjectResult>();
    }

    // --- SendMessage ---

    [Fact]
    public async Task SendMessage_EmptyBody_ReturnsBadRequest()
    {
        var channel = await CreateDmChannelAsync();
        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id)).Returns(Task.CompletedTask);

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest(""));
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SendMessage_Valid_ReturnsCreated()
    {
        var channel = await CreateDmChannelAsync();
        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id)).Returns(Task.CompletedTask);

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest("Hi there!"));
        result.Should().BeOfType<CreatedResult>();
        _db.DirectMessages.Should().ContainSingle(m => m.Body == "Hi there!");
    }

    [Fact]
    public async Task SendMessage_WithImage_ReturnsCreated()
    {
        var channel = await CreateDmChannelAsync();
        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id)).Returns(Task.CompletedTask);

        var result = await _controller.SendMessage(channel.Id, new CreateMessageRequest("", "https://img.com/pic.png"));
        result.Should().BeOfType<CreatedResult>();
    }

    // --- EditMessage ---

    [Fact]
    public async Task EditMessage_MessageNotFound_Returns404()
    {
        var channel = await CreateDmChannelAsync();
        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id)).Returns(Task.CompletedTask);

        var result = await _controller.EditMessage(channel.Id, Guid.NewGuid(), new EditMessageRequest("edited"));
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task EditMessage_OwnMessage_ReturnsOk()
    {
        var channel = await CreateDmChannelAsync();
        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id)).Returns(Task.CompletedTask);
        var dm = new DirectMessage { DmChannelId = channel.Id, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Original" };
        _db.DirectMessages.Add(dm);
        await _db.SaveChangesAsync();

        var result = await _controller.EditMessage(channel.Id, dm.Id, new EditMessageRequest("Edited"));
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task EditMessage_OtherUser_ThrowsForbidden()
    {
        var channel = await CreateDmChannelAsync();
        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id)).Returns(Task.CompletedTask);
        var dm = new DirectMessage { DmChannelId = channel.Id, AuthorUserId = _otherUser.Id, AuthorName = "Other", Body = "Not yours" };
        _db.DirectMessages.Add(dm);
        await _db.SaveChangesAsync();

        await _controller.Invoking(c => c.EditMessage(channel.Id, dm.Id, new EditMessageRequest("hack")))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    // --- DeleteMessage ---

    [Fact]
    public async Task DeleteMessage_OwnMessage_ReturnsNoContent()
    {
        var channel = await CreateDmChannelAsync();
        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id)).Returns(Task.CompletedTask);
        var dm = new DirectMessage { DmChannelId = channel.Id, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "Delete me" };
        _db.DirectMessages.Add(dm);
        await _db.SaveChangesAsync();

        var result = await _controller.DeleteMessage(channel.Id, dm.Id);
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteMessage_OtherUser_ThrowsForbidden()
    {
        var channel = await CreateDmChannelAsync();
        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id)).Returns(Task.CompletedTask);
        var dm = new DirectMessage { DmChannelId = channel.Id, AuthorUserId = _otherUser.Id, AuthorName = "Other", Body = "Nope" };
        _db.DirectMessages.Add(dm);
        await _db.SaveChangesAsync();

        await _controller.Invoking(c => c.DeleteMessage(channel.Id, dm.Id))
            .Should().ThrowAsync<Codec.Api.Services.Exceptions.ForbiddenException>();
    }

    // --- ToggleReaction ---

    [Fact]
    public async Task ToggleReaction_AddsReaction()
    {
        var channel = await CreateDmChannelAsync();
        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id)).Returns(Task.CompletedTask);
        var dm = new DirectMessage { DmChannelId = channel.Id, AuthorUserId = _testUser.Id, AuthorName = "T", Body = "React" };
        _db.DirectMessages.Add(dm);
        await _db.SaveChangesAsync();

        var result = await _controller.ToggleReaction(channel.Id, dm.Id, new ToggleReactionRequest("❤️"));
        result.Should().BeOfType<OkObjectResult>();
        _db.Reactions.Should().ContainSingle();
    }

    // --- CloseChannel ---

    [Fact]
    public async Task CloseChannel_Succeeds()
    {
        var channel = await CreateDmChannelAsync();
        _userService.Setup(u => u.EnsureDmParticipantAsync(channel.Id, _testUser.Id)).Returns(Task.CompletedTask);

        var result = await _controller.CloseChannel(channel.Id);
        result.Should().BeOfType<NoContentResult>();

        var membership = await _db.DmChannelMembers.FirstAsync(m => m.DmChannelId == channel.Id && m.UserId == _testUser.Id);
        membership.IsOpen.Should().BeFalse();
    }
}
