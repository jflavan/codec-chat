using System.IdentityModel.Tokens.Jwt;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
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
    IConfiguration configuration) : ControllerBase
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

        var accessToken = tokenService.GenerateAccessToken(user);
        var (refreshToken, _) = await tokenService.GenerateRefreshTokenAsync(user);

        var effectiveAvatarUrl = avatarService.ResolveUrl(user.CustomAvatarPath) ?? user.AvatarUrl;

        return Created("", new
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
                user.IsGlobalAdmin
            }
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || user.PasswordHash is null)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { error = "Invalid email or password." });
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
                user.IsGlobalAdmin
            }
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var storedToken = await tokenService.ValidateRefreshTokenAsync(request.RefreshToken);
        if (storedToken is null)
        {
            return Unauthorized(new { error = "Invalid or expired refresh token." });
        }

        // Revoke old token (rotation)
        await tokenService.RevokeRefreshTokenAsync(storedToken);

        var user = storedToken.User;
        var accessToken = tokenService.GenerateAccessToken(user);
        var (newRefreshToken, _) = await tokenService.GenerateRefreshTokenAsync(user);

        return Ok(new
        {
            accessToken,
            refreshToken = newRefreshToken
        });
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

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
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

            // Update Google-sourced profile fields
            var googleName = principal.FindFirst("name")?.Value;
            var googlePicture = principal.FindFirst("picture")?.Value;
            if (googleName is not null) user.DisplayName = googleName;
            if (googlePicture is not null) user.AvatarUrl = googlePicture;

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

public record RefreshRequest(string RefreshToken);
public record LinkGoogleRequest(string Email, string Password, string GoogleCredential);
