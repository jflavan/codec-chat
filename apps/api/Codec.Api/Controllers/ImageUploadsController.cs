using Codec.Api.Filters;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Codec.Api.Controllers;

/// <summary>
/// Handles chat image and file uploads. Files are validated for type and size,
/// then stored and served as static content.
/// </summary>
[ApiController]
[Authorize]
[RequireEmailVerified]
[Route("uploads")]
public class ImageUploadsController(IImageUploadService imageUploadService, IFileUploadService fileUploadService, IUserService userService) : ControllerBase
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

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        var imageUrl = await imageUploadService.SaveImageAsync(appUser.Id, file);

        return Ok(new { imageUrl });
    }

    /// <summary>
    /// Uploads a general file attachment (documents, archives, media, etc.).
    /// Returns the public URL and file metadata for embedding in messages.
    /// Accepts common document, archive, code, audio, and video files up to 25 MB.
    /// </summary>
    [HttpPost("files")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        var validationError = fileUploadService.Validate(file);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        var fileUrl = await fileUploadService.SaveFileAsync(appUser.Id, file);

        return Ok(new
        {
            fileUrl,
            fileName = file.FileName,
            fileSize = file.Length,
            fileContentType = file.ContentType
        });
    }
}
