using System.Security.Claims;
using Codec.Api.Controllers;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class PresenceControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly PresenceTracker _tracker = new();
    private readonly PresenceController _controller;
    private readonly User _testUser;
    private readonly User _otherUser;
    private readonly Server _server;

    public PresenceControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-1", DisplayName = "Test User" };
        _otherUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-2", DisplayName = "Other User" };
        _server = new Server { Id = Guid.NewGuid(), Name = "Test Server" };
        _db.Users.AddRange(_testUser, _otherUser);
        _db.Servers.Add(_server);
        _db.SaveChanges();

        _controller = new PresenceController(_db, _userService.Object, _tracker);
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
    }

    public void Dispose() => _db.Dispose();

    private void AddServerMember(Guid userId)
    {
        _db.ServerMembers.Add(new ServerMember
        {
            ServerId = _server.Id,
            UserId = userId,
            RoleId = Guid.NewGuid()
        });
        _db.SaveChanges();
    }

    // --- GetServerPresence ---

    [Fact]
    public async Task GetServerPresence_AsMember_ReturnsOnlineMembers()
    {
        AddServerMember(_testUser.Id);
        AddServerMember(_otherUser.Id);
        _tracker.Connect(_otherUser.Id, "conn-1");

        var result = await _controller.GetServerPresence(_server.Id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetServerPresence_NotMember_ReturnsForbid()
    {
        var result = await _controller.GetServerPresence(_server.Id);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetServerPresence_AllOffline_ReturnsEmptyList()
    {
        AddServerMember(_testUser.Id);
        AddServerMember(_otherUser.Id);
        // No connections in tracker — all offline

        var result = await _controller.GetServerPresence(_server.Id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().Be(0);
    }

    [Fact]
    public async Task GetServerPresence_OnlyOnlineMembersReturned()
    {
        AddServerMember(_testUser.Id);
        AddServerMember(_otherUser.Id);
        // Only otherUser is online
        _tracker.Connect(_otherUser.Id, "conn-1");

        var result = await _controller.GetServerPresence(_server.Id);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        // testUser is offline, otherUser is online
        list!.Count.Should().Be(1);
    }

    // --- GetDmPresence ---

    [Fact]
    public async Task GetDmPresence_WithOnlineContact_ReturnsPresence()
    {
        var dmChannel = new DmChannel();
        _db.DmChannels.Add(dmChannel);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = dmChannel, UserId = _testUser.Id });
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = dmChannel, UserId = _otherUser.Id });
        await _db.SaveChangesAsync();
        _tracker.Connect(_otherUser.Id, "conn-1");

        var result = await _controller.GetDmPresence();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetDmPresence_NoDmChannels_ReturnsEmpty()
    {
        var result = await _controller.GetDmPresence();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list.Should().NotBeNull();
        list!.Count.Should().Be(0);
    }

    [Fact]
    public async Task GetDmPresence_ContactOffline_ReturnsEmpty()
    {
        var dmChannel = new DmChannel();
        _db.DmChannels.Add(dmChannel);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = dmChannel, UserId = _testUser.Id });
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = dmChannel, UserId = _otherUser.Id });
        await _db.SaveChangesAsync();
        // No connections — otherUser is offline

        var result = await _controller.GetDmPresence();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list!.Count.Should().Be(0);
    }

    [Fact]
    public async Task GetDmPresence_MultipleChannels_ReturnsDistinctContacts()
    {
        var thirdUser = new User { Id = Guid.NewGuid(), GoogleSubject = "g-3", DisplayName = "Third" };
        _db.Users.Add(thirdUser);

        var dm1 = new DmChannel();
        _db.DmChannels.Add(dm1);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = dm1, UserId = _testUser.Id });
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = dm1, UserId = _otherUser.Id });

        var dm2 = new DmChannel();
        _db.DmChannels.Add(dm2);
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = dm2, UserId = _testUser.Id });
        _db.DmChannelMembers.Add(new DmChannelMember { DmChannel = dm2, UserId = thirdUser.Id });

        await _db.SaveChangesAsync();
        _tracker.Connect(_otherUser.Id, "conn-1");
        _tracker.Connect(thirdUser.Id, "conn-2");

        var result = await _controller.GetDmPresence();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var list = ok.Value as System.Collections.IList;
        list!.Count.Should().Be(2);
    }
}
