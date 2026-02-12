using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Codec.Api.Controllers;

/// <summary>
/// Returns information about the currently authenticated user.
/// </summary>
[ApiController]
[Authorize]
[Route("")]
public class UsersController(IUserService userService, IAvatarService avatarService) : ControllerBase
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
}
