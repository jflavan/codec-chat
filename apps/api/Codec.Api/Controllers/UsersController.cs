using Codec.Api.Data;
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
        var appUser = await userService.GetOrCreateUserAsync(User);
        var claims = User.Claims.Select(claim => new { claim.Type, claim.Value });
        var effectiveAvatarUrl = avatarService.ResolveUrl(appUser.CustomAvatarPath) ?? appUser.AvatarUrl;

        return Ok(new
        {
            user = new
            {
                appUser.Id,
                appUser.DisplayName,
                appUser.Email,
                AvatarUrl = effectiveAvatarUrl,
                appUser.GoogleSubject
            },
            claims
        });
    }

    /// <summary>
    /// Searches users by display name or email. Returns up to 20 results.
    /// Excludes the current user from results.
    /// </summary>
    [HttpGet("users/search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string? q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Ok(Array.Empty<object>());
        }

        var term = q.Trim();
        var appUser = await userService.GetOrCreateUserAsync(User);

        // Use parameterized EF.Functions.Like to prevent injection.
        var pattern = $"%{term}%";
        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.Id != appUser.Id &&
                        (EF.Functions.Like(u.DisplayName, pattern) ||
                         (u.Email != null && EF.Functions.Like(u.Email, pattern))))
            .Take(20)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
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

            return new
            {
                u.Id,
                u.DisplayName,
                u.Email,
                AvatarUrl = avatarService.ResolveUrl(u.CustomAvatarPath) ?? u.GoogleAvatarUrl,
                RelationshipStatus = relationshipStatus
            };
        });

        return Ok(result);
    }
}
