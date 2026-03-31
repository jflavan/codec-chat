using Codec.Api.Data;
using Codec.Api.Filters;
using Codec.Api.Hubs;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

/// <summary>
/// Manages custom roles within a server.
/// </summary>
[ApiController]
[Authorize]
[RequireEmailVerified]
[Route("servers/{serverId:guid}/roles")]
public class RolesController(CodecDbContext db, IUserService userService, IHubContext<ChatHub> hub, IPermissionResolverService permissionResolver) : ControllerBase
{
    /// <summary>
    /// Lists all roles for a server, ordered by position.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetRoles(Guid serverId)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureMemberAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var roles = await db.ServerRoles
            .AsNoTracking()
            .Where(r => r.ServerId == serverId)
            .OrderBy(r => r.Position)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Color,
                r.Position,
                Permissions = (long)r.Permissions,
                r.IsSystemRole,
                r.IsHoisted,
                r.IsMentionable,
                MemberCount = db.ServerMemberRoles.Count(mr => mr.RoleId == r.Id)
            })
            .ToListAsync();

        return Ok(roles);
    }

    /// <summary>
    /// Creates a new custom role. Requires ManageRoles permission.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateRole(Guid serverId, [FromBody] CreateRoleRequest request, [FromServices] AuditService audit)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Role name is required." });
        }

        if (request.Name.Length > 100)
        {
            return BadRequest(new { error = "Role name must be 100 characters or fewer." });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsurePermissionAsync(serverId, appUser.Id, Permission.ManageRoles, appUser.IsGlobalAdmin);

        // Check for duplicate name
        var exists = await db.ServerRoles.AnyAsync(r => r.ServerId == serverId && r.Name == request.Name.Trim());
        if (exists)
        {
            return Conflict(new { error = $"A role named '{request.Name.Trim()}' already exists." });
        }

        // New roles are inserted just above the Member role (last system role)
        var maxPosition = await db.ServerRoles
            .Where(r => r.ServerId == serverId)
            .MaxAsync(r => r.Position);

        // Reject undefined/reserved permission bits
        if (request.Permissions is not null && ((Permission)request.Permissions.Value).HasUndefinedBits())
        {
            return BadRequest(new { error = "Request contains undefined permission bits." });
        }

        // Ensure caller cannot grant permissions they don't have
        if (!appUser.IsGlobalAdmin)
        {
            var callerPermsList = await db.ServerMemberRoles
                .Where(mr => mr.UserId == appUser.Id && mr.Role!.ServerId == serverId)
                .Select(mr => mr.Role!.Permissions)
                .ToListAsync();
            var callerEffectivePerms = callerPermsList.Aggregate(Permission.None, (acc, p) => acc | p);

            if (!callerEffectivePerms.Has(Permission.Administrator))
            {
                var requestedPerms = (Permission)(request.Permissions ?? 0);
                if ((requestedPerms & ~callerEffectivePerms) != Permission.None)
                {
                    return Forbid();
                }
            }
        }

        var role = new ServerRoleEntity
        {
            ServerId = serverId,
            Name = request.Name.Trim(),
            Color = request.Color,
            Position = maxPosition, // Insert before the @everyone/Member role
            Permissions = (Permission)(request.Permissions ?? (long)PermissionExtensions.MemberDefaults),
            IsSystemRole = false,
            IsHoisted = request.IsHoisted ?? false,
            IsMentionable = request.IsMentionable ?? false,
        };

        // Shift the Member role down
        var memberSystemRole = await db.ServerRoles
            .FirstOrDefaultAsync(r => r.ServerId == serverId && r.IsSystemRole && r.Name == "Member");
        if (memberSystemRole is not null)
        {
            memberSystemRole.Position = maxPosition + 1;
        }

        db.ServerRoles.Add(role);
        audit.Log(serverId, appUser.Id, AuditAction.RoleCreated,
            targetType: "Role", targetId: role.Id.ToString(),
            details: role.Name);
        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}").SendAsync("RoleCreated", new
        {
            serverId,
            role = new
            {
                role.Id,
                role.Name,
                role.Color,
                role.Position,
                Permissions = (long)role.Permissions,
                role.IsSystemRole,
                role.IsHoisted,
                role.IsMentionable
            }
        });

        return Created($"/servers/{serverId}/roles/{role.Id}", new
        {
            role.Id,
            role.Name,
            role.Color,
            role.Position,
            Permissions = (long)role.Permissions,
            role.IsSystemRole,
            role.IsHoisted,
            role.IsMentionable
        });
    }

    /// <summary>
    /// Updates a custom role's properties. Requires ManageRoles permission.
    /// System roles can have permissions and color updated but not their name.
    /// </summary>
    [HttpPatch("{roleId:guid}")]
    public async Task<IActionResult> UpdateRole(Guid serverId, Guid roleId, [FromBody] UpdateRoleRequest request, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsurePermissionAsync(serverId, appUser.Id, Permission.ManageRoles, appUser.IsGlobalAdmin);

        var role = await db.ServerRoles.FirstOrDefaultAsync(r => r.Id == roleId && r.ServerId == serverId);
        if (role is null)
        {
            return NotFound(new { error = "Role not found." });
        }

        // Cannot edit the Owner role unless you are the owner or global admin
        if (role is { IsSystemRole: true, Position: 0 } && !appUser.IsGlobalAdmin)
        {
            var isOwner = await userService.IsOwnerAsync(serverId, appUser.Id);
            if (!isOwner) return Forbid();
        }

        // Cannot edit roles equal to or higher than your own (unless global admin)
        if (!appUser.IsGlobalAdmin)
        {
            var callerHighest = await permissionResolver.GetHighestRolePositionAsync(serverId, appUser.Id);
            if (role.Position <= callerHighest)
            {
                return Forbid();
            }
        }

        if (request.Name is not null)
        {
            if (role.IsSystemRole)
            {
                return BadRequest(new { error = "Cannot rename a system role." });
            }
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
            {
                return BadRequest(new { error = "Role name must be 1-100 characters." });
            }
            var duplicate = await db.ServerRoles
                .AnyAsync(r => r.ServerId == serverId && r.Id != roleId && r.Name == request.Name.Trim());
            if (duplicate)
            {
                return Conflict(new { error = $"A role named '{request.Name.Trim()}' already exists." });
            }
            role.Name = request.Name.Trim();
        }

        if (request.Color is not null) role.Color = request.Color;
        if (request.Permissions is not null)
        {
            var newPerms = (Permission)request.Permissions.Value;
            if (newPerms.HasUndefinedBits())
            {
                return BadRequest(new { error = "Request contains undefined permission bits." });
            }
            // Cannot grant permissions you don't have (unless global admin)
            if (!appUser.IsGlobalAdmin)
            {
                var callerPerms = await permissionResolver.ResolveServerPermissionsAsync(serverId, appUser.Id);
                if (!callerPerms.Has(Permission.Administrator) && (newPerms & ~callerPerms) != Permission.None)
                {
                    return BadRequest(new { error = "Cannot grant permissions you don't have." });
                }
            }
            role.Permissions = newPerms;
        }
        if (request.IsHoisted is not null) role.IsHoisted = request.IsHoisted.Value;
        if (request.IsMentionable is not null) role.IsMentionable = request.IsMentionable.Value;

        audit.Log(serverId, appUser.Id, AuditAction.RoleUpdated,
            targetType: "Role", targetId: roleId.ToString(),
            details: role.Name);
        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}").SendAsync("RoleUpdated", new
        {
            serverId,
            role = new
            {
                role.Id,
                role.Name,
                role.Color,
                role.Position,
                Permissions = (long)role.Permissions,
                role.IsSystemRole,
                role.IsHoisted,
                role.IsMentionable
            }
        });

        return Ok(new
        {
            role.Id,
            role.Name,
            role.Color,
            role.Position,
            Permissions = (long)role.Permissions,
            role.IsSystemRole,
            role.IsHoisted,
            role.IsMentionable
        });
    }

    /// <summary>
    /// Deletes a custom role. Requires ManageRoles permission.
    /// System roles cannot be deleted. Members with the deleted role are moved to the Member role.
    /// </summary>
    [HttpDelete("{roleId:guid}")]
    public async Task<IActionResult> DeleteRole(Guid serverId, Guid roleId, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsurePermissionAsync(serverId, appUser.Id, Permission.ManageRoles, appUser.IsGlobalAdmin);

        var role = await db.ServerRoles.FirstOrDefaultAsync(r => r.Id == roleId && r.ServerId == serverId);
        if (role is null)
        {
            return NotFound(new { error = "Role not found." });
        }

        if (role.IsSystemRole)
        {
            return BadRequest(new { error = "Cannot delete a system role." });
        }

        // Cannot delete roles equal to or higher than your own
        if (!appUser.IsGlobalAdmin)
        {
            var callerHighest = await permissionResolver.GetHighestRolePositionAsync(serverId, appUser.Id);
            if (role.Position <= callerHighest)
            {
                return Forbid();
            }
        }

        // Remove all ServerMemberRole entries for the deleted role;
        // for users who now have no roles, auto-assign the Member system role
        var memberRole = await db.ServerRoles
            .FirstAsync(r => r.ServerId == serverId && r.IsSystemRole && r.Name == "Member");

        var affectedEntries = await db.ServerMemberRoles
            .Where(mr => mr.RoleId == roleId)
            .ToListAsync();

        var affectedUserIds = affectedEntries.Select(mr => mr.UserId).Distinct().ToList();
        db.ServerMemberRoles.RemoveRange(affectedEntries);

        // After removal, find users who have no remaining roles in this server and give them Member
        foreach (var userId in affectedUserIds)
        {
            var remainingRoleCount = await db.ServerMemberRoles
                .CountAsync(mr => mr.UserId == userId && mr.RoleId != roleId
                    && db.ServerRoles.Any(r => r.Id == mr.RoleId && r.ServerId == serverId));
            if (remainingRoleCount == 0)
            {
                db.ServerMemberRoles.Add(new ServerMemberRole
                {
                    UserId = userId,
                    RoleId = memberRole.Id,
                    AssignedAt = DateTimeOffset.UtcNow
                });
            }
        }

        var roleName = role.Name;
        db.ServerRoles.Remove(role);

        audit.Log(serverId, appUser.Id, AuditAction.RoleDeleted,
            targetType: "Role", targetId: roleId.ToString(),
            details: roleName);
        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}").SendAsync("RoleDeleted", new
        {
            serverId,
            roleId,
            roleName
        });

        return NoContent();
    }

    /// <summary>
    /// Reorders roles within a server. Requires ManageRoles permission.
    /// Owner role (position 0) cannot be moved.
    /// </summary>
    [HttpPut("reorder")]
    public async Task<IActionResult> ReorderRoles(Guid serverId, [FromBody] ReorderRolesRequest request, [FromServices] AuditService audit)
    {
        if (request.RoleIds.Count == 0)
        {
            return BadRequest(new { error = "Role list cannot be empty." });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsurePermissionAsync(serverId, appUser.Id, Permission.ManageRoles, appUser.IsGlobalAdmin);

        var roles = await db.ServerRoles
            .Where(r => r.ServerId == serverId)
            .OrderBy(r => r.Position)
            .ToListAsync();

        // Validate: request must contain exactly all roles (no partial reorders to prevent privilege escalation)
        var serverRoleIds = roles.Select(r => r.Id).ToHashSet();
        var requestRoleIds = request.RoleIds.ToHashSet();
        if (!serverRoleIds.SetEquals(requestRoleIds))
        {
            return BadRequest(new { error = "Role list must contain exactly all server roles." });
        }

        // Validate: Owner role must remain at position 0
        var ownerRole = roles.FirstOrDefault(r => r.IsSystemRole && r.Position == 0);
        if (ownerRole is not null && request.RoleIds.Count > 0 && request.RoleIds[0] != ownerRole.Id)
        {
            return BadRequest(new { error = "Owner role must remain at position 0." });
        }

        // Non-admin/non-owner callers cannot reorder roles at or above their own position
        var isCallerOwner = await permissionResolver.IsOwnerAsync(serverId, appUser.Id);
        if (!appUser.IsGlobalAdmin && !isCallerOwner)
        {
            var callerHighestPosition = await permissionResolver.GetHighestRolePositionAsync(serverId, appUser.Id);
            // Build a map of old positions
            var oldPositionById = roles.ToDictionary(r => r.Id, r => r.Position);
            for (var i = 0; i < request.RoleIds.Count; i++)
            {
                var roleId = request.RoleIds[i];
                var oldPosition = oldPositionById[roleId];
                var newPosition = i;
                // If a role's position changed, both old and new positions must be below (higher number than) the caller's
                if (oldPosition != newPosition &&
                    (oldPosition <= callerHighestPosition || newPosition <= callerHighestPosition))
                {
                    return Forbid();
                }
            }
        }

        // Apply new positions
        for (var i = 0; i < request.RoleIds.Count; i++)
        {
            var role = roles.First(r => r.Id == request.RoleIds[i]);
            role.Position = i;
        }

        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}").SendAsync("RolesReordered", new { serverId });

        return NoContent();
    }
}

public class CreateRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public long? Permissions { get; set; }
    public bool? IsHoisted { get; set; }
    public bool? IsMentionable { get; set; }
}

public class UpdateRoleRequest
{
    public string? Name { get; set; }
    public string? Color { get; set; }
    public long? Permissions { get; set; }
    public bool? IsHoisted { get; set; }
    public bool? IsMentionable { get; set; }
}

public class ReorderRolesRequest
{
    public List<Guid> RoleIds { get; set; } = [];
}
