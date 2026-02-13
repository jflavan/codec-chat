using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Codec.Api.Controllers;

/// <summary>
/// Handles chat image uploads. Images are validated for type and size,
/// then stored on disk and served as static content.
/// </summary>
[ApiController]
[Authorize]
[Route("uploads")]
public class ImageUploadsController(IImageUploadService imageUploadService, IUserService userService) : ControllerBase
{
    /// <summary>
    /// Uploads a chat image. Returns the public URL for embedding in messages.
    /// Accepts JPG, JPEG, PNG, WebP, and GIF up to 10 MB.
    /// </summary>
    [HttpPost("images")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        var validationError = imageUploadService.Validate(file);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);
        var imageUrl = await imageUploadService.SaveImageAsync(appUser.Id, file);

        return Ok(new { imageUrl });
    }
}
