using Codec.Api.Data;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

/// <summary>
/// Manages avatar image uploads for users and server-specific overrides.
/// </summary>
[ApiController]
[Authorize]
[Route("")]
public class AvatarsController(CodecDbContext db, IUserService userService, IAvatarService avatarService) : ControllerBase
{
    /// <summary>
    /// Uploads a custom global avatar for the authenticated user.
    /// Accepts JPG, JPEG, PNG, WebP, or GIF files up to 10 MB.
    /// </summary>
    [HttpPost("me/avatar")]
    public async Task<IActionResult> UploadUserAvatar(IFormFile file)
    {
        var validationError = avatarService.Validate(file);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);

        // Remove the previous custom avatar file if one exists.
        if (!string.IsNullOrEmpty(appUser.CustomAvatarPath))
        {
            avatarService.DeleteAvatar(appUser.CustomAvatarPath);
        }

        var relativePath = await avatarService.SaveUserAvatarAsync(appUser.Id, file);
        appUser.CustomAvatarPath = relativePath;
        appUser.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { avatarUrl = avatarService.ResolveUrl(relativePath) });
    }

    /// <summary>
    /// Removes the authenticated user's custom avatar, reverting to the Google profile picture.
    /// </summary>
    [HttpDelete("me/avatar")]
    public async Task<IActionResult> DeleteUserAvatar()
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        if (string.IsNullOrEmpty(appUser.CustomAvatarPath))
        {
            return Ok(new { avatarUrl = appUser.AvatarUrl });
        }

        avatarService.DeleteAvatar(appUser.CustomAvatarPath);
        appUser.CustomAvatarPath = null;
        appUser.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { avatarUrl = appUser.AvatarUrl });
    }

    /// <summary>
    /// Uploads a server-specific avatar for the authenticated user.
    /// This avatar overrides the global avatar within the specified server.
    /// </summary>
    [HttpPost("servers/{serverId:guid}/avatar")]
    public async Task<IActionResult> UploadServerAvatar(Guid serverId, IFormFile file)
    {
        var validationError = avatarService.Validate(file);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);
        var membership = await db.ServerMembers
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

        if (membership is null)
        {
            return Forbid();
        }

        // Remove the previous server avatar file if one exists.
        if (!string.IsNullOrEmpty(membership.CustomAvatarPath))
        {
            avatarService.DeleteAvatar(membership.CustomAvatarPath);
        }

        var relativePath = await avatarService.SaveServerAvatarAsync(appUser.Id, serverId, file);
        membership.CustomAvatarPath = relativePath;
        await db.SaveChangesAsync();

        return Ok(new { avatarUrl = avatarService.ResolveUrl(relativePath) });
    }

    /// <summary>
    /// Removes the authenticated user's server-specific avatar, falling back to the global avatar.
    /// </summary>
    [HttpDelete("servers/{serverId:guid}/avatar")]
    public async Task<IActionResult> DeleteServerAvatar(Guid serverId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var membership = await db.ServerMembers
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

        if (membership is null)
        {
            return Forbid();
        }

        if (string.IsNullOrEmpty(membership.CustomAvatarPath))
        {
            // No server avatar to delete; return the effective global avatar.
            var effectiveUrl = avatarService.ResolveUrl(appUser.CustomAvatarPath) ?? appUser.AvatarUrl;
            return Ok(new { avatarUrl = effectiveUrl });
        }

        avatarService.DeleteAvatar(membership.CustomAvatarPath);
        membership.CustomAvatarPath = null;
        await db.SaveChangesAsync();

        var fallbackUrl = avatarService.ResolveUrl(appUser.CustomAvatarPath) ?? appUser.AvatarUrl;
        return Ok(new { avatarUrl = fallbackUrl });
    }
}
