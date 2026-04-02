using Codec.Api.Data;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "GlobalAdmin")]
[Route("admin/servers")]
public class AdminServersController(CodecDbContext db, IUserService userService, AdminActionService adminActions, IHubContext<ChatHub> hub) : ControllerBase
{
    public record ReasonRequest { public required string Reason { get; init; } }
    public record TransferRequest { public required Guid NewOwnerUserId { get; init; } }

    [HttpGet]
    public async Task<IActionResult> GetServers([FromQuery] PaginationParams p)
    {
        var query = db.Servers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(p.Search))
        {
            var term = p.Search.ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .Select(s => new
            {
                s.Id, s.Name, s.IconUrl, s.Description, s.CreatedAt, s.IsQuarantined,
                MemberCount = s.Members.Count
            })
            .ToListAsync();

        return Ok(PaginatedResponse<object>.Create(items.Cast<object>().ToList(), totalCount, p.Page, p.PageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetServer(Guid id)
    {
        var server = await db.Servers.AsNoTracking()
            .Include(s => s.Channels)
            .Include(s => s.Roles)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (server is null) return NotFound();

        var members = await db.ServerMembers.AsNoTracking()
            .Where(m => m.ServerId == id)
            .Include(m => m.User)
            .Select(m => new { m.UserId, DisplayName = m.User!.DisplayName, m.JoinedAt })
            .ToListAsync();

        var ownerRole = server.Roles.FirstOrDefault(r => r.Position == 0 && r.IsSystemRole);
        Guid? ownerId = null;
        if (ownerRole is not null)
        {
            ownerId = await db.ServerMemberRoles.AsNoTracking()
                .Where(mr => mr.RoleId == ownerRole.Id)
                .Select(mr => mr.UserId)
                .FirstOrDefaultAsync();
        }

        return Ok(new
        {
            server = new { server.Id, server.Name, server.IconUrl, server.Description, server.CreatedAt, server.IsQuarantined, server.QuarantinedReason, server.QuarantinedAt },
            ownerId,
            memberCount = members.Count,
            members = members.Take(100),
            channels = server.Channels.Select(c => new { c.Id, c.Name, c.Type }),
            roles = server.Roles.OrderBy(r => r.Position).Select(r => new { r.Id, r.Name, r.Position, r.IsSystemRole, r.Permissions })
        });
    }

    [HttpPost("{id:guid}/quarantine")]
    [EnableRateLimiting("admin-writes")]
    public async Task<IActionResult> QuarantineServer(Guid id, [FromBody] ReasonRequest request)
    {
        var server = await db.Servers.FindAsync(id);
        if (server is null) return NotFound();

        server.IsQuarantined = true;
        server.QuarantinedReason = request.Reason;
        server.QuarantinedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var (admin, _) = await userService.GetOrCreateUserAsync(User);
        await adminActions.LogAsync(admin.Id, AdminActionType.ServerQuarantined, "Server", id.ToString(), request.Reason);

        return Ok();
    }

    [HttpPost("{id:guid}/unquarantine")]
    [EnableRateLimiting("admin-writes")]
    public async Task<IActionResult> UnquarantineServer(Guid id)
    {
        var server = await db.Servers.FindAsync(id);
        if (server is null) return NotFound();

        server.IsQuarantined = false;
        server.QuarantinedReason = null;
        server.QuarantinedAt = null;
        await db.SaveChangesAsync();

        var (admin, _) = await userService.GetOrCreateUserAsync(User);
        await adminActions.LogAsync(admin.Id, AdminActionType.ServerUnquarantined, "Server", id.ToString());

        return Ok();
    }

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting("admin-writes")]
    public async Task<IActionResult> DeleteServer(Guid id, [FromBody] ReasonRequest request)
    {
        var server = await db.Servers.FindAsync(id);
        if (server is null) return NotFound();

        await hub.Clients.Group($"server-{id}").SendAsync("ServerDeleted", new { serverId = id });

        db.Servers.Remove(server);
        await db.SaveChangesAsync();

        var (admin, _) = await userService.GetOrCreateUserAsync(User);
        await adminActions.LogAsync(admin.Id, AdminActionType.ServerDeleted, "Server", id.ToString(), request.Reason);

        return Ok();
    }

    [HttpPut("{id:guid}/transfer-ownership")]
    [EnableRateLimiting("admin-writes")]
    public async Task<IActionResult> TransferOwnership(Guid id, [FromBody] TransferRequest request)
    {
        var server = await db.Servers.Include(s => s.Roles).FirstOrDefaultAsync(s => s.Id == id);
        if (server is null) return NotFound();

        var isMember = await db.ServerMembers.AnyAsync(m => m.ServerId == id && m.UserId == request.NewOwnerUserId);
        if (!isMember) return BadRequest(new { error = "Target user is not a member of this server." });

        var ownerRole = server.Roles.FirstOrDefault(r => r.Position == 0 && r.IsSystemRole);
        if (ownerRole is null) return BadRequest(new { error = "Server has no owner role." });

        var currentOwnerRoles = await db.ServerMemberRoles.Where(mr => mr.RoleId == ownerRole.Id).ToListAsync();
        db.ServerMemberRoles.RemoveRange(currentOwnerRoles);

        db.ServerMemberRoles.Add(new ServerMemberRole { UserId = request.NewOwnerUserId, RoleId = ownerRole.Id, AssignedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var (admin, _) = await userService.GetOrCreateUserAsync(User);
        await adminActions.LogAsync(admin.Id, AdminActionType.ServerOwnershipTransferred, "Server", id.ToString(),
            details: System.Text.Json.JsonSerializer.Serialize(new { newOwnerUserId = request.NewOwnerUserId }));

        return Ok();
    }
}
