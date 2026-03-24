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

public class AvatarsControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IAvatarService> _avatarService = new();
    private readonly AvatarsController _controller;
    private readonly User _testUser;

    public AvatarsControllerTests()
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
            AvatarUrl = "https://google.com/pic.jpg"
        };
        _db.Users.Add(_testUser);
        _db.SaveChanges();

        _controller = new AvatarsController(_db, _userService.Object, _avatarService.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("sub", "google-test"), new Claim("name", "Test User")
                ], "Bearer"))
            }
        };

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_db.Users.First(u => u.Id == _testUser.Id), false));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task UploadUserAvatar_Invalid_ReturnsBadRequest()
    {
        var file = new Mock<IFormFile>();
        _avatarService.Setup(a => a.Validate(file.Object)).Returns("File is empty.");

        var result = await _controller.UploadUserAvatar(file.Object);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UploadUserAvatar_Valid_ReturnsOk()
    {
        var file = new Mock<IFormFile>();
        _avatarService.Setup(a => a.Validate(file.Object)).Returns((string?)null);
        _avatarService.Setup(a => a.SaveUserAvatarAsync(_testUser.Id, file.Object))
            .ReturnsAsync("https://storage/avatar.png");

        var result = await _controller.UploadUserAvatar(file.Object);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task DeleteUserAvatar_NoCustomAvatar_ReturnsFallback()
    {
        var user = _db.Users.First(u => u.Id == _testUser.Id);
        user.CustomAvatarPath = null;
        await _db.SaveChangesAsync();

        var result = await _controller.DeleteUserAvatar();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task DeleteUserAvatar_WithCustomAvatar_DeletesAndReturnsFallback()
    {
        var user = _db.Users.First(u => u.Id == _testUser.Id);
        user.CustomAvatarPath = "https://storage/old-avatar.png";
        await _db.SaveChangesAsync();

        _avatarService.Setup(a => a.DeleteUserAvatarAsync(_testUser.Id)).Returns(Task.CompletedTask);

        var result = await _controller.DeleteUserAvatar();
        result.Should().BeOfType<OkObjectResult>();
        _avatarService.Verify(a => a.DeleteUserAvatarAsync(_testUser.Id), Times.Once);
    }

    [Fact]
    public async Task UploadServerAvatar_Valid_ReturnsOk()
    {
        var server = new Server { Name = "S" };
        _db.Servers.Add(server);
        var memberRole = new ServerRoleEntity { ServerId = server.Id, Name = "Member", Position = 2, Permissions = PermissionExtensions.MemberDefaults, IsSystemRole = true };
        _db.ServerRoles.Add(memberRole);
        _db.ServerMembers.Add(new ServerMember { Server = server, UserId = _testUser.Id, RoleId = memberRole.Id });
        await _db.SaveChangesAsync();

        var file = new Mock<IFormFile>();
        _avatarService.Setup(a => a.Validate(file.Object)).Returns((string?)null);
        _avatarService.Setup(a => a.SaveServerAvatarAsync(_testUser.Id, server.Id, file.Object))
            .ReturnsAsync("https://storage/server-avatar.png");
        _userService.Setup(u => u.EnsureMemberAsync(server.Id, _testUser.Id, false))
            .ReturnsAsync(new ServerMember { ServerId = server.Id, UserId = _testUser.Id });

        var result = await _controller.UploadServerAvatar(server.Id, file.Object);
        result.Should().BeOfType<OkObjectResult>();
    }
}
