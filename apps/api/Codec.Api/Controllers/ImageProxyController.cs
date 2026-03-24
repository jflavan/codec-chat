using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Codec.Api.Controllers;

/// <summary>
/// Proxies external image URLs to prevent mixed content and protect user IPs.
/// </summary>
[ApiController]
[Authorize]
[Route("proxy")]
public class ImageProxyController(IImageProxyService imageProxyService) : ControllerBase
{
    /// <summary>
    /// Fetches an external image and returns it with proper caching headers.
    /// </summary>
    [HttpGet("image")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetImage(
        [FromQuery] string url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return BadRequest(new { error = "The 'url' query parameter is required." });
        }

        var result = await imageProxyService.FetchImageAsync(url, cancellationToken);
        if (result is null)
        {
            return NotFound(new { error = "Image could not be fetched." });
        }

        return File(result.Data, result.ContentType);
    }
}
