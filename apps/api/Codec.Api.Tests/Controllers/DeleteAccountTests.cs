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
using Moq;

namespace Codec.Api.Tests.Controllers;

public class DeleteAccountTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<IAvatarService> _avatarService = new();
    private readonly Mock<IHubContext<ChatHub>> _hub = new();
    private readonly Mock<IConfiguration> _config = new();
    private readonly UsersController _controller;
    private readonly User _testUser;

    public DeleteAccountTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _testUser = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "Test User",
            Email = "test@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123", workFactor: 4)
        };

        _db.Users.Add(_testUser);
        _db.SaveChanges();

        _controller = new UsersController(_userService.Object, _avatarService.Object, _db, _hub.Object, _config.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("iss", "codec-api"),
                    new Claim("sub", _testUser.Id.ToString())
                ], "Bearer"))
            }
        };

        _userService.Setup(u => u.GetOrCreateUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((_testUser, false));
        _userService.Setup(u => u.GetOwnedServersAsync(_testUser.Id))
            .ReturnsAsync(new List<(Guid, string)>());

        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(mockClientProxy.Object);
        _hub.Setup(h => h.Clients).Returns(mockClients.Object);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task DeleteAccount_WrongConfirmationText_Returns400()
    {
        var request = new DeleteAccountRequest
        {
            Password = "password123",
            ConfirmationText = "WRONG"
        };

        var result = await _controller.DeleteAccount(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeleteAccount_OwnsServer_Returns400WithServerList()
    {
        var serverId = Guid.NewGuid();
        _userService.Setup(u => u.GetOwnedServersAsync(_testUser.Id))
            .ReturnsAsync(new List<(Guid, string)> { (serverId, "My Server") });

        var request = new DeleteAccountRequest
        {
            Password = "password123",
            ConfirmationText = "DELETE"
        };

        var result = await _controller.DeleteAccount(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeleteAccount_WrongPassword_Returns401()
    {
        var request = new DeleteAccountRequest
        {
            Password = "wrongpassword",
            ConfirmationText = "DELETE"
        };

        var result = await _controller.DeleteAccount(request);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task DeleteAccount_MissingPassword_Returns400()
    {
        var request = new DeleteAccountRequest
        {
            ConfirmationText = "DELETE"
        };

        var result = await _controller.DeleteAccount(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeleteAccount_CorrectPassword_ReturnsOkAndCallsDelete()
    {
        var request = new DeleteAccountRequest
        {
            Password = "password123",
            ConfirmationText = "DELETE"
        };

        var result = await _controller.DeleteAccount(request);

        result.Should().BeOfType<OkObjectResult>();
        _userService.Verify(u => u.DeleteAccountAsync(_testUser.Id), Times.Once);
    }
}
