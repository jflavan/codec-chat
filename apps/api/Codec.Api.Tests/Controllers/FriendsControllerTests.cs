using System.Security.Claims;
using Codec.Api.Controllers;
using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Codec.Api.Services;
using Codec.Api.Services.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class FriendsControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IHubContext<ChatHub>> _hub = new();
    private readonly Mock<IAvatarService> _avatarService = new();
    private readonly FriendsController _controller;
    private readonly User _testUser;
    private readonly User _otherUser;

    public FriendsControllerTests()
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

        _controller = new FriendsController(_db, _userService.Object, _hub.Object, _avatarService.Object);
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

    // --- GetFriends ---

    [Fact]
    public async Task GetFriends_NoFriends_ReturnsEmptyList()
    {
        var result = await _controller.GetFriends();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IEnumerable;
        list.Should().NotBeNull();
        list!.Cast<object>().Should().BeEmpty();
    }

    [Fact]
    public async Task GetFriends_HasAcceptedFriend_ReturnsFriend()
    {
        _db.Friendships.Add(new Friendship
        {
            RequesterId = _testUser.Id,
            RecipientId = _otherUser.Id,
            Status = FriendshipStatus.Accepted
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetFriends();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IEnumerable;
        list!.Cast<object>().Should().HaveCount(1);
    }

    [Fact]
    public async Task GetFriends_PendingFriendship_NotReturned()
    {
        _db.Friendships.Add(new Friendship
        {
            RequesterId = _testUser.Id,
            RecipientId = _otherUser.Id,
            Status = FriendshipStatus.Pending
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetFriends();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IEnumerable;
        list!.Cast<object>().Should().BeEmpty();
    }

    [Fact]
    public async Task GetFriends_AsRecipient_ReturnsFriend()
    {
        _db.Friendships.Add(new Friendship
        {
            RequesterId = _otherUser.Id,
            RecipientId = _testUser.Id,
            Status = FriendshipStatus.Accepted
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetFriends();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IEnumerable;
        list!.Cast<object>().Should().HaveCount(1);
    }

    // --- RemoveFriend ---

    [Fact]
    public async Task RemoveFriend_ExistingFriendship_ReturnsNoContent()
    {
        var friendship = new Friendship
        {
            RequesterId = _testUser.Id,
            RecipientId = _otherUser.Id,
            Status = FriendshipStatus.Accepted
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();

        var result = await _controller.RemoveFriend(friendship.Id);

        result.Should().BeOfType<NoContentResult>();
        _db.Friendships.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveFriend_NotFound_ReturnsNotFound()
    {
        var result = await _controller.RemoveFriend(Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task RemoveFriend_NotParticipant_ThrowsForbidden()
    {
        var thirdUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-3", DisplayName = "Third" };
        _db.Users.Add(thirdUser);
        var friendship = new Friendship
        {
            RequesterId = _otherUser.Id,
            RecipientId = thirdUser.Id,
            Status = FriendshipStatus.Accepted
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();

        await FluentActions.Invoking(() => _controller.RemoveFriend(friendship.Id))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task RemoveFriend_PendingFriendship_ReturnsNotFound()
    {
        var friendship = new Friendship
        {
            RequesterId = _testUser.Id,
            RecipientId = _otherUser.Id,
            Status = FriendshipStatus.Pending
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();

        var result = await _controller.RemoveFriend(friendship.Id);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // --- SendRequest ---

    [Fact]
    public async Task SendRequest_ValidRecipient_ReturnsCreated()
    {
        var request = new SendFriendRequestRequest(RecipientUserId: _otherUser.Id);

        var result = await _controller.SendRequest(request);

        result.Should().BeOfType<CreatedResult>();
        _db.Friendships.Should().HaveCount(1);
        var f = _db.Friendships.First();
        f.RequesterId.Should().Be(_testUser.Id);
        f.RecipientId.Should().Be(_otherUser.Id);
        f.Status.Should().Be(FriendshipStatus.Pending);
    }

    [Fact]
    public async Task SendRequest_ToSelf_ReturnsBadRequest()
    {
        var request = new SendFriendRequestRequest(RecipientUserId: _testUser.Id);

        var result = await _controller.SendRequest(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SendRequest_RecipientNotFound_ReturnsNotFound()
    {
        var request = new SendFriendRequestRequest(RecipientUserId: Guid.NewGuid());

        var result = await _controller.SendRequest(request);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task SendRequest_ExistingFriendship_ReturnsConflict()
    {
        _db.Friendships.Add(new Friendship
        {
            RequesterId = _testUser.Id,
            RecipientId = _otherUser.Id,
            Status = FriendshipStatus.Accepted
        });
        await _db.SaveChangesAsync();

        var request = new SendFriendRequestRequest(RecipientUserId: _otherUser.Id);
        var result = await _controller.SendRequest(request);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task SendRequest_ExistingPendingRequest_ReturnsConflict()
    {
        _db.Friendships.Add(new Friendship
        {
            RequesterId = _otherUser.Id,
            RecipientId = _testUser.Id,
            Status = FriendshipStatus.Pending
        });
        await _db.SaveChangesAsync();

        var request = new SendFriendRequestRequest(RecipientUserId: _otherUser.Id);
        var result = await _controller.SendRequest(request);

        result.Should().BeOfType<ConflictObjectResult>();
    }

    // --- GetRequests ---

    [Fact]
    public async Task GetRequests_ReceivedDefault_ReturnsReceivedRequests()
    {
        _db.Friendships.Add(new Friendship
        {
            RequesterId = _otherUser.Id,
            RecipientId = _testUser.Id,
            Status = FriendshipStatus.Pending
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetRequests(null);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IEnumerable;
        list!.Cast<object>().Should().HaveCount(1);
    }

    [Fact]
    public async Task GetRequests_SentDirection_ReturnsSentRequests()
    {
        _db.Friendships.Add(new Friendship
        {
            RequesterId = _testUser.Id,
            RecipientId = _otherUser.Id,
            Status = FriendshipStatus.Pending
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetRequests("sent");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IEnumerable;
        list!.Cast<object>().Should().HaveCount(1);
    }

    [Fact]
    public async Task GetRequests_NoPendingRequests_ReturnsEmpty()
    {
        _db.Friendships.Add(new Friendship
        {
            RequesterId = _otherUser.Id,
            RecipientId = _testUser.Id,
            Status = FriendshipStatus.Accepted
        });
        await _db.SaveChangesAsync();

        var result = await _controller.GetRequests(null);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IEnumerable;
        list!.Cast<object>().Should().BeEmpty();
    }

    // --- RespondToRequest ---

    [Fact]
    public async Task RespondToRequest_Accept_UpdatesStatus()
    {
        var friendship = new Friendship
        {
            RequesterId = _otherUser.Id,
            RecipientId = _testUser.Id,
            Status = FriendshipStatus.Pending
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();

        var request = new RespondFriendRequestRequest(Action: "accept");
        var result = await _controller.RespondToRequest(friendship.Id, request);

        result.Should().BeOfType<OkObjectResult>();
        var updated = _db.Friendships.First(f => f.Id == friendship.Id);
        updated.Status.Should().Be(FriendshipStatus.Accepted);
    }

    [Fact]
    public async Task RespondToRequest_Decline_UpdatesStatus()
    {
        var friendship = new Friendship
        {
            RequesterId = _otherUser.Id,
            RecipientId = _testUser.Id,
            Status = FriendshipStatus.Pending
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();

        var request = new RespondFriendRequestRequest(Action: "decline");
        var result = await _controller.RespondToRequest(friendship.Id, request);

        result.Should().BeOfType<OkObjectResult>();
        var updated = _db.Friendships.First(f => f.Id == friendship.Id);
        updated.Status.Should().Be(FriendshipStatus.Declined);
    }

    [Fact]
    public async Task RespondToRequest_InvalidAction_ReturnsBadRequest()
    {
        var request = new RespondFriendRequestRequest(Action: "maybe");

        var result = await _controller.RespondToRequest(Guid.NewGuid(), request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RespondToRequest_NotFound_ReturnsNotFound()
    {
        var request = new RespondFriendRequestRequest(Action: "accept");

        var result = await _controller.RespondToRequest(Guid.NewGuid(), request);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task RespondToRequest_NotRecipient_ThrowsForbidden()
    {
        var friendship = new Friendship
        {
            RequesterId = _testUser.Id,
            RecipientId = _otherUser.Id,
            Status = FriendshipStatus.Pending
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();

        var request = new RespondFriendRequestRequest(Action: "accept");

        await FluentActions.Invoking(() => _controller.RespondToRequest(friendship.Id, request))
            .Should().ThrowAsync<ForbiddenException>();
    }

    // --- CancelRequest ---

    [Fact]
    public async Task CancelRequest_ByRequester_ReturnsNoContent()
    {
        var friendship = new Friendship
        {
            RequesterId = _testUser.Id,
            RecipientId = _otherUser.Id,
            Status = FriendshipStatus.Pending
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();

        var result = await _controller.CancelRequest(friendship.Id);

        result.Should().BeOfType<NoContentResult>();
        _db.Friendships.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelRequest_NotFound_ReturnsNotFound()
    {
        var result = await _controller.CancelRequest(Guid.NewGuid());

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CancelRequest_NotRequester_ThrowsForbidden()
    {
        var friendship = new Friendship
        {
            RequesterId = _otherUser.Id,
            RecipientId = _testUser.Id,
            Status = FriendshipStatus.Pending
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();

        await FluentActions.Invoking(() => _controller.CancelRequest(friendship.Id))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task CancelRequest_AcceptedFriendship_ReturnsNotFound()
    {
        var friendship = new Friendship
        {
            RequesterId = _testUser.Id,
            RecipientId = _otherUser.Id,
            Status = FriendshipStatus.Accepted
        };
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync();

        var result = await _controller.CancelRequest(friendship.Id);

        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
