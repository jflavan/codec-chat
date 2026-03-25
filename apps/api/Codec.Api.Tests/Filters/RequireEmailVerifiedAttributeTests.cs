using System.Security.Claims;
using Codec.Api.Data;
using Codec.Api.Filters;
using Codec.Api.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Codec.Api.Tests.Filters;

public class RequireEmailVerifiedAttributeTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly RequireEmailVerifiedAttribute _filter = new();

    public RequireEmailVerifiedAttributeTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private ActionExecutingContext CreateContext(ClaimsPrincipal? user = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _db);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
            User = user ?? new ClaimsPrincipal(new ClaimsIdentity())
        };

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());
    }

    private static ActionExecutionDelegate CreateNextDelegate()
    {
        return () => Task.FromResult<ActionExecutedContext>(null!);
    }

    [Fact]
    public async Task UnauthenticatedUser_ProceedsToNext()
    {
        var context = CreateContext();
        var nextCalled = false;

        await _filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task GoogleUser_ProceedsToNext()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("iss", "https://accounts.google.com")
        ], "Bearer"));

        var context = CreateContext(user);
        var nextCalled = false;

        await _filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task GoogleUserAlternateIssuer_ProceedsToNext()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("iss", "accounts.google.com")
        ], "Bearer"));

        var context = CreateContext(user);
        var nextCalled = false;

        await _filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task VerifiedLocalUser_ProceedsToNext()
    {
        var userId = Guid.NewGuid();
        _db.Users.Add(new User { Id = userId, DisplayName = "Test", EmailVerified = true });
        await _db.SaveChangesAsync();

        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", userId.ToString()),
            new Claim("iss", "codec-api")
        ], "Bearer"));

        var context = CreateContext(user);
        var nextCalled = false;

        await _filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task UnverifiedLocalUser_Returns403()
    {
        var userId = Guid.NewGuid();
        _db.Users.Add(new User { Id = userId, DisplayName = "Test", EmailVerified = false });
        await _db.SaveChangesAsync();

        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", userId.ToString()),
            new Claim("iss", "codec-api")
        ], "Bearer"));

        var context = CreateContext(user);

        await _filter.OnActionExecutionAsync(context, CreateNextDelegate());

        context.Result.Should().NotBeNull();
        context.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)context.Result!;
        objectResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvalidSubClaim_ProceedsToNext()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "not-a-guid"),
            new Claim("iss", "codec-api")
        ], "Bearer"));

        var context = CreateContext(user);
        var nextCalled = false;

        await _filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task MissingSubClaim_ProceedsToNext()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("iss", "codec-api")
        ], "Bearer"));

        var context = CreateContext(user);
        var nextCalled = false;

        await _filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task UserNotFoundInDb_ProceedsToNext()
    {
        var userId = Guid.NewGuid(); // Not in DB
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", userId.ToString()),
            new Claim("iss", "codec-api")
        ], "Bearer"));

        var context = CreateContext(user);
        var nextCalled = false;

        await _filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        nextCalled.Should().BeTrue();
    }
}
