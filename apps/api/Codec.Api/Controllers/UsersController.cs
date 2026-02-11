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
public class UsersController(IUserService userService) : ControllerBase
{
    /// <summary>
    /// Returns the authenticated user's profile and JWT claims.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var claims = User.Claims.Select(claim => new { claim.Type, claim.Value });
        return Ok(new
        {
            user = new
            {
                appUser.Id,
                appUser.DisplayName,
                appUser.Email,
                appUser.AvatarUrl,
                appUser.GoogleSubject
            },
            claims
        });
    }
}
