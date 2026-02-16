using System.Security.Cryptography;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Codec.Api.Hubs;

namespace Codec.Api.Controllers;

/// <summary>
/// Manages servers, memberships, and server-scoped channels.
/// </summary>
[ApiController]
[Authorize]
[Route("servers")]
public class ServersController(CodecDbContext db, IUserService userService, IAvatarService avatarService, IHubContext<ChatHub> hub) : ControllerBase
{
    /// <summary>
    /// Lists servers the current user is a member of.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMyServers()
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var servers = await db.ServerMembers
            .AsNoTracking()
            .Where(member => member.UserId == appUser.Id)
            .Select(member => new
            {
                member.ServerId,
                Name = member.Server!.Name,
                Role = member.Role.ToString()
            })
            .ToListAsync();

        return Ok(servers);
    }

    /// <summary>
    /// Creates a new server. The authenticated user becomes the Owner and a
    /// default "general" channel is created automatically.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateServerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Server name is required." });
        }

        if (request.Name.Trim().Length > 100)
        {
            return BadRequest(new { error = "Server name must be 100 characters or fewer." });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);

        var server = new Server { Name = request.Name.Trim() };
        db.Servers.Add(server);

        var defaultChannel = new Channel { Name = "general", Server = server };
        db.Channels.Add(defaultChannel);

        var membership = new ServerMember
        {
            Server = server,
            UserId = appUser.Id,
            Role = ServerRole.Owner,
            JoinedAt = DateTimeOffset.UtcNow
        };
        db.ServerMembers.Add(membership);

        await db.SaveChangesAsync();

        return Created($"/servers/{server.Id}", new
        {
            server.Id,
            server.Name,
            role = membership.Role.ToString()
        });
    }

    /// <summary>
    /// Updates a server's name. Requires Owner or Admin role.
    /// </summary>
    [HttpPatch("{serverId:guid}")]
    public async Task<IActionResult> UpdateServer(Guid serverId, [FromBody] UpdateServerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Server name is required." });
        }

        if (request.Name.Trim().Length > 100)
        {
            return BadRequest(new { error = "Server name must be 100 characters or fewer." });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);
        var membership = await db.ServerMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

        if (membership is null)
        {
            return Forbid();
        }

        if (membership.Role is not (ServerRole.Owner or ServerRole.Admin))
        {
            return Forbid();
        }

        var server = await db.Servers.FindAsync(serverId);
        if (server is null)
        {
            return NotFound(new { error = "Server not found." });
        }

        var oldName = server.Name;
        server.Name = request.Name.Trim();
        await db.SaveChangesAsync();

        // Notify all server members of the name change via SignalR.
        await hub.Clients.Group($"server-{serverId}").SendAsync("ServerNameChanged", new
        {
            serverId,
            name = server.Name
        });

        return Ok(new
        {
            server.Id,
            server.Name
        });
    }

    /// <summary>
    /// Lists the members of a server. Requires membership.
    /// </summary>
    [HttpGet("{serverId:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid serverId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var isMember = await userService.IsMemberAsync(serverId, appUser.Id);
        if (!isMember)
        {
            return Forbid();
        }

        var members = await db.ServerMembers
            .AsNoTracking()
            .Where(member => member.ServerId == serverId)
            .Select(member => new
            {
                member.UserId,
                Role = member.Role.ToString(),
                member.JoinedAt,
                member.User!.DisplayName,
                Nickname = member.User.Nickname,
                member.User.Email,
                member.User.AvatarUrl,
                member.User.CustomAvatarPath,
                ServerCustomAvatarPath = member.CustomAvatarPath
            })
            .OrderBy(member => member.DisplayName)
            .ToListAsync();

        var result = members.Select(member =>
        {
            var effectiveName = string.IsNullOrWhiteSpace(member.Nickname) ? member.DisplayName : member.Nickname;
            return new
            {
                member.UserId,
                member.Role,
                member.JoinedAt,
                DisplayName = effectiveName,
                member.Email,
                AvatarUrl = avatarService.ResolveUrl(member.ServerCustomAvatarPath)
                         ?? avatarService.ResolveUrl(member.CustomAvatarPath)
                         ?? member.AvatarUrl
            };
        });

        return Ok(result);
    }

    /// <summary>
    /// Kicks a user from a server. Requires Owner or Admin role.
    /// Owners can kick anyone except themselves; Admins can only kick Members.
    /// </summary>
    [HttpDelete("{serverId:guid}/members/{targetUserId:guid}")]
    public async Task<IActionResult> KickMember(Guid serverId, Guid targetUserId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var callerMembership = await db.ServerMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

        if (callerMembership is null)
        {
            return Forbid();
        }

        if (callerMembership.Role is not (ServerRole.Owner or ServerRole.Admin))
        {
            return Forbid();
        }

        if (targetUserId == appUser.Id)
        {
            return BadRequest(new { error = "You cannot kick yourself." });
        }

        var targetMembership = await db.ServerMembers
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == targetUserId);

        if (targetMembership is null)
        {
            return NotFound(new { error = "User is not a member of this server." });
        }

        if (targetMembership.Role is ServerRole.Owner)
        {
            return BadRequest(new { error = "Cannot kick the server owner." });
        }

        if (callerMembership.Role is ServerRole.Admin && targetMembership.Role is ServerRole.Admin)
        {
            return Forbid();
        }

        db.ServerMembers.Remove(targetMembership);
        await db.SaveChangesAsync();

        // Notify the kicked user in real-time so their client can update.
        var serverName = await db.Servers
            .AsNoTracking()
            .Where(s => s.Id == serverId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync() ?? "Unknown";

        await hub.Clients.Group($"user-{targetUserId}").SendAsync("KickedFromServer", new
        {
            serverId,
            serverName
        });

        // Notify remaining members so they can update their member list.
        await hub.Clients.Group($"server-{serverId}").SendAsync("MemberLeft", new
        {
            serverId,
            userId = targetUserId
        });

        return NoContent();
    }

    /// <summary>
    /// Lists channels within a server. Requires membership.
    /// </summary>
    [HttpGet("{serverId:guid}/channels")]
    public async Task<IActionResult> GetChannels(Guid serverId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var isMember = await userService.IsMemberAsync(serverId, appUser.Id);
        if (!isMember)
        {
            return Forbid();
        }

        var channels = await db.Channels
            .AsNoTracking()
            .Where(channel => channel.ServerId == serverId)
            .Select(channel => new { channel.Id, channel.Name, channel.ServerId })
            .ToListAsync();

        return Ok(channels);
    }

    /// <summary>
    /// Creates a channel within a server. Requires Owner or Admin role.
    /// </summary>
    [HttpPost("{serverId:guid}/channels")]
    public async Task<IActionResult> CreateChannel(Guid serverId, [FromBody] CreateChannelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Channel name is required." });
        }

        if (request.Name.Trim().Length > 100)
        {
            return BadRequest(new { error = "Channel name must be 100 characters or fewer." });
        }

        var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
        if (!serverExists)
        {
            return NotFound(new { error = "Server not found." });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);
        var membership = await db.ServerMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

        if (membership is null)
        {
            return Forbid();
        }

        if (membership.Role is not (ServerRole.Owner or ServerRole.Admin))
        {
            return Forbid();
        }

        var channel = new Channel
        {
            ServerId = serverId,
            Name = request.Name.Trim()
        };

        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        return Created($"/servers/{serverId}/channels/{channel.Id}", new
        {
            channel.Id,
            channel.Name,
            channel.ServerId
        });
    }

    /// <summary>
    /// Updates a channel's name. Requires Owner or Admin role.
    /// </summary>
    [HttpPatch("{serverId:guid}/channels/{channelId:guid}")]
    public async Task<IActionResult> UpdateChannel(Guid serverId, Guid channelId, [FromBody] UpdateChannelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Channel name is required." });
        }

        if (request.Name.Trim().Length > 100)
        {
            return BadRequest(new { error = "Channel name must be 100 characters or fewer." });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);
        var membership = await db.ServerMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

        if (membership is null)
        {
            return Forbid();
        }

        if (membership.Role is not (ServerRole.Owner or ServerRole.Admin))
        {
            return Forbid();
        }

        var channel = await db.Channels
            .FirstOrDefaultAsync(c => c.Id == channelId && c.ServerId == serverId);

        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        channel.Name = request.Name.Trim();
        await db.SaveChangesAsync();

        // Notify all server members of the channel name change via SignalR.
        await hub.Clients.Group($"server-{serverId}").SendAsync("ChannelNameChanged", new
        {
            serverId,
            channelId,
            name = channel.Name
        });

        return Ok(new
        {
            channel.Id,
            channel.Name,
            channel.ServerId
        });
    }

    /* ═══════════════════ Server Invites ═══════════════════ */

    /// <summary>
    /// Creates a new invite code for a server. Requires Owner or Admin role.
    /// </summary>
    [HttpPost("{serverId:guid}/invites")]
    public async Task<IActionResult> CreateInvite(Guid serverId, [FromBody] CreateInviteRequest request)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var membership = await db.ServerMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

        if (membership is null)
        {
            return Forbid();
        }

        if (membership.Role is not (ServerRole.Owner or ServerRole.Admin))
        {
            return Forbid();
        }

        var serverExists = await db.Servers.AsNoTracking().AnyAsync(s => s.Id == serverId);
        if (!serverExists)
        {
            return NotFound(new { error = "Server not found." });
        }

        DateTimeOffset? expiresAt = request.ExpiresInHours switch
        {
            null => DateTimeOffset.UtcNow.AddDays(7),
            0 => null,
            > 0 => DateTimeOffset.UtcNow.AddHours(request.ExpiresInHours.Value),
            _ => DateTimeOffset.UtcNow.AddDays(7)
        };

        var code = GenerateInviteCode();

        var invite = new ServerInvite
        {
            ServerId = serverId,
            Code = code,
            CreatedByUserId = appUser.Id,
            ExpiresAt = expiresAt,
            MaxUses = request.MaxUses is null or 0 ? null : request.MaxUses,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.ServerInvites.Add(invite);
        await db.SaveChangesAsync();

        return Created($"/servers/{serverId}/invites/{invite.Id}", new
        {
            invite.Id,
            invite.ServerId,
            invite.Code,
            invite.ExpiresAt,
            invite.MaxUses,
            invite.UseCount,
            invite.CreatedAt,
            CreatedByUserId = appUser.Id
        });
    }

    /// <summary>
    /// Lists active invite codes for a server. Requires Owner or Admin role.
    /// </summary>
    [HttpGet("{serverId:guid}/invites")]
    public async Task<IActionResult> GetInvites(Guid serverId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var membership = await db.ServerMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

        if (membership is null)
        {
            return Forbid();
        }

        if (membership.Role is not (ServerRole.Owner or ServerRole.Admin))
        {
            return Forbid();
        }

        var now = DateTimeOffset.UtcNow;
        var invites = await db.ServerInvites
            .AsNoTracking()
            .Where(i => i.ServerId == serverId)
            .Where(i => i.ExpiresAt == null || i.ExpiresAt > now)
            .Select(i => new
            {
                i.Id,
                i.ServerId,
                i.Code,
                i.ExpiresAt,
                i.MaxUses,
                i.UseCount,
                i.CreatedAt,
                i.CreatedByUserId
            })
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return Ok(invites);
    }

    /// <summary>
    /// Revokes (deletes) an invite code. Requires Owner or Admin role.
    /// </summary>
    [HttpDelete("{serverId:guid}/invites/{inviteId:guid}")]
    public async Task<IActionResult> RevokeInvite(Guid serverId, Guid inviteId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var membership = await db.ServerMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

        if (membership is null)
        {
            return Forbid();
        }

        if (membership.Role is not (ServerRole.Owner or ServerRole.Admin))
        {
            return Forbid();
        }

        var invite = await db.ServerInvites
            .FirstOrDefaultAsync(i => i.Id == inviteId && i.ServerId == serverId);

        if (invite is null)
        {
            return NotFound(new { error = "Invite not found." });
        }

        db.ServerInvites.Remove(invite);
        await db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Joins a server via an invite code. Any authenticated user can use this.
    /// Uses an absolute route to break out of the "servers" prefix.
    /// </summary>
    [HttpPost("/invites/{code}")]
    public async Task<IActionResult> JoinViaInvite(string code)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var now = DateTimeOffset.UtcNow;

        var invite = await db.ServerInvites
            .FirstOrDefaultAsync(i => i.Code == code);

        if (invite is null)
        {
            return NotFound(new { error = "Invalid invite code." });
        }

        if (invite.ExpiresAt is not null && invite.ExpiresAt <= now)
        {
            return BadRequest(new { error = "This invite has expired." });
        }

        if (invite.MaxUses is not null && invite.UseCount >= invite.MaxUses)
        {
            return BadRequest(new { error = "This invite has reached maximum uses." });
        }

        var existingMembership = await db.ServerMembers
            .FindAsync(invite.ServerId, appUser.Id);

        if (existingMembership is not null)
        {
            return Ok(new
            {
                serverId = invite.ServerId,
                userId = appUser.Id,
                role = existingMembership.Role.ToString()
            });
        }

        var membership = new ServerMember
        {
            ServerId = invite.ServerId,
            UserId = appUser.Id,
            Role = ServerRole.Member,
            JoinedAt = DateTimeOffset.UtcNow
        };

        db.ServerMembers.Add(membership);
        invite.UseCount++;
        await db.SaveChangesAsync();

        // Notify existing server members that a new member joined.
        await hub.Clients.Group($"server-{invite.ServerId}").SendAsync("MemberJoined", new
        {
            serverId = invite.ServerId
        });

        return Created($"/servers/{invite.ServerId}/members/{appUser.Id}", new
        {
            serverId = invite.ServerId,
            userId = appUser.Id,
            role = membership.Role.ToString()
        });
    }

    private static string GenerateInviteCode()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return string.Create(8, bytes.ToArray(), static (span, b) =>
        {
            const string c = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            for (var i = 0; i < span.Length; i++)
                span[i] = c[b[i] % c.Length];
        });
    }
}
