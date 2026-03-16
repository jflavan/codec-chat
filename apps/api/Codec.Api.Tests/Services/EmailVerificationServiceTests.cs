using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Codec.Api.Tests.Services;

public class EmailVerificationServiceTests : IDisposable
{
    private readonly CodecDbContext _db;
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly EmailVerificationService _service;

    public EmailVerificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<CodecDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CodecDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "http://localhost:5174"
            })
            .Build();

        _service = new EmailVerificationService(_db, _emailSender.Object, config);
    }

    public void Dispose() => _db.Dispose();

    private async Task<User> CreateUnverifiedUser(string email = "test@test.com")
    {
        var user = new User { Email = email, PasswordHash = "hash", DisplayName = "Test", Nickname = "Test" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task GenerateAndSend_StoresHashedToken_SendsEmail()
    {
        var user = await CreateUnverifiedUser();

        var rawToken = await _service.GenerateAndSendVerificationAsync(user);

        rawToken.Should().NotBeNullOrEmpty();
        user.EmailVerificationToken.Should().NotBeNullOrEmpty();
        user.EmailVerificationToken.Should().NotBe(rawToken); // Should be hashed
        user.EmailVerificationTokenExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        user.EmailVerificationTokenSentAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        _emailSender.Verify(e => e.SendEmailAsync("test@test.com", "Verify your Codec email", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task VerifyToken_ValidToken_SetsEmailVerified()
    {
        var user = await CreateUnverifiedUser();
        var rawToken = await _service.GenerateAndSendVerificationAsync(user);

        var result = await _service.VerifyTokenAsync(rawToken);

        result.Should().NotBeNull();
        result!.EmailVerified.Should().BeTrue();
        result.EmailVerificationToken.Should().BeNull();
        result.EmailVerificationTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task VerifyToken_InvalidToken_ReturnsNull()
    {
        var result = await _service.VerifyTokenAsync("not-a-real-token");
        result.Should().BeNull();
    }

    [Fact]
    public async Task VerifyToken_ExpiredToken_ReturnsNull()
    {
        var user = await CreateUnverifiedUser();
        var rawToken = await _service.GenerateAndSendVerificationAsync(user);

        // Manually expire the token
        user.EmailVerificationTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
        await _db.SaveChangesAsync();

        var result = await _service.VerifyTokenAsync(rawToken);
        result.Should().BeNull();
    }

    [Fact]
    public async Task VerifyToken_AlreadyVerified_ReturnsNull()
    {
        var user = await CreateUnverifiedUser();
        var rawToken = await _service.GenerateAndSendVerificationAsync(user);

        // Verify once
        await _service.VerifyTokenAsync(rawToken);

        // Try to verify again with same token pattern
        var result = await _service.VerifyTokenAsync(rawToken);
        result.Should().BeNull();
    }

    [Fact]
    public void CanResend_AlreadyVerified_ReturnsFalse()
    {
        var user = new User { EmailVerified = true };
        _service.CanResend(user).Should().BeFalse();
    }

    [Fact]
    public void CanResend_WithinCooldown_ReturnsFalse()
    {
        var user = new User
        {
            EmailVerified = false,
            EmailVerificationTokenSentAt = DateTimeOffset.UtcNow.AddSeconds(-30)
        };
        _service.CanResend(user).Should().BeFalse();
    }

    [Fact]
    public void CanResend_AfterCooldown_ReturnsTrue()
    {
        var user = new User
        {
            EmailVerified = false,
            EmailVerificationTokenSentAt = DateTimeOffset.UtcNow.AddMinutes(-3)
        };
        _service.CanResend(user).Should().BeTrue();
    }

    [Fact]
    public void CanResend_NeverSent_ReturnsTrue()
    {
        var user = new User { EmailVerified = false, EmailVerificationTokenSentAt = null };
        _service.CanResend(user).Should().BeTrue();
    }
}
