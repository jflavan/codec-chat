using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "GlobalAdmin")]
[Route("admin")]
public class AdminSystemController(CodecDbContext db, IUserService userService, AdminActionService adminActions, PresenceTracker presence) : ControllerBase
{
    public record CreateAnnouncementRequest
    {
        public required string Title { get; init; }
        public required string Body { get; init; }
        public DateTimeOffset? ExpiresAt { get; init; }
    }

    public record UpdateAnnouncementRequest
    {
        public string? Title { get; init; }
        public string? Body { get; init; }
        public DateTimeOffset? ExpiresAt { get; init; }
        public bool ClearExpiresAt { get; init; }
        public bool? IsActive { get; init; }
    }

    [HttpGet("actions")]
    public async Task<IActionResult> GetAdminActions([FromQuery] PaginationParams p, [FromQuery] AdminActionType? actionType = null)
    {
        var query = db.AdminActions.AsNoTracking().AsQueryable();
        if (actionType.HasValue) query = query.Where(a => a.ActionType == actionType.Value);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .Include(a => a.ActorUser)
            .Select(a => new
            {
                a.Id, a.ActionType, a.TargetType, a.TargetId, a.Reason, a.Details, a.CreatedAt,
                ActorName = a.ActorUser!.DisplayName
            })
            .ToListAsync();

        return Ok(PaginatedResponse<object>.Create(items.Cast<object>().ToList(), totalCount, p.Page, p.PageSize));
    }

    [HttpGet("connections")]
    public IActionResult GetConnections()
    {
        return Ok(new { activeUsers = presence.GetOnlineUserCount() });
    }

    [HttpGet("announcements")]
    public async Task<IActionResult> GetAnnouncements()
    {
        var items = await db.SystemAnnouncements.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Include(a => a.CreatedByUser)
            .Select(a => new
            {
                a.Id, a.Title, a.Body, a.IsActive, a.CreatedAt, a.ExpiresAt,
                CreatedBy = a.CreatedByUser!.DisplayName
            })
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost("announcements")]
    [EnableRateLimiting("admin-writes")]
    public async Task<IActionResult> CreateAnnouncement([FromBody] CreateAnnouncementRequest request)
    {
        var (admin, _) = await userService.GetOrCreateUserAsync(User);

        var announcement = new SystemAnnouncement
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Body = request.Body,
            CreatedByUserId = admin.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = request.ExpiresAt,
            IsActive = true
        };
        db.SystemAnnouncements.Add(announcement);
        await db.SaveChangesAsync();

        await adminActions.LogAsync(admin.Id, AdminActionType.AnnouncementCreated, "Announcement", announcement.Id.ToString());

        return Ok(new { announcement.Id });
    }

    [HttpPut("announcements/{id:guid}")]
    [EnableRateLimiting("admin-writes")]
    public async Task<IActionResult> UpdateAnnouncement(Guid id, [FromBody] UpdateAnnouncementRequest request)
    {
        var announcement = await db.SystemAnnouncements.FindAsync(id);
        if (announcement is null) return NotFound();

        if (request.Title is not null) announcement.Title = request.Title;
        if (request.Body is not null) announcement.Body = request.Body;
        if (request.ClearExpiresAt)
            announcement.ExpiresAt = null;
        else if (request.ExpiresAt.HasValue)
            announcement.ExpiresAt = request.ExpiresAt.Value;
        if (request.IsActive.HasValue) announcement.IsActive = request.IsActive.Value;

        await db.SaveChangesAsync();

        var (admin, _) = await userService.GetOrCreateUserAsync(User);
        await adminActions.LogAsync(admin.Id, AdminActionType.AnnouncementUpdated, "Announcement", id.ToString());

        return Ok();
    }

    [HttpDelete("announcements/{id:guid}")]
    [EnableRateLimiting("admin-writes")]
    public async Task<IActionResult> DeleteAnnouncement(Guid id)
    {
        var announcement = await db.SystemAnnouncements.FindAsync(id);
        if (announcement is null) return NotFound();

        db.SystemAnnouncements.Remove(announcement);
        await db.SaveChangesAsync();

        var (admin, _) = await userService.GetOrCreateUserAsync(User);
        await adminActions.LogAsync(admin.Id, AdminActionType.AnnouncementDeleted, "Announcement", id.ToString());

        return Ok();
    }
}
