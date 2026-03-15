using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Codec.Api.Tests.Services;

public class TokenServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly IConfiguration _config;
    private readonly TokenService _svc;
    private readonly User _testUser;

    public TokenServiceTests()
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

        _svc = new TokenService(_config, _db);

        _testUser = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "Alice",
            Nickname = null,
            Email = "alice@test.com",
            AvatarUrl = "https://google.com/alice.jpg",
            CustomAvatarPath = null
        };
        _db.Users.Add(_testUser);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // --- GenerateAccessToken ---

    [Fact]
    public void GenerateAccessToken_ReturnsValidJwtWithCorrectClaims()
    {
        var jwt = _svc.GenerateAccessToken(_testUser);

        jwt.Should().NotBeNullOrWhiteSpace();
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);

        token.Claims.First(c => c.Type == "sub").Value.Should().Be(_testUser.Id.ToString());
        token.Claims.First(c => c.Type == "email").Value.Should().Be("alice@test.com");
        token.Claims.First(c => c.Type == "name").Value.Should().Be("Alice");
        token.Claims.First(c => c.Type == "picture").Value.Should().Be("https://google.com/alice.jpg");
    }

    [Fact]
    public void GenerateAccessToken_UsesConfiguredIssuerAndAudience()
    {
        var jwt = _svc.GenerateAccessToken(_testUser);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);

        token.Issuer.Should().Be("codec-api");
        token.Audiences.Should().Contain("codec-api");
    }

    [Fact]
    public void GenerateAccessToken_NoPicture_OmitsPictureClaim()
    {
        var userNoPic = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "Bob",
            Email = "bob@test.com",
            AvatarUrl = null,
            CustomAvatarPath = null
        };

        var jwt = _svc.GenerateAccessToken(userNoPic);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);

        token.Claims.Should().NotContain(c => c.Type == "picture");
    }

    [Fact]
    public void GenerateAccessToken_CustomAvatarPath_UsesThatOverAvatarUrl()
    {
        var userCustom = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "Custom",
            Email = "custom@test.com",
            AvatarUrl = "https://google.com/old.jpg",
            CustomAvatarPath = "/uploads/custom-avatar.png"
        };

        var jwt = _svc.GenerateAccessToken(userCustom);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);

        token.Claims.First(c => c.Type == "picture").Value.Should().Be("/uploads/custom-avatar.png");
    }

    [Fact]
    public void GenerateAccessToken_ThrowsWhenJwtSecretMissing()
    {
        var emptyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var svc = new TokenService(emptyConfig, _db);

        svc.Invoking(s => s.GenerateAccessToken(_testUser))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:Secret*");
    }

    [Fact]
    public void GenerateAccessToken_UsesNicknameAsEffectiveDisplayName()
    {
        var userWithNick = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "Original",
            Nickname = "Nicky",
            Email = "nick@test.com"
        };

        var jwt = _svc.GenerateAccessToken(userWithNick);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);

        token.Claims.First(c => c.Type == "name").Value.Should().Be("Nicky");
    }

    // --- GenerateRefreshTokenAsync ---

    [Fact]
    public async Task GenerateRefreshTokenAsync_SavesHashedTokenToDb()
    {
        var (opaqueToken, entity) = await _svc.GenerateRefreshTokenAsync(_testUser);

        opaqueToken.Should().NotBeNullOrWhiteSpace();
        entity.Should().NotBeNull();
        entity.UserId.Should().Be(_testUser.Id);
        entity.TokenHash.Should().NotBeNullOrWhiteSpace();
        entity.RevokedAt.Should().BeNull();
        entity.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        // Verify the hash matches
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(opaqueToken)));
        entity.TokenHash.Should().Be(expectedHash);

        // Verify it was persisted
        var dbToken = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Id == entity.Id);
        dbToken.Should().NotBeNull();
    }

    // --- ValidateRefreshTokenAsync ---

    [Fact]
    public async Task ValidateRefreshTokenAsync_ReturnsTokenForValidHash()
    {
        var (opaqueToken, _) = await _svc.GenerateRefreshTokenAsync(_testUser);

        var result = await _svc.ValidateRefreshTokenAsync(opaqueToken);

        result.Should().NotBeNull();
        result!.UserId.Should().Be(_testUser.Id);
        result.User.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_ReturnsNullForRevokedToken()
    {
        var (opaqueToken, entity) = await _svc.GenerateRefreshTokenAsync(_testUser);
        entity.RevokedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var result = await _svc.ValidateRefreshTokenAsync(opaqueToken);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_ReturnsNullForExpiredToken()
    {
        var (opaqueToken, entity) = await _svc.GenerateRefreshTokenAsync(_testUser);
        entity.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();

        var result = await _svc.ValidateRefreshTokenAsync(opaqueToken);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_ReturnsNullForUnknownToken()
    {
        var result = await _svc.ValidateRefreshTokenAsync("totally-bogus-token");
        result.Should().BeNull();
    }

    // --- RevokeRefreshTokenAsync ---

    [Fact]
    public async Task RevokeRefreshTokenAsync_SetsRevokedAtTimestamp()
    {
        var (_, entity) = await _svc.GenerateRefreshTokenAsync(_testUser);
        entity.RevokedAt.Should().BeNull();

        await _svc.RevokeRefreshTokenAsync(entity);

        entity.RevokedAt.Should().NotBeNull();
        entity.RevokedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    // --- RevokeAllUserRefreshTokensAsync ---
    // Note: ExecuteUpdateAsync is not supported by the InMemory provider.
    // This method is tested via integration tests against a real database.
    // We verify here that the method exists and throws the expected provider exception.

    [Fact]
    public async Task RevokeAllUserRefreshTokensAsync_ThrowsOnInMemoryProvider()
    {
        // ExecuteUpdateAsync is a bulk SQL operation not supported by InMemory.
        // This verifies the method signature; real behavior is covered by integration tests.
        await _svc.Invoking(s => s.RevokeAllUserRefreshTokensAsync(_testUser.Id))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
