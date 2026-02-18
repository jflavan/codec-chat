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
    /// Global admins see all servers.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMyServers()
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        if (appUser.IsGlobalAdmin)
        {
            var allServers = await db.Servers
                .AsNoTracking()
                .Select(s => new { s.Id, s.Name, s.IconUrl })
                .ToListAsync();

            var myMemberships = await db.ServerMembers
                .AsNoTracking()
                .Where(m => m.UserId == appUser.Id)
                .ToDictionaryAsync(m => m.ServerId, m => m.Role.ToString());

            var result = allServers.Select(s => new
            {
                ServerId = s.Id,
                s.Name,
                s.IconUrl,
                Role = myMemberships.TryGetValue(s.Id, out var role) ? role : (string?)null
            });

            return Ok(result);
        }

        var servers = await db.ServerMembers
            .AsNoTracking()
            .Where(member => member.UserId == appUser.Id)
            .Select(member => new
            {
                member.ServerId,
                Name = member.Server!.Name,
                IconUrl = member.Server!.IconUrl,
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
            server.IconUrl,
            role = membership.Role.ToString()
        });
    }

    /// <summary>
    /// Updates a server's name. Requires Owner, Admin, or global admin role.
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

        if (!appUser.IsGlobalAdmin)
        {
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
            server.Name,
            server.IconUrl
        });
    }

    /// <summary>
    /// Lists the members of a server. Requires membership or global admin.
    /// </summary>
    [HttpGet("{serverId:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid serverId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var isMember = appUser.IsGlobalAdmin || await userService.IsMemberAsync(serverId, appUser.Id);
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
    /// Kicks a user from a server. Requires Owner, Admin, or global admin role.
    /// Owners and global admins can kick anyone except themselves and the server owner;
    /// Admins can only kick Members.
    /// </summary>
    [HttpDelete("{serverId:guid}/members/{targetUserId:guid}")]
    public async Task<IActionResult> KickMember(Guid serverId, Guid targetUserId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        ServerRole? callerRole = null;
        if (!appUser.IsGlobalAdmin)
        {
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

            callerRole = callerMembership.Role;
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

        if (callerRole is ServerRole.Admin && targetMembership.Role is ServerRole.Admin)
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
    /// Lists channels within a server. Requires membership or global admin.
    /// </summary>
    [HttpGet("{serverId:guid}/channels")]
    public async Task<IActionResult> GetChannels(Guid serverId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var isMember = appUser.IsGlobalAdmin || await userService.IsMemberAsync(serverId, appUser.Id);
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
    /// Creates a channel within a server. Requires Owner, Admin, or global admin role.
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

        if (!appUser.IsGlobalAdmin)
        {
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
    /// Updates a channel's name. Requires Owner, Admin, or global admin role.
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

        if (!appUser.IsGlobalAdmin)
        {
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
    /// Creates a new invite code for a server. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpPost("{serverId:guid}/invites")]
    public async Task<IActionResult> CreateInvite(Guid serverId, [FromBody] CreateInviteRequest request)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        if (!appUser.IsGlobalAdmin)
        {
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
    /// Lists active invite codes for a server. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpGet("{serverId:guid}/invites")]
    public async Task<IActionResult> GetInvites(Guid serverId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        if (!appUser.IsGlobalAdmin)
        {
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
    /// Revokes (deletes) an invite code. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpDelete("{serverId:guid}/invites/{inviteId:guid}")]
    public async Task<IActionResult> RevokeInvite(Guid serverId, Guid inviteId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        if (!appUser.IsGlobalAdmin)
        {
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

    /// <summary>
    /// Deletes a server and all associated data. Requires server Owner role or global admin privileges.
    /// </summary>
    [HttpDelete("{serverId:guid}")]
    public async Task<IActionResult> DeleteServer(Guid serverId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        if (!appUser.IsGlobalAdmin)
        {
            var membership = await db.ServerMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

            if (membership is null || membership.Role is not ServerRole.Owner)
            {
                return Forbid();
            }
        }

        var server = await db.Servers
            .Include(s => s.Channels)
            .Include(s => s.Members)
            .Include(s => s.Invites)
            .FirstOrDefaultAsync(s => s.Id == serverId);

        if (server is null)
        {
            return NotFound(new { error = "Server not found." });
        }

        // Delete all messages and their associated data in server channels.
        var channelIds = server.Channels.Select(c => c.Id).ToList();
        if (channelIds.Count > 0)
        {
            var linkPreviews = await db.LinkPreviews
                .Where(lp => lp.MessageId != null && db.Messages.Any(m => channelIds.Contains(m.ChannelId) && m.Id == lp.MessageId))
                .ToListAsync();
            db.LinkPreviews.RemoveRange(linkPreviews);

            var reactions = await db.Reactions
                .Where(r => db.Messages.Any(m => channelIds.Contains(m.ChannelId) && m.Id == r.MessageId))
                .ToListAsync();
            db.Reactions.RemoveRange(reactions);

            var messages = await db.Messages
                .Where(m => channelIds.Contains(m.ChannelId))
                .ToListAsync();
            db.Messages.RemoveRange(messages);
        }

        db.Servers.Remove(server);
        await db.SaveChangesAsync();

        // Notify all connected clients that the server was deleted.
        await hub.Clients.Group($"server-{serverId}").SendAsync("ServerDeleted", new
        {
            serverId
        });

        return NoContent();
    }

    /// <summary>
    /// Deletes a channel within a server. Requires Owner/Admin role or global admin privileges.
    /// Cascade-deletes all messages, reactions, and link previews in the channel.
    /// </summary>
    [HttpDelete("{serverId:guid}/channels/{channelId:guid}")]
    public async Task<IActionResult> DeleteChannel(Guid serverId, Guid channelId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        if (!appUser.IsGlobalAdmin)
        {
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
        }

        var channel = await db.Channels
            .FirstOrDefaultAsync(c => c.Id == channelId && c.ServerId == serverId);

        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        // Delete associated data before removing the channel.
        var linkPreviews = await db.LinkPreviews
            .Where(lp => lp.MessageId != null && db.Messages.Any(m => m.ChannelId == channelId && m.Id == lp.MessageId))
            .ToListAsync();
        db.LinkPreviews.RemoveRange(linkPreviews);

        var reactions = await db.Reactions
            .Where(r => db.Messages.Any(m => m.ChannelId == channelId && m.Id == r.MessageId))
            .ToListAsync();
        db.Reactions.RemoveRange(reactions);

        var messages = await db.Messages
            .Where(m => m.ChannelId == channelId)
            .ToListAsync();
        db.Messages.RemoveRange(messages);

        db.Channels.Remove(channel);
        await db.SaveChangesAsync();

        // Notify all server members of the channel deletion via SignalR.
        await hub.Clients.Group($"server-{serverId}").SendAsync("ChannelDeleted", new
        {
            serverId,
            channelId
        });

        return NoContent();
    }

    /* ═══════════════════ Server Icon ═══════════════════ */

    /// <summary>
    /// Uploads or updates the server icon image. Requires Owner, Admin, or global admin role.
    /// Accepts JPG, JPEG, PNG, WebP, or GIF files up to 10 MB.
    /// </summary>
    [HttpPost("{serverId:guid}/icon")]
    public async Task<IActionResult> UploadServerIcon(Guid serverId, IFormFile file)
    {
        var validationError = avatarService.Validate(file);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);

        if (!appUser.IsGlobalAdmin)
        {
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
        }

        var server = await db.Servers.FindAsync(serverId);
        if (server is null)
        {
            return NotFound(new { error = "Server not found." });
        }

        // Remove the previous icon file if one exists.
        if (!string.IsNullOrEmpty(server.IconUrl))
        {
            await avatarService.DeleteServerIconAsync(serverId);
        }

        var iconUrl = await avatarService.SaveServerIconAsync(serverId, file);
        server.IconUrl = iconUrl;
        await db.SaveChangesAsync();

        // Notify all server members of the icon change via SignalR.
        await hub.Clients.Group($"server-{serverId}").SendAsync("ServerIconChanged", new
        {
            serverId,
            iconUrl
        });

        return Ok(new { iconUrl });
    }

    /// <summary>
    /// Removes the server icon. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpDelete("{serverId:guid}/icon")]
    public async Task<IActionResult> DeleteServerIcon(Guid serverId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);

        if (!appUser.IsGlobalAdmin)
        {
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
        }

        var server = await db.Servers.FindAsync(serverId);
        if (server is null)
        {
            return NotFound(new { error = "Server not found." });
        }

        if (!string.IsNullOrEmpty(server.IconUrl))
        {
            await avatarService.DeleteServerIconAsync(serverId);
            server.IconUrl = null;
            await db.SaveChangesAsync();

            // Notify all server members of the icon removal via SignalR.
            await hub.Clients.Group($"server-{serverId}").SendAsync("ServerIconChanged", new
            {
                serverId,
                iconUrl = (string?)null
            });
        }

        return NoContent();
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
