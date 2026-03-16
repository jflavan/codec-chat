using System.IdentityModel.Tokens.Jwt;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Codec.Api.Controllers;

[ApiController]
[Route("auth")]
[EnableRateLimiting("auth")]
public class AuthController(
    CodecDbContext db,
    TokenService tokenService,
    IAvatarService avatarService,
    IConfiguration configuration,
    EmailVerificationService emailVerificationService,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existingUser is not null)
        {
            return Conflict(new { error = "An account with this email already exists." });
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);
        var nickname = request.Nickname.Trim();

        var user = new User
        {
            Email = email,
            PasswordHash = passwordHash,
            DisplayName = nickname,
            Nickname = nickname,
            GoogleSubject = null
        };

        db.Users.Add(user);

        // Auto-join default server
        var defaultServerExists = await db.Servers
            .AsNoTracking()
            .AnyAsync(s => s.Id == Server.DefaultServerId);

        if (defaultServerExists)
        {
            db.ServerMembers.Add(new ServerMember
            {
                ServerId = Server.DefaultServerId,
                User = user,
                Role = ServerRole.Member,
                JoinedAt = DateTimeOffset.UtcNow
            });
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = "An account with this email already exists." });
        }

        var emailSent = true;
        try
        {
            await emailVerificationService.GenerateAndSendVerificationAsync(user);
        }
        catch (Exception ex)
        {
            emailSent = false;
            logger.LogError(ex, "Failed to send verification email during registration for user {UserId}", user.Id);
        }

        var accessToken = tokenService.GenerateAccessToken(user);
        var (refreshToken, _) = await tokenService.GenerateRefreshTokenAsync(user);

        var effectiveAvatarUrl = avatarService.ResolveUrl(user.CustomAvatarPath) ?? user.AvatarUrl;

        return Created("", new
        {
            accessToken,
            refreshToken,
            emailSent,
            user = new
            {
                user.Id,
                user.DisplayName,
                user.Nickname,
                EffectiveDisplayName = user.EffectiveDisplayName,
                user.Email,
                AvatarUrl = effectiveAvatarUrl,
                user.IsGlobalAdmin,
                user.EmailVerified
            }
        });
    }

    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || user.PasswordHash is null)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        // Check account lockout
        if (user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            return Unauthorized(new { error = "Account temporarily locked. Try again later." });
        }

        // Reset failed attempts if a previous lockout has expired
        if (user.LockoutEnd is not null && user.LockoutEnd <= DateTimeOffset.UtcNow)
        {
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            // Increment failed attempts and lock if threshold exceeded
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutEnd = DateTimeOffset.UtcNow.Add(LockoutDuration);
            }
            await db.SaveChangesAsync();
            return Unauthorized(new { error = "Invalid email or password." });
        }

        // Reset lockout state on successful login
        if (user.FailedLoginAttempts > 0 || user.LockoutEnd is not null)
        {
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            await db.SaveChangesAsync();
        }

        var accessToken = tokenService.GenerateAccessToken(user);
        var (refreshToken, _) = await tokenService.GenerateRefreshTokenAsync(user);

        var effectiveAvatarUrl = avatarService.ResolveUrl(user.CustomAvatarPath) ?? user.AvatarUrl;

        return Ok(new
        {
            accessToken,
            refreshToken,
            user = new
            {
                user.Id,
                user.DisplayName,
                user.Nickname,
                EffectiveDisplayName = user.EffectiveDisplayName,
                user.Email,
                AvatarUrl = effectiveAvatarUrl,
                user.IsGlobalAdmin,
                user.EmailVerified
            }
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var result = await tokenService.RotateRefreshTokenAsync(request.RefreshToken);
        if (result is null)
        {
            return Unauthorized(new { error = "Invalid or expired refresh token." });
        }

        var (user, accessToken, newRefreshToken) = result.Value;

        return Ok(new
        {
            accessToken,
            refreshToken = newRefreshToken
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        var storedToken = await tokenService.ValidateRefreshTokenAsync(request.RefreshToken);
        if (storedToken is not null)
        {
            await tokenService.RevokeRefreshTokenAsync(storedToken);
        }

        // Always return 204 to avoid leaking whether the token was valid
        return NoContent();
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        var user = await emailVerificationService.VerifyTokenAsync(request.Token);
        if (user is null)
        {
            return BadRequest(new { error = "Invalid or expired verification token." });
        }

        return Ok(new { message = "Email verified successfully." });
    }

    [HttpPost("resend-verification")]
    [Authorize]
    public async Task<IActionResult> ResendVerification()
    {
        var sub = User.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var userId))
            return Unauthorized();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return Unauthorized();

        if (user.EmailVerified)
            return BadRequest(new { error = "Email is already verified." });

        if (!emailVerificationService.CanResend(user))
            return StatusCode(429, new { error = "Please wait before requesting another verification email." });

        try
        {
            await emailVerificationService.GenerateAndSendVerificationAsync(user);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send verification email to user {UserId}", user.Id);
            return StatusCode(502, new { error = "Failed to send verification email. Please try again later." });
        }

        return Ok(new { message = "Verification email sent." });
    }

    [HttpPost("link-google")]
    public async Task<IActionResult> LinkGoogle([FromBody] LinkGoogleRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || user.PasswordHash is null)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        // Check account lockout
        if (user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            return Unauthorized(new { error = "Account temporarily locked. Try again later." });
        }

        // Reset failed attempts if a previous lockout has expired
        if (user.LockoutEnd is not null && user.LockoutEnd <= DateTimeOffset.UtcNow)
        {
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutEnd = DateTimeOffset.UtcNow.Add(LockoutDuration);
            }
            await db.SaveChangesAsync();
            return Unauthorized(new { error = "Invalid email or password." });
        }

        // Validate the Google credential against Google's JWKS
        var googleClientId = configuration["Google:ClientId"];
        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            "https://accounts.google.com/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

        try
        {
            var openIdConfig = await configManager.GetConfigurationAsync();
            var validationParams = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
                ValidateAudience = true,
                ValidAudience = googleClientId,
                ValidateLifetime = true,
                IssuerSigningKeys = openIdConfig.SigningKeys
            };

            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(request.GoogleCredential, validationParams, out _);

            var googleSubject = principal.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(googleSubject))
            {
                return BadRequest(new { error = "Invalid Google credential." });
            }

            // Check if this Google subject is already linked to another account
            var existingGoogleUser = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == googleSubject);
            if (existingGoogleUser is not null && existingGoogleUser.Id != user.Id)
            {
                return Conflict(new { error = "This Google account is already linked to another user." });
            }

            user.GoogleSubject = googleSubject;

            // Only update profile fields if the user hasn't set their own values
            var googleName = principal.FindFirst("name")?.Value;
            var googlePicture = principal.FindFirst("picture")?.Value;
            if (googleName is not null && (string.IsNullOrWhiteSpace(user.DisplayName) || user.DisplayName == "Unknown"))
                user.DisplayName = googleName;
            if (googlePicture is not null && user.CustomAvatarPath is null && user.AvatarUrl is null)
                user.AvatarUrl = googlePicture;

            user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (SecurityTokenException)
        {
            return BadRequest(new { error = "Invalid or expired Google credential." });
        }

        var accessToken = tokenService.GenerateAccessToken(user);
        var (refreshToken, _) = await tokenService.GenerateRefreshTokenAsync(user);

        return Ok(new
        {
            accessToken,
            refreshToken,
            user = new
            {
                user.Id,
                user.DisplayName,
                user.Nickname,
                EffectiveDisplayName = user.EffectiveDisplayName,
                user.Email,
                AvatarUrl = avatarService.ResolveUrl(user.CustomAvatarPath) ?? user.AvatarUrl,
                user.IsGlobalAdmin
            }
        });
    }
}
