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
[Route("admin/users")]
public class AdminUsersController(CodecDbContext db, IUserService userService, AdminActionService adminActions) : ControllerBase
{
    public record DisableRequest { public required string Reason { get; init; } }
    public record GlobalAdminRequest { public required bool IsGlobalAdmin { get; init; } }

    [HttpGet]
    public async Task<IActionResult> GetUsers([FromQuery] PaginationParams p)
    {
        var query = db.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.Search))
        {
            var term = p.Search.ToLower();
            query = query.Where(u => u.DisplayName.ToLower().Contains(term)
                || (u.Email != null && u.Email.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .Select(u => new
            {
                u.Id, u.DisplayName, u.Nickname, u.Email, u.AvatarUrl,
                u.IsGlobalAdmin, u.IsDisabled, u.CreatedAt,
                HasGoogle = u.GoogleSubject != null,
                HasGitHub = u.GitHubSubject != null,
                HasDiscord = u.DiscordSubject != null,
                HasSaml = u.SamlNameId != null,
                HasPassword = u.PasswordHash != null
            })
            .ToListAsync();

        return Ok(PaginatedResponse<object>.Create(items.Cast<object>().ToList(), totalCount, p.Page, p.PageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetUser(Guid id)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        var memberships = await db.ServerMembers.AsNoTracking()
            .Where(m => m.UserId == id)
            .Include(m => m.Server)
            .Select(m => new { m.ServerId, ServerName = m.Server!.Name, m.JoinedAt })
            .ToListAsync();

        var recentMessages = await db.Messages.AsNoTracking()
            .Where(m => m.AuthorUserId == id)
            .OrderByDescending(m => m.CreatedAt)
            .Take(50)
            .Select(m => new { m.Id, Content = m.Body, m.CreatedAt, m.ChannelId, ChannelName = m.Channel!.Name, ServerName = m.Channel.Server!.Name })
            .ToListAsync();

        var reportHistory = await db.Reports.AsNoTracking()
            .Where(r => r.ReportType == ReportType.User && r.TargetId == id.ToString())
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .ToListAsync();

        var adminHistory = await db.AdminActions.AsNoTracking()
            .Where(a => a.TargetType == "User" && a.TargetId == id.ToString())
            .OrderByDescending(a => a.CreatedAt)
            .Take(20)
            .ToListAsync();

        return Ok(new
        {
            user = new
            {
                user.Id, user.DisplayName, user.Nickname, user.Email, user.AvatarUrl,
                user.IsGlobalAdmin, user.IsDisabled, user.DisabledReason, user.DisabledAt,
                user.CreatedAt, user.LockoutEnd, user.FailedLoginAttempts, user.EmailVerified,
                HasGoogle = user.GoogleSubject != null, HasGitHub = user.GitHubSubject != null,
                HasDiscord = user.DiscordSubject != null, HasSaml = user.SamlNameId != null,
                HasPassword = user.PasswordHash != null
            },
            memberships,
            recentMessages,
            reportHistory,
            adminHistory
        });
    }

    [HttpPost("{id:guid}/disable")]
    [EnableRateLimiting("admin-writes")]
    public async Task<IActionResult> DisableUser(Guid id, [FromBody] DisableRequest request)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        user.IsDisabled = true;
        user.DisabledReason = request.Reason;
        user.DisabledAt = DateTimeOffset.UtcNow;

        var tokens = await db.RefreshTokens.Where(t => t.UserId == id).ToListAsync();
        db.RefreshTokens.RemoveRange(tokens);

        await db.SaveChangesAsync();

        var (admin, _) = await userService.GetOrCreateUserAsync(User);
        await adminActions.LogAsync(admin.Id, AdminActionType.UserDisabled, "User", id.ToString(), request.Reason);

        return Ok();
    }

    [HttpPost("{id:guid}/enable")]
    [EnableRateLimiting("admin-writes")]
    public async Task<IActionResult> EnableUser(Guid id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        user.IsDisabled = false;
        user.DisabledReason = null;
        user.DisabledAt = null;
        await db.SaveChangesAsync();

        var (admin, _) = await userService.GetOrCreateUserAsync(User);
        await adminActions.LogAsync(admin.Id, AdminActionType.UserEnabled, "User", id.ToString());

        return Ok();
    }

    [HttpPost("{id:guid}/force-logout")]
    [EnableRateLimiting("admin-writes")]
    public async Task<IActionResult> ForceLogout(Guid id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        var tokens = await db.RefreshTokens.Where(t => t.UserId == id).ToListAsync();
        db.RefreshTokens.RemoveRange(tokens);
        await db.SaveChangesAsync();

        var (admin, _) = await userService.GetOrCreateUserAsync(User);
        await adminActions.LogAsync(admin.Id, AdminActionType.UserForcedLogout, "User", id.ToString());

        return Ok();
    }

    [HttpPost("{id:guid}/reset-password")]
    [EnableRateLimiting("admin-writes")]
    public async Task<IActionResult> ResetPassword(Guid id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        user.PasswordHash = null;
        await db.SaveChangesAsync();

        var (admin, _) = await userService.GetOrCreateUserAsync(User);
        await adminActions.LogAsync(admin.Id, AdminActionType.UserPasswordReset, "User", id.ToString());

        return Ok();
    }

    [HttpPut("{id:guid}/global-admin")]
    [EnableRateLimiting("admin-writes")]
    public async Task<IActionResult> SetGlobalAdmin(Guid id, [FromBody] GlobalAdminRequest request)
    {
        var (admin, _) = await userService.GetOrCreateUserAsync(User);

        if (!request.IsGlobalAdmin && id == admin.Id)
            return BadRequest(new { error = "Cannot demote yourself." });

        if (!request.IsGlobalAdmin)
        {
            var adminCount = await db.Users.CountAsync(u => u.IsGlobalAdmin);
            if (adminCount <= 1)
                return BadRequest(new { error = "Cannot demote the last global admin." });
        }

        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        user.IsGlobalAdmin = request.IsGlobalAdmin;
        await db.SaveChangesAsync();

        var actionType = request.IsGlobalAdmin ? AdminActionType.UserPromotedAdmin : AdminActionType.UserDemotedAdmin;
        await adminActions.LogAsync(admin.Id, actionType, "User", id.ToString());

        return Ok();
    }
}
