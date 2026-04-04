using System.IdentityModel.Tokens.Jwt;
using Codec.Api.Data;
using Codec.Api.Filters;
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
    OAuthProviderService oauthProviderService,
    ILogger<AuthController> logger) : ControllerBase
{
    [HttpPost("register")]
    [ValidateRecaptcha(Action = "register")]
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
            var defaultMemberRole = await db.ServerRoles
                .AsNoTracking()
                .Where(r => r.ServerId == Server.DefaultServerId && r.IsSystemRole && r.Name == "Member")
                .FirstOrDefaultAsync();

            if (defaultMemberRole is not null)
            {
                db.ServerMembers.Add(new ServerMember
                {
                    ServerId = Server.DefaultServerId,
                    User = user,
                    JoinedAt = DateTimeOffset.UtcNow
                });
                db.ServerMemberRoles.Add(new ServerMemberRole
                {
                    UserId = user.Id,
                    RoleId = defaultMemberRole.Id,
                    AssignedAt = DateTimeOffset.UtcNow
                });
            }
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
    [ValidateRecaptcha(Action = "login")]
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

        if (user.IsDisabled)
            return Unauthorized(new { error = "Account is disabled." });

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

        if (user.IsDisabled)
            return Unauthorized(new { error = "Account is disabled." });

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
    [Authorize]
    public async Task<IActionResult> LinkGoogle([FromBody] LinkGoogleRequest request)
    {
        var sub = User.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var authenticatedUserId))
            return Unauthorized();

        var email = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || user.PasswordHash is null)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        // Ensure the authenticated user is linking their own account
        if (user.Id != authenticatedUserId)
        {
            return Forbid();
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
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
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

    [HttpPost("google")]
    public async Task<IActionResult> GoogleSignIn([FromBody] GoogleSignInRequest request)
    {
        var googleClientId = configuration["Google:ClientId"];
        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            "https://accounts.google.com/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

        string? googleSubject;
        string? googleEmail;
        string? googleName;
        string? googlePicture;

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
            var principal = handler.ValidateToken(request.Credential, validationParams, out _);

            googleSubject = principal.FindFirst("sub")?.Value;
            googleEmail = principal.FindFirst("email")?.Value;
            googleName = principal.FindFirst("name")?.Value;
            googlePicture = principal.FindFirst("picture")?.Value;

            if (string.IsNullOrWhiteSpace(googleSubject))
                return BadRequest(new { error = "Invalid Google credential." });
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return Unauthorized(new { error = "Invalid or expired Google credential." });
        }

        // Check for account linking: existing email/password user without Google linked
        if (googleEmail is not null)
        {
            var existingByEmail = await db.Users.FirstOrDefaultAsync(
                u => u.Email == googleEmail.ToLowerInvariant() && u.GoogleSubject == null && u.PasswordHash != null);
            if (existingByEmail is not null)
            {
                return Ok(new
                {
                    needsLinking = true,
                    email = existingByEmail.Email
                });
            }
        }

        // Find or create user by Google subject
        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == googleSubject);
        var isNewUser = false;

        if (user is not null)
        {
            // Update profile if changed
            var hasChanges = user.DisplayName != googleName
                          || user.Email != googleEmail
                          || user.AvatarUrl != googlePicture;
            if (hasChanges)
            {
                user.DisplayName = googleName ?? user.DisplayName;
                user.Email = googleEmail;
                user.AvatarUrl = googlePicture;
                user.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        else
        {
            user = new User
            {
                GoogleSubject = googleSubject,
                DisplayName = googleName ?? "Unknown",
                Email = googleEmail,
                AvatarUrl = googlePicture,
                EmailVerified = true
            };
            db.Users.Add(user);

            // Auto-join default server
            var defaultMemberRole = await db.ServerRoles
                .AsNoTracking()
                .Where(r => r.ServerId == Server.DefaultServerId && r.IsSystemRole && r.Name == "Member")
                .FirstOrDefaultAsync();

            if (defaultMemberRole is not null)
            {
                db.ServerMembers.Add(new ServerMember
                {
                    ServerId = Server.DefaultServerId,
                    User = user,
                    JoinedAt = DateTimeOffset.UtcNow
                });
                db.ServerMemberRoles.Add(new ServerMemberRole
                {
                    UserId = user.Id,
                    RoleId = defaultMemberRole.Id,
                    AssignedAt = DateTimeOffset.UtcNow
                });
            }

            try
            {
                await db.SaveChangesAsync();
                isNewUser = true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.Entry(user).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == googleSubject);
                if (user is null)
                    return Conflict(new { error = "Account creation conflict. Please try again." });
            }
        }

        if (user.IsDisabled)
            return Unauthorized(new { error = "Account is disabled." });

        var accessToken = tokenService.GenerateAccessToken(user);
        var (refreshToken, _) = await tokenService.GenerateRefreshTokenAsync(user);
        var effectiveAvatarUrl = avatarService.ResolveUrl(user.CustomAvatarPath) ?? user.AvatarUrl;

        return Ok(new
        {
            accessToken,
            refreshToken,
            isNewUser,
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

    [HttpPost("oauth/github")]
    public async Task<IActionResult> GitHubCallback([FromBody] OAuthCallbackRequest request)
    {
        var userInfo = await oauthProviderService.ExchangeGitHubCodeAsync(request.Code);
        if (userInfo is null)
            return BadRequest(new { error = "Failed to authenticate with GitHub." });

        return await HandleOAuthLogin(userInfo, "github");
    }

    [HttpPost("oauth/discord")]
    public async Task<IActionResult> DiscordCallback([FromBody] OAuthCallbackRequest request)
    {
        var userInfo = await oauthProviderService.ExchangeDiscordCodeAsync(request.Code);
        if (userInfo is null)
            return BadRequest(new { error = "Failed to authenticate with Discord." });

        return await HandleOAuthLogin(userInfo, "discord");
    }

    [HttpGet("oauth/config")]
    public IActionResult GetOAuthConfig()
    {
        return Ok(new
        {
            github = new
            {
                clientId = configuration["OAuth:GitHub:ClientId"] ?? "",
                enabled = !string.IsNullOrWhiteSpace(configuration["OAuth:GitHub:ClientId"])
            },
            discord = new
            {
                clientId = configuration["OAuth:Discord:ClientId"] ?? "",
                enabled = !string.IsNullOrWhiteSpace(configuration["OAuth:Discord:ClientId"])
            }
        });
    }

    private async Task<IActionResult> HandleOAuthLogin(OAuthUserInfo userInfo, string provider)
    {
        User? user;
        var isNew = false;

        if (provider == "github")
        {
            user = await db.Users.FirstOrDefaultAsync(u => u.GitHubSubject == userInfo.Subject);
        }
        else
        {
            user = await db.Users.FirstOrDefaultAsync(u => u.DiscordSubject == userInfo.Subject);
        }

        if (user is null && userInfo.Email is not null)
        {
            // Check if there's an existing account with this email
            var email = userInfo.Email.Trim().ToLowerInvariant();
            var existingByEmail = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (existingByEmail is not null)
            {
                // Do NOT auto-link OAuth identity by email match alone — this is an
                // account-takeover vector if the OAuth provider's email is unverified.
                // The user must sign in to their existing account first, then link the
                // OAuth provider through the authenticated link-google / link-oauth endpoint.
                return Conflict(new
                {
                    error = "An account with this email already exists. Please sign in to that account and link your " + provider + " account from settings.",
                    code = "OAUTH_LINK_REQUIRED",
                    provider
                });
            }
        }

        if (user is null)
        {
            // Create new user
            user = new User
            {
                DisplayName = userInfo.DisplayName,
                Email = userInfo.Email?.Trim().ToLowerInvariant(),
                AvatarUrl = userInfo.AvatarUrl,
                EmailVerified = userInfo.Email is not null,
                GitHubSubject = provider == "github" ? userInfo.Subject : null,
                DiscordSubject = provider == "discord" ? userInfo.Subject : null
            };

            db.Users.Add(user);

            var defaultServerExists = await db.Servers
                .AsNoTracking()
                .AnyAsync(s => s.Id == Server.DefaultServerId);

            if (defaultServerExists)
            {
                var defaultMemberRole = await db.ServerRoles
                    .AsNoTracking()
                    .Where(r => r.ServerId == Server.DefaultServerId && r.IsSystemRole && r.Name == "Member")
                    .FirstOrDefaultAsync();

                if (defaultMemberRole is not null)
                {
                    db.ServerMembers.Add(new ServerMember
                    {
                        ServerId = Server.DefaultServerId,
                        User = user,
                        JoinedAt = DateTimeOffset.UtcNow
                    });
                    db.ServerMemberRoles.Add(new ServerMemberRole
                    {
                        UserId = user.Id,
                        RoleId = defaultMemberRole.Id,
                        AssignedAt = DateTimeOffset.UtcNow
                    });
                }
            }

            try
            {
                await db.SaveChangesAsync();
                isNew = true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Concurrent creation — re-fetch
                db.Entry(user).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                user = provider == "github"
                    ? await db.Users.FirstOrDefaultAsync(u => u.GitHubSubject == userInfo.Subject)
                    : await db.Users.FirstOrDefaultAsync(u => u.DiscordSubject == userInfo.Subject);
                if (user is null)
                    return Conflict(new { error = "Account creation conflict. Please try again." });
            }
        }

        if (user.IsDisabled)
            return Unauthorized(new { error = "Account is disabled." });

        var accessToken = tokenService.GenerateAccessToken(user);
        var (refreshToken, _) = await tokenService.GenerateRefreshTokenAsync(user);
        var effectiveAvatarUrl = avatarService.ResolveUrl(user.CustomAvatarPath) ?? user.AvatarUrl;

        return Ok(new
        {
            accessToken,
            refreshToken,
            isNewUser = isNew,
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
}
