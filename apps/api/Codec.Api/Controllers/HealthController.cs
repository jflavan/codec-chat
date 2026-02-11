using Microsoft.AspNetCore.Mvc;

namespace Codec.Api.Controllers;

/// <summary>
/// Provides health-check and diagnostic endpoints.
/// </summary>
[ApiController]
[Route("")]
public class HealthController(IWebHostEnvironment env) : ControllerBase
{
    /// <summary>
    /// Returns basic API information. Available in development only.
    /// </summary>
    [HttpGet]
    public IActionResult GetInfo()
    {
        if (!env.IsDevelopment())
        {
            return NotFound();
        }

        return Ok(new { name = "Codec API", status = "dev" });
    }

    /// <summary>
    /// Returns a simple health-check response.
    /// </summary>
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new { status = "ok" });
    }
}
