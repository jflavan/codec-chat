using Codec.Api.Data;
using Codec.Api.Filters;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

/// <summary>
/// Returns information about the currently authenticated user and supports user search.
/// </summary>
[ApiController]
[Authorize]
[Route("")]
public class UsersController(IUserService userService, IAvatarService avatarService, CodecDbContext db) : ControllerBase
{
    /// <summary>
    /// Returns the authenticated user's profile and JWT claims.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var issuer = User.FindFirst("iss")?.Value;

        // Check for account linking: Google sign-in where email matches an email/password account
        if (issuer is "https://accounts.google.com" or "accounts.google.com")
        {
            var googleEmail = User.FindFirst("email")?.Value;
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
        }

        var (appUser, isNewUser) = await userService.GetOrCreateUserAsync(User);
        var claims = User.Claims.Select(claim => new { claim.Type, claim.Value });
        var effectiveAvatarUrl = avatarService.ResolveUrl(appUser.CustomAvatarPath) ?? appUser.AvatarUrl;
        var effectiveDisplayName = userService.GetEffectiveDisplayName(appUser);

        return Ok(new
        {
            user = new
            {
                appUser.Id,
                appUser.DisplayName,
                appUser.Nickname,
                EffectiveDisplayName = effectiveDisplayName,
                appUser.Email,
                AvatarUrl = effectiveAvatarUrl,
                appUser.GoogleSubject,
                appUser.GitHubSubject,
                appUser.DiscordSubject,
                appUser.IsGlobalAdmin,
                appUser.EmailVerified
            },
            isNewUser,
            claims
        });
    }

    /// <summary>
    /// Sets or updates the current user's nickname.
    /// </summary>
    [HttpPut("me/nickname")]
    [RequireEmailVerified]
    public async Task<IActionResult> SetNickname([FromBody] SetNicknameRequest request)
    {
        var trimmed = request.Nickname?.Trim() ?? "";

        if (string.IsNullOrEmpty(trimmed))
        {
            return BadRequest(new { error = "Nickname must be between 1 and 32 characters." });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        appUser.Nickname = trimmed;
        appUser.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new
        {
            Nickname = appUser.Nickname,
            EffectiveDisplayName = userService.GetEffectiveDisplayName(appUser)
        });
    }

    /// <summary>
    /// Removes the current user's nickname, reverting to the Google display name.
    /// </summary>
    [HttpDelete("me/nickname")]
    [RequireEmailVerified]
    public async Task<IActionResult> RemoveNickname()
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

        if (appUser.Nickname is null)
        {
            return NotFound(new { error = "No nickname is set." });
        }

        appUser.Nickname = null;
        appUser.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new
        {
            Nickname = (string?)null,
            EffectiveDisplayName = userService.GetEffectiveDisplayName(appUser)
        });
    }

    /// <summary>
    /// Searches users by display name or email. Returns up to 20 results.
    /// Excludes the current user from results.
    /// </summary>
    [HttpGet("users/search")]
    [RequireEmailVerified]
    public async Task<IActionResult> SearchUsers([FromQuery] string? q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Ok(Array.Empty<object>());
        }

        var term = q.Trim();
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

        // Use parameterized EF.Functions.Like to prevent injection.
        var pattern = $"%{term}%";
        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.Id != appUser.Id &&
                        (EF.Functions.Like(u.DisplayName, pattern) ||
                         (u.Nickname != null && EF.Functions.Like(u.Nickname, pattern)) ||
                         (u.Email != null && EF.Functions.Like(u.Email, pattern))))
            .Take(20)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.Nickname,
                u.Email,
                u.CustomAvatarPath,
                GoogleAvatarUrl = u.AvatarUrl
            })
            .ToListAsync();

        // Look up existing friendships between the current user and the results.
        var userIds = users.Select(u => u.Id).ToList();
        var friendships = await db.Friendships
            .AsNoTracking()
            .Where(f => (f.RequesterId == appUser.Id && userIds.Contains(f.RecipientId)) ||
                        (f.RecipientId == appUser.Id && userIds.Contains(f.RequesterId)))
            .Select(f => new { f.RequesterId, f.RecipientId, f.Status })
            .ToListAsync();

        var result = users.Select(u =>
        {
            var fs = friendships.FirstOrDefault(f =>
                (f.RequesterId == appUser.Id && f.RecipientId == u.Id) ||
                (f.RecipientId == appUser.Id && f.RequesterId == u.Id));

            string relationshipStatus = fs is null ? "None" : fs.Status.ToString();
            var effectiveDisplayName = string.IsNullOrWhiteSpace(u.Nickname) ? u.DisplayName : u.Nickname;

            return new
            {
                u.Id,
                DisplayName = effectiveDisplayName,
                EffectiveDisplayName = effectiveDisplayName,
                u.Email,
                AvatarUrl = avatarService.ResolveUrl(u.CustomAvatarPath) ?? u.GoogleAvatarUrl,
                RelationshipStatus = relationshipStatus
            };
        });

        return Ok(result);
    }
}
