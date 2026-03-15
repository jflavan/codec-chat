using Codec.Api.Controllers;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class AuthControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly TokenService _tokenService;
    private readonly Mock<IAvatarService> _avatarService = new();
    private readonly IConfiguration _config;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "super-secret-key-that-is-at-least-32-chars-long!!",
                ["Jwt:Issuer"] = "codec-api",
                ["Jwt:Audience"] = "codec-api",
                ["Jwt:ExpiryMinutes"] = "60"
            })
            .Build();

        _tokenService = new TokenService(_config, _db);
        _avatarService.Setup(a => a.ResolveUrl(It.IsAny<string?>())).Returns((string?)null);

        _controller = new AuthController(_db, _tokenService, _avatarService.Object, _config);
    }

    public void Dispose() => _db.Dispose();

    private async Task<User> CreateUserWithPassword(string email = "user@test.com", string password = "P@ssword123!", string nickname = "TestUser")
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        var user = new User
        {
            Email = email,
            PasswordHash = hash,
            DisplayName = nickname,
            Nickname = nickname,
            GoogleSubject = null
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    // --- Register ---

    [Fact]
    public async Task Register_CreatesUserWithHashedPassword_Returns201WithTokens()
    {
        var request = new RegisterRequest
        {
            Email = "new@test.com",
            Password = "StrongPass1!",
            Nickname = "NewUser"
        };

        var result = await _controller.Register(request);

        var created = result.Should().BeOfType<CreatedResult>().Subject;
        created.Value.Should().NotBeNull();

        // Verify user was created in DB with hashed password
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == "new@test.com");
        user.Should().NotBeNull();
        user!.PasswordHash.Should().NotBeNullOrWhiteSpace();
        user.PasswordHash.Should().NotBe("StrongPass1!"); // hashed, not plaintext
        BCrypt.Net.BCrypt.Verify("StrongPass1!", user.PasswordHash).Should().BeTrue();
        user.DisplayName.Should().Be("NewUser");
        user.Nickname.Should().Be("NewUser");
        user.GoogleSubject.Should().BeNull();
    }

    [Fact]
    public async Task Register_Returns409WhenEmailAlreadyExists()
    {
        await CreateUserWithPassword("taken@test.com");

        var request = new RegisterRequest
        {
            Email = "taken@test.com",
            Password = "AnotherPass1!",
            Nickname = "Another"
        };

        var result = await _controller.Register(request);
        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Register_NormalizesEmailToLowercase()
    {
        var request = new RegisterRequest
        {
            Email = "USER@TEST.COM",
            Password = "StrongPass1!",
            Nickname = "User"
        };

        await _controller.Register(request);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == "user@test.com");
        user.Should().NotBeNull();
    }

    // --- Login ---

    [Fact]
    public async Task Login_Returns200WithTokensForValidCredentials()
    {
        await CreateUserWithPassword("login@test.com", "Correct123!");

        var request = new LoginRequest { Email = "login@test.com", Password = "Correct123!" };
        var result = await _controller.Login(request);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Login_Returns401ForWrongPassword()
    {
        await CreateUserWithPassword("wrong@test.com", "Correct123!");

        var request = new LoginRequest { Email = "wrong@test.com", Password = "WrongPass!" };
        var result = await _controller.Login(request);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_Returns401ForNonExistentEmail()
    {
        var request = new LoginRequest { Email = "ghost@test.com", Password = "AnyPass1!" };
        var result = await _controller.Login(request);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_Returns401ForGoogleOnlyAccount()
    {
        // Google-only user has no PasswordHash
        var googleUser = new User
        {
            Email = "google@test.com",
            GoogleSubject = "google-123",
            DisplayName = "Google User",
            PasswordHash = null
        };
        _db.Users.Add(googleUser);
        await _db.SaveChangesAsync();

        var request = new LoginRequest { Email = "google@test.com", Password = "AnyPass1!" };
        var result = await _controller.Login(request);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_NormalizesEmailToLowercase()
    {
        await CreateUserWithPassword("normalize@test.com", "Pass1234!");

        var request = new LoginRequest { Email = "NORMALIZE@TEST.COM", Password = "Pass1234!" };
        var result = await _controller.Login(request);

        result.Should().BeOfType<OkObjectResult>();
    }

    // --- Refresh ---

    [Fact]
    public async Task Refresh_ReturnsNewTokenPairForValidRefreshToken()
    {
        var user = await CreateUserWithPassword("refresh@test.com");
        var (opaqueToken, _) = await _tokenService.GenerateRefreshTokenAsync(user);

        var request = new RefreshRequest { RefreshToken = opaqueToken };
        var result = await _controller.Refresh(request);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Refresh_Returns401ForInvalidRefreshToken()
    {
        var request = new RefreshRequest { RefreshToken = "totally-invalid-token" };
        var result = await _controller.Refresh(request);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Refresh_Returns401ForExpiredRefreshToken()
    {
        var user = await CreateUserWithPassword("expired@test.com");
        var (opaqueToken, entity) = await _tokenService.GenerateRefreshTokenAsync(user);
        entity.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();

        var request = new RefreshRequest { RefreshToken = opaqueToken };
        var result = await _controller.Refresh(request);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Refresh_RevokesOldRefreshToken()
    {
        var user = await CreateUserWithPassword("rotate@test.com");
        var (opaqueToken, entity) = await _tokenService.GenerateRefreshTokenAsync(user);

        var request = new RefreshRequest { RefreshToken = opaqueToken };
        await _controller.Refresh(request);

        // Reload the entity to check revocation
        var oldToken = await _db.RefreshTokens.FindAsync(entity.Id);
        oldToken!.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Refresh_RevokedTokenCannotBeReused()
    {
        var user = await CreateUserWithPassword("reuse@test.com");
        var (opaqueToken, _) = await _tokenService.GenerateRefreshTokenAsync(user);

        // First refresh should succeed
        var result1 = await _controller.Refresh(new RefreshRequest { RefreshToken = opaqueToken });
        result1.Should().BeOfType<OkObjectResult>();

        // Second use of the same token should fail (it was revoked during rotation)
        var result2 = await _controller.Refresh(new RefreshRequest { RefreshToken = opaqueToken });
        result2.Should().BeOfType<UnauthorizedObjectResult>();
    }
}
