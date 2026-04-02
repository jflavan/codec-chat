using Codec.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

[ApiController]
[Route("announcements")]
[Authorize]
public class AnnouncementsController(CodecDbContext db) : ControllerBase
{
    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var now = DateTimeOffset.UtcNow;
        var items = await db.SystemAnnouncements.AsNoTracking()
            .Where(a => a.IsActive && (a.ExpiresAt == null || a.ExpiresAt > now))
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.Id, a.Title, a.Body, a.CreatedAt, a.ExpiresAt })
            .ToListAsync();
        return Ok(items);
    }
}
