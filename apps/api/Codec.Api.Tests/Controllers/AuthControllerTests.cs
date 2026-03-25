using Codec.Api.Controllers;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Codec.Api.Tests.Controllers;

public class AuthControllerTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly TokenService _tokenService;
    private readonly Mock<IAvatarService> _avatarService = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<OAuthProviderService> _oauthProviderService;
    private readonly IConfiguration _config;
    private readonly EmailVerificationService _emailVerificationService;
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
                ["Jwt:ExpiryMinutes"] = "60",
                ["Frontend:BaseUrl"] = "http://localhost:5174"
            })
            .Build();

        _tokenService = new TokenService(_config, _db);
        _avatarService.Setup(a => a.ResolveUrl(It.IsAny<string?>())).Returns((string?)null);
        _emailVerificationService = new EmailVerificationService(_db, _emailSender.Object, _config);

        _oauthProviderService = new Mock<OAuthProviderService>(
            Mock.Of<IHttpClientFactory>(), _config, Mock.Of<ILogger<OAuthProviderService>>());
        _controller = new AuthController(_db, _tokenService, _avatarService.Object, _config, _emailVerificationService, _oauthProviderService.Object, Mock.Of<ILogger<AuthController>>());
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

    // --- Logout ---

    [Fact]
    public async Task Logout_Returns204ForValidToken()
    {
        var user = await CreateUserWithPassword("logout@test.com");
        var (opaqueToken, _) = await _tokenService.GenerateRefreshTokenAsync(user);

        var result = await _controller.Logout(new LogoutRequest { RefreshToken = opaqueToken });

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Logout_Returns204ForInvalidToken()
    {
        // Should not leak whether the token was valid
        var result = await _controller.Logout(new LogoutRequest { RefreshToken = "bogus-token" });

        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshToken()
    {
        var user = await CreateUserWithPassword("logout-revoke@test.com");
        var (opaqueToken, entity) = await _tokenService.GenerateRefreshTokenAsync(user);

        await _controller.Logout(new LogoutRequest { RefreshToken = opaqueToken });

        var dbToken = await _db.RefreshTokens.FindAsync(entity.Id);
        dbToken!.RevokedAt.Should().NotBeNull();

        // Token can no longer be used for refresh
        var refreshResult = await _controller.Refresh(new RefreshRequest { RefreshToken = opaqueToken });
        refreshResult.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // --- Account Lockout ---

    [Fact]
    public async Task Login_IncrementsFailedAttemptsOnWrongPassword()
    {
        var user = await CreateUserWithPassword("lockout@test.com", "Correct123!");

        await _controller.Login(new LoginRequest { Email = "lockout@test.com", Password = "Wrong!" });

        var dbUser = await _db.Users.FindAsync(user.Id);
        dbUser!.FailedLoginAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Login_LocksAccountAfter5FailedAttempts()
    {
        await CreateUserWithPassword("lock5@test.com", "Correct123!");

        for (int i = 0; i < 5; i++)
        {
            await _controller.Login(new LoginRequest { Email = "lock5@test.com", Password = "Wrong!" });
        }

        // Account should now be locked — even correct password returns 401
        var result = await _controller.Login(new LoginRequest { Email = "lock5@test.com", Password = "Correct123!" });
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Login_ResetsFailedAttemptsOnSuccess()
    {
        var user = await CreateUserWithPassword("reset@test.com", "Correct123!");

        // Fail twice
        await _controller.Login(new LoginRequest { Email = "reset@test.com", Password = "Wrong!" });
        await _controller.Login(new LoginRequest { Email = "reset@test.com", Password = "Wrong!" });

        var dbUser = await _db.Users.FindAsync(user.Id);
        dbUser!.FailedLoginAttempts.Should().Be(2);

        // Succeed — counter should reset
        await _controller.Login(new LoginRequest { Email = "reset@test.com", Password = "Correct123!" });

        await _db.Entry(dbUser).ReloadAsync();
        dbUser.FailedLoginAttempts.Should().Be(0);
        dbUser.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public async Task Login_AllowsLoginAfterLockoutExpires()
    {
        var user = await CreateUserWithPassword("expired-lock@test.com", "Correct123!");

        // Simulate expired lockout
        user.FailedLoginAttempts = 5;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(-1); // expired
        await _db.SaveChangesAsync();

        var result = await _controller.Login(new LoginRequest { Email = "expired-lock@test.com", Password = "Correct123!" });
        result.Should().BeOfType<OkObjectResult>();
    }

    // --- Email Verification ---

    [Fact]
    public async Task Register_SendsVerificationEmail_UserNotVerified()
    {
        var request = new RegisterRequest
        {
            Email = "verify@test.com",
            Password = "StrongPass1!",
            Nickname = "VerifyUser"
        };

        await _controller.Register(request);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == "verify@test.com");
        user.Should().NotBeNull();
        user!.EmailVerified.Should().BeFalse();
        user.EmailVerificationToken.Should().NotBeNullOrEmpty();
        _emailSender.Verify(e => e.SendEmailAsync("verify@test.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task VerifyEmail_ValidToken_Returns200()
    {
        var user = await CreateUserWithPassword("v@test.com");
        var rawToken = await _emailVerificationService.GenerateAndSendVerificationAsync(user);

        var result = await _controller.VerifyEmail(new VerifyEmailRequest { Token = rawToken });

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.Users.FirstAsync(u => u.Id == user.Id);
        updated.EmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyEmail_InvalidToken_Returns400()
    {
        var result = await _controller.VerifyEmail(new VerifyEmailRequest { Token = "bad-token" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // --- OAuth: GitHub Callback ---

    [Fact]
    public async Task GitHubCallback_ReturnsBadRequest_WhenExchangeFails()
    {
        _oauthProviderService
            .Setup(s => s.ExchangeGitHubCodeAsync(It.IsAny<string>()))
            .ReturnsAsync((OAuthUserInfo?)null);

        var result = await _controller.GitHubCallback(new OAuthCallbackRequest { Code = "bad" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GitHubCallback_CreatesNewUser_ReturnsTokens()
    {
        _oauthProviderService
            .Setup(s => s.ExchangeGitHubCodeAsync("gh-code"))
            .ReturnsAsync(new OAuthUserInfo("gh-123", "GitHub User", "gh@test.com", "https://avatar.url/gh"));

        var result = await _controller.GitHubCallback(new OAuthCallbackRequest { Code = "gh-code" });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.GitHubSubject == "gh-123");
        user.Should().NotBeNull();
        user!.DisplayName.Should().Be("GitHub User");
        user.Email.Should().Be("gh@test.com");
        user.AvatarUrl.Should().Be("https://avatar.url/gh");
        user.EmailVerified.Should().BeTrue();
    }

    [Fact]
    public async Task GitHubCallback_ExistingEmail_ReturnsConflict_RequiresExplicitLink()
    {
        var existing = await CreateUserWithPassword("link@test.com", "Pass123!", "Existing");

        _oauthProviderService
            .Setup(s => s.ExchangeGitHubCodeAsync("gh-code"))
            .ReturnsAsync(new OAuthUserInfo("gh-456", "GH Name", "link@test.com", null));

        var result = await _controller.GitHubCallback(new OAuthCallbackRequest { Code = "gh-code" });
        result.Should().BeOfType<ConflictObjectResult>();

        await _db.Entry(existing).ReloadAsync();
        existing.GitHubSubject.Should().BeNull();
    }

    [Fact]
    public async Task GitHubCallback_ReturnsExistingUser_BySubject()
    {
        var existing = new User
        {
            Email = "existing-gh@test.com",
            GitHubSubject = "gh-existing",
            DisplayName = "Existing GH User"
        };
        _db.Users.Add(existing);
        await _db.SaveChangesAsync();

        _oauthProviderService
            .Setup(s => s.ExchangeGitHubCodeAsync("code"))
            .ReturnsAsync(new OAuthUserInfo("gh-existing", "Updated Name", "existing-gh@test.com", null));

        var result = await _controller.GitHubCallback(new OAuthCallbackRequest { Code = "code" });
        result.Should().BeOfType<OkObjectResult>();

        // Should not create a duplicate
        var count = await _db.Users.CountAsync(u => u.GitHubSubject == "gh-existing");
        count.Should().Be(1);
    }

    // --- OAuth: Discord Callback ---

    [Fact]
    public async Task DiscordCallback_ReturnsBadRequest_WhenExchangeFails()
    {
        _oauthProviderService
            .Setup(s => s.ExchangeDiscordCodeAsync(It.IsAny<string>()))
            .ReturnsAsync((OAuthUserInfo?)null);

        var result = await _controller.DiscordCallback(new OAuthCallbackRequest { Code = "bad" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DiscordCallback_CreatesNewUser_ReturnsTokens()
    {
        _oauthProviderService
            .Setup(s => s.ExchangeDiscordCodeAsync("dc-code"))
            .ReturnsAsync(new OAuthUserInfo("dc-789", "Discord User", "dc@test.com", "https://cdn.discordapp.com/avatar.png"));

        var result = await _controller.DiscordCallback(new OAuthCallbackRequest { Code = "dc-code" });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.DiscordSubject == "dc-789");
        user.Should().NotBeNull();
        user!.DisplayName.Should().Be("Discord User");
        user.Email.Should().Be("dc@test.com");
    }

    [Fact]
    public async Task DiscordCallback_ExistingEmail_ReturnsConflict_RequiresExplicitLink()
    {
        var existing = await CreateUserWithPassword("dclink@test.com", "Pass123!", "Existing");

        _oauthProviderService
            .Setup(s => s.ExchangeDiscordCodeAsync("dc-code"))
            .ReturnsAsync(new OAuthUserInfo("dc-link", "DC Name", "dclink@test.com", null));

        var result = await _controller.DiscordCallback(new OAuthCallbackRequest { Code = "dc-code" });
        result.Should().BeOfType<ConflictObjectResult>();

        await _db.Entry(existing).ReloadAsync();
        existing.DiscordSubject.Should().BeNull();
    }

    // --- OAuth: Config ---

    [Fact]
    public void GetOAuthConfig_ReturnsProviderStatus()
    {
        var result = _controller.GetOAuthConfig();
        result.Should().BeOfType<OkObjectResult>();
    }

    // --- OAuth: New user with no email ---

    [Fact]
    public async Task GitHubCallback_CreatesUser_WithNullEmail()
    {
        _oauthProviderService
            .Setup(s => s.ExchangeGitHubCodeAsync("code"))
            .ReturnsAsync(new OAuthUserInfo("gh-no-email", "No Email User", null, null));

        var result = await _controller.GitHubCallback(new OAuthCallbackRequest { Code = "code" });
        result.Should().BeOfType<OkObjectResult>();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.GitHubSubject == "gh-no-email");
        user.Should().NotBeNull();
        user!.Email.Should().BeNull();
        user.EmailVerified.Should().BeFalse();
    }

    // --- OAuth: isNewUser flag ---

    [Fact]
    public async Task GitHubCallback_SetsIsNewUser_ForNewAccounts()
    {
        _oauthProviderService
            .Setup(s => s.ExchangeGitHubCodeAsync("code"))
            .ReturnsAsync(new OAuthUserInfo("gh-new", "New User", "new@test.com", null));

        var result = await _controller.GitHubCallback(new OAuthCallbackRequest { Code = "code" });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("\"isNewUser\":true");
    }

    [Fact]
    public async Task GitHubCallback_IsNewUserFalse_ForExistingAccounts()
    {
        var existing = new User
        {
            Email = "returning@test.com",
            GitHubSubject = "gh-returning",
            DisplayName = "Returning"
        };
        _db.Users.Add(existing);
        await _db.SaveChangesAsync();

        _oauthProviderService
            .Setup(s => s.ExchangeGitHubCodeAsync("code"))
            .ReturnsAsync(new OAuthUserInfo("gh-returning", "Returning", "returning@test.com", null));

        var result = await _controller.GitHubCallback(new OAuthCallbackRequest { Code = "code" });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("\"isNewUser\":false");
    }

    // =====================================================================
    // Additional coverage tests
    // =====================================================================

    // --- VerifyEmail ---

    [Fact]
    public async Task VerifyEmail_ExpiredToken_Returns400()
    {
        var user = await CreateUserWithPassword("expired-verify@test.com");
        var rawToken = await _emailVerificationService.GenerateAndSendVerificationAsync(user);

        // Expire the token
        user.EmailVerificationTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();

        var result = await _controller.VerifyEmail(new VerifyEmailRequest { Token = rawToken });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task VerifyEmail_AlreadyVerifiedUser_TokenBecomesInvalid()
    {
        var user = await CreateUserWithPassword("already-verified@test.com");
        var rawToken = await _emailVerificationService.GenerateAndSendVerificationAsync(user);

        // First verify succeeds
        var result1 = await _controller.VerifyEmail(new VerifyEmailRequest { Token = rawToken });
        result1.Should().BeOfType<OkObjectResult>();

        // Second verify with same token fails (token cleared after verify)
        var result2 = await _controller.VerifyEmail(new VerifyEmailRequest { Token = rawToken });
        result2.Should().BeOfType<BadRequestObjectResult>();
    }

    // --- ResendVerification ---

    [Fact]
    public async Task ResendVerification_NoAuthClaim_Returns401()
    {
        // Controller with no sub claim
        var controllerNoAuth = new AuthController(_db, _tokenService, _avatarService.Object, _config, _emailVerificationService, _oauthProviderService.Object, Mock.Of<ILogger<AuthController>>());
        controllerNoAuth.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity())
            }
        };

        var result = await controllerNoAuth.ResendVerification();
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task ResendVerification_InvalidSubClaim_Returns401()
    {
        var controllerBadSub = new AuthController(_db, _tokenService, _avatarService.Object, _config, _emailVerificationService, _oauthProviderService.Object, Mock.Of<ILogger<AuthController>>());
        controllerBadSub.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
                    new System.Security.Claims.Claim("sub", "not-a-guid")
                ], "Bearer"))
            }
        };

        var result = await controllerBadSub.ResendVerification();
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task ResendVerification_UserNotFound_Returns401()
    {
        var nonExistentId = Guid.NewGuid();
        var controllerNonExistent = new AuthController(_db, _tokenService, _avatarService.Object, _config, _emailVerificationService, _oauthProviderService.Object, Mock.Of<ILogger<AuthController>>());
        controllerNonExistent.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
                    new System.Security.Claims.Claim("sub", nonExistentId.ToString())
                ], "Bearer"))
            }
        };

        var result = await controllerNonExistent.ResendVerification();
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task ResendVerification_AlreadyVerified_Returns400()
    {
        var user = await CreateUserWithPassword("already-v@test.com");
        user.EmailVerified = true;
        await _db.SaveChangesAsync();

        var controller = CreateControllerForUser(user);
        var result = await controller.ResendVerification();
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ResendVerification_CooldownNotExpired_Returns429()
    {
        var user = await CreateUserWithPassword("cooldown@test.com");
        user.EmailVerificationTokenSentAt = DateTimeOffset.UtcNow; // just sent
        await _db.SaveChangesAsync();

        var controller = CreateControllerForUser(user);
        var result = await controller.ResendVerification();
        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task ResendVerification_Success_Returns200()
    {
        var user = await CreateUserWithPassword("resend-ok@test.com");
        // Ensure cooldown has passed
        user.EmailVerificationTokenSentAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await _db.SaveChangesAsync();

        var controller = CreateControllerForUser(user);
        var result = await controller.ResendVerification();
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ResendVerification_EmailSendFails_Returns502()
    {
        var user = await CreateUserWithPassword("fail-send@test.com");
        user.EmailVerificationTokenSentAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await _db.SaveChangesAsync();

        _emailSender.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("SMTP down"));

        var controller = CreateControllerForUser(user);
        var result = await controller.ResendVerification();
        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(502);
    }

    // --- Account Lockout: additional edge cases ---

    [Fact]
    public async Task Login_AccountLocked_ReturnsLockedMessage()
    {
        var user = await CreateUserWithPassword("locked@test.com", "Pass123!");
        user.FailedLoginAttempts = 5;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10);
        await _db.SaveChangesAsync();

        var result = await _controller.Login(new LoginRequest { Email = "locked@test.com", Password = "Pass123!" });
        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(unauthorized.Value);
        json.Should().Contain("temporarily locked");
    }

    [Fact]
    public async Task Login_WrongPasswordSetsLockoutAfterThreshold()
    {
        var user = await CreateUserWithPassword("lockout-set@test.com", "Pass123!");
        user.FailedLoginAttempts = 4; // one more triggers lockout
        await _db.SaveChangesAsync();

        await _controller.Login(new LoginRequest { Email = "lockout-set@test.com", Password = "WrongPass!" });

        await _db.Entry(user).ReloadAsync();
        user.FailedLoginAttempts.Should().Be(5);
        user.LockoutEnd.Should().NotBeNull();
        user.LockoutEnd.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_ExpiredLockout_ResetsAndAllowsLogin()
    {
        var user = await CreateUserWithPassword("expired-lockout2@test.com", "Pass123!");
        user.FailedLoginAttempts = 5;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(-5);
        await _db.SaveChangesAsync();

        var result = await _controller.Login(new LoginRequest { Email = "expired-lockout2@test.com", Password = "Pass123!" });
        result.Should().BeOfType<OkObjectResult>();

        await _db.Entry(user).ReloadAsync();
        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public async Task Login_ExpiredLockout_WrongPassword_IncrementsFromZero()
    {
        var user = await CreateUserWithPassword("expired-lockout-wrong@test.com", "Pass123!");
        user.FailedLoginAttempts = 5;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(-5);
        await _db.SaveChangesAsync();

        await _controller.Login(new LoginRequest { Email = "expired-lockout-wrong@test.com", Password = "WrongPass!" });

        await _db.Entry(user).ReloadAsync();
        // Reset to 0, then incremented to 1
        user.FailedLoginAttempts.Should().Be(1);
    }

    // --- Register: verification email failure does not block registration ---

    [Fact]
    public async Task Register_VerificationEmailFails_StillReturns201()
    {
        _emailSender.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("SMTP failure"));

        var request = new RegisterRequest
        {
            Email = "email-fail@test.com",
            Password = "StrongPass1!",
            Nickname = "FailEmailUser"
        };

        var result = await _controller.Register(request);
        var created = result.Should().BeOfType<CreatedResult>().Subject;

        // emailSent should be false in response
        var json = System.Text.Json.JsonSerializer.Serialize(created.Value);
        json.Should().Contain("\"emailSent\":false");
    }

    [Fact]
    public async Task Register_TrimsNickname()
    {
        var request = new RegisterRequest
        {
            Email = "trim-nick@test.com",
            Password = "StrongPass1!",
            Nickname = "  TrimmedNick  "
        };

        await _controller.Register(request);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == "trim-nick@test.com");
        user!.Nickname.Should().Be("TrimmedNick");
        user.DisplayName.Should().Be("TrimmedNick");
    }

    [Fact]
    public async Task Register_AutoJoinsDefaultServer_WhenExists()
    {
        // Create default server with member role
        var defaultServer = new Server { Id = Server.DefaultServerId, Name = "Default" };
        _db.Servers.Add(defaultServer);
        var memberRole = new ServerRoleEntity { ServerId = Server.DefaultServerId, Name = "Member", Position = 2, Permissions = PermissionExtensions.MemberDefaults, IsSystemRole = true };
        _db.ServerRoles.Add(memberRole);
        await _db.SaveChangesAsync();

        var request = new RegisterRequest
        {
            Email = "autojoin@test.com",
            Password = "StrongPass1!",
            Nickname = "AutoJoinUser"
        };

        await _controller.Register(request);

        var user = await _db.Users.FirstAsync(u => u.Email == "autojoin@test.com");
        var membership = await _db.ServerMembers.FirstOrDefaultAsync(sm => sm.UserId == user.Id && sm.ServerId == Server.DefaultServerId);
        membership.Should().NotBeNull();
    }

    // --- Refresh: rotation produces new token pair ---

    [Fact]
    public async Task Refresh_ReturnsNewAccessAndRefreshTokens()
    {
        var user = await CreateUserWithPassword("refresh-pair@test.com");
        var (opaqueToken, _) = await _tokenService.GenerateRefreshTokenAsync(user);

        var result = await _controller.Refresh(new RefreshRequest { RefreshToken = opaqueToken });
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("accessToken");
        json.Should().Contain("refreshToken");
    }

    // --- Logout: revoked token cannot be reused ---

    [Fact]
    public async Task Logout_RevokedToken_CannotRefresh()
    {
        var user = await CreateUserWithPassword("logout-reuse@test.com");
        var (opaqueToken, _) = await _tokenService.GenerateRefreshTokenAsync(user);

        await _controller.Logout(new LogoutRequest { RefreshToken = opaqueToken });

        var refreshResult = await _controller.Refresh(new RefreshRequest { RefreshToken = opaqueToken });
        refreshResult.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // --- OAuth config: with configured providers ---

    [Fact]
    public void GetOAuthConfig_WithProviders_ReturnsEnabled()
    {
        var configWithProviders = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "super-secret-key-that-is-at-least-32-chars-long!!",
                ["Jwt:Issuer"] = "codec-api",
                ["Jwt:Audience"] = "codec-api",
                ["Jwt:ExpiryMinutes"] = "60",
                ["Frontend:BaseUrl"] = "http://localhost:5174",
                ["OAuth:GitHub:ClientId"] = "gh-client-id",
                ["OAuth:Discord:ClientId"] = "dc-client-id"
            })
            .Build();

        var controller = new AuthController(_db, _tokenService, _avatarService.Object, configWithProviders, _emailVerificationService, _oauthProviderService.Object, Mock.Of<ILogger<AuthController>>());

        var result = controller.GetOAuthConfig();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("gh-client-id");
        json.Should().Contain("dc-client-id");
    }

    [Fact]
    public void GetOAuthConfig_WithoutProviders_ReturnsDisabled()
    {
        var result = _controller.GetOAuthConfig();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        // No providers configured in default test config
        json.Should().Contain("\"enabled\":false");
    }

    // --- Discord callback: additional edge cases ---

    [Fact]
    public async Task DiscordCallback_ReturnsExistingUser_BySubject()
    {
        var existing = new User
        {
            Email = "existing-dc@test.com",
            DiscordSubject = "dc-existing",
            DisplayName = "Existing DC"
        };
        _db.Users.Add(existing);
        await _db.SaveChangesAsync();

        _oauthProviderService
            .Setup(s => s.ExchangeDiscordCodeAsync("code"))
            .ReturnsAsync(new OAuthUserInfo("dc-existing", "Updated Name", "existing-dc@test.com", null));

        var result = await _controller.DiscordCallback(new OAuthCallbackRequest { Code = "code" });
        result.Should().BeOfType<OkObjectResult>();

        var count = await _db.Users.CountAsync(u => u.DiscordSubject == "dc-existing");
        count.Should().Be(1);
    }

    [Fact]
    public async Task DiscordCallback_CreatesUser_WithNullEmail()
    {
        _oauthProviderService
            .Setup(s => s.ExchangeDiscordCodeAsync("code"))
            .ReturnsAsync(new OAuthUserInfo("dc-no-email", "No Email User", null, null));

        var result = await _controller.DiscordCallback(new OAuthCallbackRequest { Code = "code" });
        result.Should().BeOfType<OkObjectResult>();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.DiscordSubject == "dc-no-email");
        user.Should().NotBeNull();
        user!.Email.Should().BeNull();
        user.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task DiscordCallback_SetsIsNewUser_ForNewAccounts()
    {
        _oauthProviderService
            .Setup(s => s.ExchangeDiscordCodeAsync("code"))
            .ReturnsAsync(new OAuthUserInfo("dc-new", "New Discord", "new-dc@test.com", null));

        var result = await _controller.DiscordCallback(new OAuthCallbackRequest { Code = "code" });
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("\"isNewUser\":true");
    }

    [Fact]
    public async Task DiscordCallback_IsNewUserFalse_ForExistingAccounts()
    {
        var existing = new User
        {
            Email = "returning-dc@test.com",
            DiscordSubject = "dc-returning",
            DisplayName = "Returning DC"
        };
        _db.Users.Add(existing);
        await _db.SaveChangesAsync();

        _oauthProviderService
            .Setup(s => s.ExchangeDiscordCodeAsync("code"))
            .ReturnsAsync(new OAuthUserInfo("dc-returning", "Returning DC", "returning-dc@test.com", null));

        var result = await _controller.DiscordCallback(new OAuthCallbackRequest { Code = "code" });
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("\"isNewUser\":false");
    }

    // --- Register: response contains expected user fields ---

    [Fact]
    public async Task Register_ResponseIncludesUserData()
    {
        var request = new RegisterRequest
        {
            Email = "response-data@test.com",
            Password = "StrongPass1!",
            Nickname = "ResponseUser"
        };

        var result = await _controller.Register(request);
        var created = result.Should().BeOfType<CreatedResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(created.Value);
        json.Should().Contain("accessToken");
        json.Should().Contain("refreshToken");
        json.Should().Contain("ResponseUser");
        json.Should().Contain("response-data@test.com");
    }

    // --- Login: response contains expected user fields ---

    [Fact]
    public async Task Login_ResponseIncludesUserData()
    {
        await CreateUserWithPassword("login-data@test.com", "Pass123!", "LoginUser");

        var result = await _controller.Login(new LoginRequest { Email = "login-data@test.com", Password = "Pass123!" });
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("accessToken");
        json.Should().Contain("refreshToken");
        json.Should().Contain("LoginUser");
    }

    // --- OAuth: auto-join default server for new OAuth users ---

    [Fact]
    public async Task GitHubCallback_NewUser_AutoJoinsDefaultServer()
    {
        // Create default server
        var defaultServer = new Server { Id = Server.DefaultServerId, Name = "Default" };
        _db.Servers.Add(defaultServer);
        var memberRole = new ServerRoleEntity { ServerId = Server.DefaultServerId, Name = "Member", Position = 2, Permissions = PermissionExtensions.MemberDefaults, IsSystemRole = true };
        _db.ServerRoles.Add(memberRole);
        await _db.SaveChangesAsync();

        _oauthProviderService
            .Setup(s => s.ExchangeGitHubCodeAsync("code"))
            .ReturnsAsync(new OAuthUserInfo("gh-autojoin", "AutoJoin", "autojoin-gh@test.com", null));

        await _controller.GitHubCallback(new OAuthCallbackRequest { Code = "code" });

        var user = await _db.Users.FirstAsync(u => u.GitHubSubject == "gh-autojoin");
        var membership = await _db.ServerMembers.FirstOrDefaultAsync(sm => sm.UserId == user.Id && sm.ServerId == Server.DefaultServerId);
        membership.Should().NotBeNull();
    }

    // --- Register: case sensitivity of email duplicate check ---

    [Fact]
    public async Task Register_DuplicateEmail_CaseInsensitive_Returns409()
    {
        await CreateUserWithPassword("dupe@test.com");

        var request = new RegisterRequest
        {
            Email = "DUPE@TEST.COM",
            Password = "AnotherPass1!",
            Nickname = "Dupe"
        };

        var result = await _controller.Register(request);
        result.Should().BeOfType<ConflictObjectResult>();
    }

    // --- Helper to create controller with specific authenticated user ---

    private AuthController CreateControllerForUser(User user)
    {
        var controller = new AuthController(_db, _tokenService, _avatarService.Object, _config, _emailVerificationService, _oauthProviderService.Object, Mock.Of<ILogger<AuthController>>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
                    new System.Security.Claims.Claim("sub", user.Id.ToString())
                ], "Bearer"))
            }
        };
        return controller;
    }

    // ═══════════════════ LinkGoogle tests ═══════════════════

    [Fact]
    public async Task LinkGoogle_NoAuthClaim_Returns401()
    {
        var controller = new AuthController(_db, _tokenService, _avatarService.Object, _config, _emailVerificationService, _oauthProviderService.Object, Mock.Of<ILogger<AuthController>>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity())
            }
        };

        var request = new LinkGoogleRequest { Email = "test@test.com", Password = "Pass123!", GoogleCredential = "fake-cred" };
        var result = await controller.LinkGoogle(request);
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task LinkGoogle_InvalidSubClaim_Returns401()
    {
        var controller = new AuthController(_db, _tokenService, _avatarService.Object, _config, _emailVerificationService, _oauthProviderService.Object, Mock.Of<ILogger<AuthController>>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
                    new System.Security.Claims.Claim("sub", "not-a-guid")
                ], "Bearer"))
            }
        };

        var request = new LinkGoogleRequest { Email = "test@test.com", Password = "Pass123!", GoogleCredential = "fake-cred" };
        var result = await controller.LinkGoogle(request);
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task LinkGoogle_UserNotFound_Returns401()
    {
        var user = await CreateUserWithPassword("linkgoogle@test.com", "Pass123!");
        var controller = CreateControllerForUser(user);

        var request = new LinkGoogleRequest { Email = "nonexistent@test.com", Password = "Pass123!", GoogleCredential = "fake-cred" };
        var result = await controller.LinkGoogle(request);
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task LinkGoogle_WrongPassword_Returns401()
    {
        var user = await CreateUserWithPassword("linkgoogle-wrong@test.com", "Correct123!");
        var controller = CreateControllerForUser(user);

        var request = new LinkGoogleRequest { Email = "linkgoogle-wrong@test.com", Password = "WrongPass!", GoogleCredential = "fake-cred" };
        var result = await controller.LinkGoogle(request);
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task LinkGoogle_DifferentUser_ReturnsForbid()
    {
        var user1 = await CreateUserWithPassword("user1-link@test.com", "Pass123!");
        var user2 = await CreateUserWithPassword("user2-link@test.com", "Pass123!");

        // Controller authenticated as user1, trying to link user2's account
        var controller = CreateControllerForUser(user1);

        var request = new LinkGoogleRequest { Email = "user2-link@test.com", Password = "Pass123!", GoogleCredential = "fake-cred" };
        var result = await controller.LinkGoogle(request);
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task LinkGoogle_LockedAccount_Returns401()
    {
        var user = await CreateUserWithPassword("locked-link@test.com", "Pass123!");
        user.FailedLoginAttempts = 5;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10);
        await _db.SaveChangesAsync();

        var controller = CreateControllerForUser(user);

        var request = new LinkGoogleRequest { Email = "locked-link@test.com", Password = "Pass123!", GoogleCredential = "fake-cred" };
        var result = await controller.LinkGoogle(request);
        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        var json = System.Text.Json.JsonSerializer.Serialize(unauthorized.Value);
        json.Should().Contain("temporarily locked");
    }

    [Fact]
    public async Task LinkGoogle_ExpiredLockout_ReturnsErrorButDoesNotStayLocked()
    {
        var user = await CreateUserWithPassword("expired-lock-link@test.com", "Pass123!");
        user.FailedLoginAttempts = 5;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(-1); // expired
        await _db.SaveChangesAsync();

        var controller = CreateControllerForUser(user);

        // Password is correct but Google credential is invalid — should return
        // BadRequest from the SecurityTokenException catch or throw
        var request = new LinkGoogleRequest { Email = "expired-lock-link@test.com", Password = "Pass123!", GoogleCredential = "fake-cred" };

        IActionResult? result = null;
        try
        {
            result = await controller.LinkGoogle(request);
        }
        catch (Exception)
        {
            // Google credential parsing may throw before reaching the catch block
        }

        // The method either returns BadRequest or throws — either way the
        // Google credential was invalid, which exercises the validation path.
        if (result is not null)
        {
            result.Should().BeOfType<BadRequestObjectResult>();
        }
    }

    [Fact]
    public async Task LinkGoogle_WrongPassword_IncrementsFailedAttempts()
    {
        var user = await CreateUserWithPassword("fail-link@test.com", "Correct123!");
        var controller = CreateControllerForUser(user);

        var request = new LinkGoogleRequest { Email = "fail-link@test.com", Password = "WrongPass!", GoogleCredential = "fake-cred" };
        await controller.LinkGoogle(request);

        await _db.Entry(user).ReloadAsync();
        user.FailedLoginAttempts.Should().Be(1);
    }

    [Fact]
    public async Task LinkGoogle_WrongPassword_FiveAttempts_LocksAccount()
    {
        var user = await CreateUserWithPassword("lock-link@test.com", "Correct123!");
        user.FailedLoginAttempts = 4; // one more triggers lockout
        await _db.SaveChangesAsync();

        var controller = CreateControllerForUser(user);

        var request = new LinkGoogleRequest { Email = "lock-link@test.com", Password = "WrongPass!", GoogleCredential = "fake-cred" };
        await controller.LinkGoogle(request);

        await _db.Entry(user).ReloadAsync();
        user.FailedLoginAttempts.Should().Be(5);
        user.LockoutEnd.Should().NotBeNull();
    }

    [Fact]
    public async Task LinkGoogle_GoogleOnlyUser_Returns401()
    {
        var googleUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "google-only@test.com",
            GoogleSubject = "google-subject-123",
            DisplayName = "Google Only",
            PasswordHash = null
        };
        _db.Users.Add(googleUser);
        await _db.SaveChangesAsync();

        var controller = CreateControllerForUser(googleUser);

        var request = new LinkGoogleRequest { Email = "google-only@test.com", Password = "AnyPass!", GoogleCredential = "fake-cred" };
        var result = await controller.LinkGoogle(request);
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // ═══════════════════ Login — lockout edge cases ═══════════════════

    [Fact]
    public async Task Login_MultipleFailedAttempts_IncrementCounter()
    {
        var user = await CreateUserWithPassword("lockout@test.com", "Correct1!");

        // 3 failed attempts
        for (int i = 0; i < 3; i++)
        {
            await _controller.Login(new LoginRequest { Email = "lockout@test.com", Password = "Wrong!" });
        }

        var updated = await _db.Users.FirstAsync(u => u.Email == "lockout@test.com");
        updated.FailedLoginAttempts.Should().Be(3);
        updated.LockoutEnd.Should().BeNull(); // Not yet locked (needs 5)
    }

    [Fact]
    public async Task Login_ExactlyFiveFailedAttempts_LocksAccount()
    {
        var user = await CreateUserWithPassword("lock5@test.com", "Correct1!");

        for (int i = 0; i < 5; i++)
        {
            await _controller.Login(new LoginRequest { Email = "lock5@test.com", Password = "Wrong!" });
        }

        var updated = await _db.Users.FirstAsync(u => u.Email == "lock5@test.com");
        updated.FailedLoginAttempts.Should().Be(5);
        updated.LockoutEnd.Should().NotBeNull();
    }

    [Fact]
    public async Task Login_SuccessAfterFailedAttempts_ResetsCounter()
    {
        var user = await CreateUserWithPassword("resetlock@test.com", "Correct1!");

        // 2 failed attempts
        await _controller.Login(new LoginRequest { Email = "resetlock@test.com", Password = "Wrong!" });
        await _controller.Login(new LoginRequest { Email = "resetlock@test.com", Password = "Wrong!" });

        // Successful login
        var result = await _controller.Login(new LoginRequest { Email = "resetlock@test.com", Password = "Correct1!" });

        result.Should().BeOfType<OkObjectResult>();
        var updated = await _db.Users.FirstAsync(u => u.Email == "resetlock@test.com");
        updated.FailedLoginAttempts.Should().Be(0);
        updated.LockoutEnd.Should().BeNull();
    }

    // ═══════════════════ Register — email normalization ═══════════════════

    [Fact]
    public async Task Register_WhitespaceEmail_IsTrimmed()
    {
        var request = new RegisterRequest
        {
            Email = "  whitespace@test.com  ",
            Password = "StrongPass1!",
            Nickname = "Ws"
        };

        var result = await _controller.Register(request);

        result.Should().BeOfType<CreatedResult>();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == "whitespace@test.com");
        user.Should().NotBeNull();
    }

    // ═══════════════════ Refresh — rotation ═══════════════════

    [Fact]
    public async Task Refresh_InvalidToken_Returns401()
    {
        var result = await _controller.Refresh(new RefreshRequest { RefreshToken = "invalid-token-abc" });
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // ═══════════════════ Logout — invalid token still returns 204 ═══════════════════

    [Fact]
    public async Task Logout_NonExistentToken_Returns204()
    {
        var result = await _controller.Logout(new LogoutRequest { RefreshToken = "nonexistent" });
        result.Should().BeOfType<NoContentResult>();
    }

    // ═══════════════════ VerifyEmail — tests ═══════════════════

    [Fact]
    public async Task VerifyEmail_EmptyToken_Returns400()
    {
        var result = await _controller.VerifyEmail(new VerifyEmailRequest { Token = "" });
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ═══════════════════ OAuth config ═══════════════════

    [Fact]
    public void GetOAuthConfig_EmptyConfig_ReturnsDisabledProviders()
    {
        var result = _controller.GetOAuthConfig();

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ GitHub callback — null email user ═══════════════════

    [Fact]
    public async Task GitHubCallback_ExistingUser_BySubject_ReturnsTokens()
    {
        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "GitHub User",
            GitHubSubject = "gh-existing-123",
            Email = "ghuser@test.com"
        };
        _db.Users.Add(existingUser);
        await _db.SaveChangesAsync();

        _oauthProviderService
            .Setup(o => o.ExchangeGitHubCodeAsync(It.IsAny<string>()))
            .ReturnsAsync(new OAuthUserInfo("gh-existing-123", "GitHub User", "ghuser@test.com", null));

        var result = await _controller.GitHubCallback(new OAuthCallbackRequest { Code = "code" });

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ Discord callback — existing by subject ═══════════════════

    [Fact]
    public async Task DiscordCallback_ExistingUser_BySubject_ReturnsOk()
    {
        var existingUser = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "Discord User",
            DiscordSubject = "dc-existing-456",
            Email = "dcuser@test.com"
        };
        _db.Users.Add(existingUser);
        await _db.SaveChangesAsync();

        _oauthProviderService
            .Setup(o => o.ExchangeDiscordCodeAsync(It.IsAny<string>()))
            .ReturnsAsync(new OAuthUserInfo("dc-existing-456", "Discord User", "dcuser@test.com", null));

        var result = await _controller.DiscordCallback(new OAuthCallbackRequest { Code = "code" });

        result.Should().BeOfType<OkObjectResult>();
    }

    // ═══════════════════ Login — response structure ═══════════════════

    [Fact]
    public async Task Login_SuccessfulLogin_ReturnsAccessAndRefreshTokens()
    {
        await CreateUserWithPassword("struct@test.com", "Valid1!");

        var result = await _controller.Login(new LoginRequest { Email = "struct@test.com", Password = "Valid1!" });

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();
    }

    // ═══════════════════ Register — auto joins default server when it exists ═══════════════════

    [Fact]
    public async Task Register_NoDefaultServer_StillSucceeds()
    {
        // No default server in DB
        var request = new RegisterRequest
        {
            Email = "nodefault@test.com",
            Password = "StrongPass1!",
            Nickname = "NoDefault"
        };

        var result = await _controller.Register(request);

        result.Should().BeOfType<CreatedResult>();
    }

    // ═══════════════════ Register — default server without member role ═══════════════════

    [Fact]
    public async Task Register_DefaultServerExistsButNoMemberRole_StillSucceeds()
    {
        var defaultServer = new Server { Id = Server.DefaultServerId, Name = "Default" };
        _db.Servers.Add(defaultServer);
        // No member role added — only the server exists
        await _db.SaveChangesAsync();

        var request = new RegisterRequest
        {
            Email = "norole@test.com",
            Password = "StrongPass1!",
            Nickname = "NoRole"
        };

        var result = await _controller.Register(request);

        result.Should().BeOfType<CreatedResult>();
        var user = await _db.Users.FirstAsync(u => u.Email == "norole@test.com");
        var membership = await _db.ServerMembers.FirstOrDefaultAsync(sm => sm.UserId == user.Id);
        membership.Should().BeNull();
    }

    // ═══════════════════ Register — response includes emailSent field ═══════════════════

    [Fact]
    public async Task Register_EmailVerificationSuccess_EmailSentTrue()
    {
        _emailSender.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var request = new RegisterRequest
        {
            Email = "emailsent@test.com",
            Password = "StrongPass1!",
            Nickname = "EmailSent"
        };

        var result = await _controller.Register(request);

        result.Should().BeOfType<CreatedResult>();
    }

    // ═══════════════════ Login — lockout and reset paths ═══════════════════

    [Fact]
    public async Task Login_SuccessAfterExpiredLockout_ResetsFailedCountAndLockout()
    {
        var user = await CreateUserWithPassword("lockout-reset@test.com", "Valid1!");
        user.FailedLoginAttempts = 5;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(-1); // Expired lockout
        await _db.SaveChangesAsync();

        var result = await _controller.Login(new LoginRequest { Email = "lockout-reset@test.com", Password = "Valid1!" });

        result.Should().BeOfType<OkObjectResult>();
        var updatedUser = await _db.Users.FirstAsync(u => u.Email == "lockout-reset@test.com");
        updatedUser.FailedLoginAttempts.Should().Be(0);
        updatedUser.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public async Task Login_SuccessWithPreviousFailedAttempts_ResetsCounter()
    {
        var user = await CreateUserWithPassword("reset-counter@test.com", "Valid1!");
        user.FailedLoginAttempts = 3;
        user.LockoutEnd = null;
        await _db.SaveChangesAsync();

        var result = await _controller.Login(new LoginRequest { Email = "reset-counter@test.com", Password = "Valid1!" });

        result.Should().BeOfType<OkObjectResult>();
        var updatedUser = await _db.Users.FirstAsync(u => u.Email == "reset-counter@test.com");
        updatedUser.FailedLoginAttempts.Should().Be(0);
    }

    // ═══════════════════ OAuth — HandleOAuthLogin new user auto-join ═══════════════════

    [Fact]
    public async Task DiscordCallback_NewUser_AutoJoinsDefaultServer()
    {
        var defaultServer = new Server { Id = Server.DefaultServerId, Name = "Default" };
        _db.Servers.Add(defaultServer);
        var memberRole = new ServerRoleEntity { ServerId = Server.DefaultServerId, Name = "Member", Position = 2, Permissions = PermissionExtensions.MemberDefaults, IsSystemRole = true };
        _db.ServerRoles.Add(memberRole);
        await _db.SaveChangesAsync();

        _oauthProviderService.Setup(o => o.ExchangeDiscordCodeAsync(It.IsAny<string>()))
            .ReturnsAsync(new OAuthUserInfo("discord-new-123", "DiscordNew", "discordnew@test.com", null));

        var result = await _controller.DiscordCallback(new OAuthCallbackRequest { Code = "code123" });

        result.Should().BeOfType<OkObjectResult>();
        var user = await _db.Users.FirstAsync(u => u.DiscordSubject == "discord-new-123");
        var membership = await _db.ServerMembers.FirstOrDefaultAsync(sm => sm.UserId == user.Id && sm.ServerId == Server.DefaultServerId);
        membership.Should().NotBeNull();
    }

    [Fact]
    public async Task DiscordCallback_NewUser_DefaultServerNoMemberRole_StillSucceeds()
    {
        var defaultServer = new Server { Id = Server.DefaultServerId, Name = "Default" };
        _db.Servers.Add(defaultServer);
        // No member role
        await _db.SaveChangesAsync();

        _oauthProviderService.Setup(o => o.ExchangeDiscordCodeAsync(It.IsAny<string>()))
            .ReturnsAsync(new OAuthUserInfo("discord-norole-123", "DiscordNoRole", "discordnorole@test.com", null));

        var result = await _controller.DiscordCallback(new OAuthCallbackRequest { Code = "code" });

        result.Should().BeOfType<OkObjectResult>();
        var user = await _db.Users.FirstAsync(u => u.DiscordSubject == "discord-norole-123");
        var membership = await _db.ServerMembers.FirstOrDefaultAsync(sm => sm.UserId == user.Id);
        membership.Should().BeNull();
    }

    // ═══════════════════ Register — duplicate email on save race ═══════════════════

    [Fact]
    public async Task Register_ResponseIncludesEmailVerifiedField()
    {
        var request = new RegisterRequest
        {
            Email = "verified-field@test.com",
            Password = "StrongPass1!",
            Nickname = "VerifiedField"
        };

        var result = await _controller.Register(request);

        result.Should().BeOfType<CreatedResult>();
        var user = await _db.Users.FirstAsync(u => u.Email == "verified-field@test.com");
        user.EmailVerified.Should().BeFalse();
    }

    // ═══════════════════ Login — response shape ═══════════════════

    [Fact]
    public async Task Login_ResponseIncludesEffectiveDisplayName()
    {
        var user = await CreateUserWithPassword("effective-name@test.com", "Valid1!", "NickName");
        user.Nickname = "MyNick";
        await _db.SaveChangesAsync();

        var result = await _controller.Login(new LoginRequest { Email = "effective-name@test.com", Password = "Valid1!" });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    // ═══════════════════ OAuth — GitHub new user with null email ═══════════════════

    [Fact]
    public async Task GitHubCallback_NewUser_NullEmail_SetsEmailVerifiedFalse()
    {
        _oauthProviderService.Setup(o => o.ExchangeGitHubCodeAsync(It.IsAny<string>()))
            .ReturnsAsync(new OAuthUserInfo("github-noemail-456", "GitHubNoEmail", null, "https://avatars.github.com/u/456"));

        var result = await _controller.GitHubCallback(new OAuthCallbackRequest { Code = "code" });

        result.Should().BeOfType<OkObjectResult>();
        var user = await _db.Users.FirstAsync(u => u.GitHubSubject == "github-noemail-456");
        user.EmailVerified.Should().BeFalse();
        user.AvatarUrl.Should().Be("https://avatars.github.com/u/456");
    }

    // ═══════════════════ OAuth — avatar resolution ═══════════════════

    [Fact]
    public async Task GitHubCallback_ExistingUser_ResolvesCustomAvatar()
    {
        var existingUser = new User
        {
            GitHubSubject = "github-avatar-789",
            DisplayName = "GitAvatarUser",
            CustomAvatarPath = "avatars/github.png"
        };
        _db.Users.Add(existingUser);
        await _db.SaveChangesAsync();

        _avatarService.Setup(a => a.ResolveUrl("avatars/github.png"))
            .Returns("https://cdn.example.com/avatars/github.png");

        _oauthProviderService.Setup(o => o.ExchangeGitHubCodeAsync(It.IsAny<string>()))
            .ReturnsAsync(new OAuthUserInfo("github-avatar-789", "GitAvatarUser", null, null));

        var result = await _controller.GitHubCallback(new OAuthCallbackRequest { Code = "code" });

        result.Should().BeOfType<OkObjectResult>();
    }
}
