using Codec.Api.Data;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

/// <summary>
/// Provides online-presence information for server members and DM contacts.
/// </summary>
[ApiController]
[Authorize]
public class PresenceController(CodecDbContext db, IUserService userService, PresenceTracker tracker) : ControllerBase
{
    /// <summary>
    /// Returns the presence status of all online members in the specified server.
    /// </summary>
    [HttpGet("servers/{serverId}/presence")]
    public async Task<IActionResult> GetServerPresence(Guid serverId)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

        // Verify user is a member of this server
        var isMember = await db.ServerMembers
            .AsNoTracking()
            .AnyAsync(sm => sm.ServerId == serverId && sm.UserId == appUser.Id);

        if (!isMember) return Forbid();

        // Get member user IDs
        var memberUserIds = await db.ServerMembers
            .AsNoTracking()
            .Where(sm => sm.ServerId == serverId)
            .Select(sm => sm.UserId)
            .ToListAsync();

        // Get presence for each member from in-memory tracker (only non-offline)
        var presenceList = memberUserIds
            .Select(uid => new
            {
                userId = uid.ToString(),
                status = tracker.GetAggregateStatus(uid).ToString().ToLowerInvariant()
            })
            .Where(p => p.status != "offline")
            .ToList();

        return Ok(presenceList);
    }

    /// <summary>
    /// Returns the presence status of all online DM contacts for the current user.
    /// </summary>
    [HttpGet("dm/presence")]
    public async Task<IActionResult> GetDmPresence()
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

        // Get distinct user IDs from all DM channels the user participates in
        var dmParticipantIds = await db.DmChannelMembers
            .AsNoTracking()
            .Where(m => m.UserId == appUser.Id)
            .SelectMany(m => m.DmChannel!.Members)
            .Where(m => m.UserId != appUser.Id)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync();

        var presenceList = dmParticipantIds
            .Select(uid => new
            {
                userId = uid.ToString(),
                status = tracker.GetAggregateStatus(uid).ToString().ToLowerInvariant()
            })
            .Where(p => p.status != "offline")
            .ToList();

        return Ok(presenceList);
    }
}
