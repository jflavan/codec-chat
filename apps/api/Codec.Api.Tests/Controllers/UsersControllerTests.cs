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
using Moq;

namespace Codec.Api.Tests.Controllers;

public class UsersControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IAvatarService> _avatarService = new();
    private readonly Mock<IHubContext<ChatHub>> _hub = new();
    private readonly UsersController _controller;
    private readonly User _testUser;

    public UsersControllerTests()
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
            AvatarUrl = "https://google.com/pic.jpg"
        };

        _controller = new UsersController(_userService.Object, _avatarService.Object, _db, _hub.Object);
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

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync((_testUser, false));
        _userService.Setup(u => u.GetEffectiveDisplayName(_testUser)).Returns("Test User");
        _avatarService.Setup(a => a.ResolveUrl(It.IsAny<string?>())).Returns((string?)null);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Me_ReturnsOkWithUserProfile()
    {
        var result = await _controller.Me();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SetNickname_ValidNickname_ReturnsOk()
    {
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));
        _userService.Setup(u => u.GetEffectiveDisplayName(It.IsAny<User>())).Returns("Nicky");

        var result = await _controller.SetNickname(new SetNicknameRequest { Nickname = "Nicky" });
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SetNickname_EmptyNickname_ReturnsBadRequest()
    {
        var result = await _controller.SetNickname(new SetNicknameRequest { Nickname = "" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SetNickname_NullNickname_ReturnsBadRequest()
    {
        var result = await _controller.SetNickname(new SetNicknameRequest { Nickname = null });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RemoveNickname_NoNickname_ReturnsNotFound()
    {
        _testUser.Nickname = null;
        var result = await _controller.RemoveNickname();
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task RemoveNickname_HasNickname_ReturnsOk()
    {
        _testUser.Nickname = "OldNick";
        _db.Users.Add(_testUser);
        await _db.SaveChangesAsync();
        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));

        var result = await _controller.RemoveNickname();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SearchUsers_ShortQuery_ReturnsEmpty()
    {
        var result = await _controller.SearchUsers("a");
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<Array>();
    }

    [Fact]
    public async Task SearchUsers_NullQuery_ReturnsEmpty()
    {
        var result = await _controller.SearchUsers(null);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<Array>();
    }
}
