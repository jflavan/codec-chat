using System.Security.Claims;
using Codec.Api.Data;
using Codec.Api.Filters;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Codec.Api.Tests.Filters;

public class GlobalAdminHandlerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IUserService> _userService = new();
    private readonly GlobalAdminHandler _handler;

    public GlobalAdminHandlerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);
        _handler = new GlobalAdminHandler(_userService.Object);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Succeeds_WhenUserIsGlobalAdmin()
    {
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Admin", IsGlobalAdmin = true };
        _userService.Setup(u => u.ResolveUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test")], "Bearer"));
        var context = new AuthorizationHandlerContext([new GlobalAdminRequirement()], principal, null);

        await _handler.HandleAsync(context);
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_WhenUserIsNotGlobalAdmin()
    {
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Regular", IsGlobalAdmin = false };
        _userService.Setup(u => u.ResolveUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test")], "Bearer"));
        var context = new AuthorizationHandlerContext([new GlobalAdminRequirement()], principal, null);

        await _handler.HandleAsync(context);
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Fails_WhenGlobalAdminIsDisabled()
    {
        var user = new User { Id = Guid.NewGuid(), DisplayName = "Disabled Admin", IsGlobalAdmin = true, IsDisabled = true };
        _userService.Setup(u => u.ResolveUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test")], "Bearer"));
        var context = new AuthorizationHandlerContext([new GlobalAdminRequirement()], principal, null);

        await _handler.HandleAsync(context);
        context.HasSucceeded.Should().BeFalse();
    }
}
