using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
public partial class ServersController(CodecDbContext db, IUserService userService, IAvatarService avatarService, ICustomEmojiService customEmojiService, IHubContext<ChatHub> hub, IHttpClientFactory httpClientFactory, IConfiguration config) : ControllerBase
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
                .ToDictionaryAsync(m => m.ServerId, m => new { Role = m.Role.ToString(), m.SortOrder });

            var result = allServers.Select(s => new
            {
                ServerId = s.Id,
                s.Name,
                s.IconUrl,
                Role = myMemberships.TryGetValue(s.Id, out var info) ? info.Role : (string?)null,
                SortOrder = myMemberships.TryGetValue(s.Id, out var sortInfo) ? sortInfo.SortOrder : int.MaxValue
            }).OrderBy(s => s.SortOrder);

            return Ok(result);
        }

        var servers = await db.ServerMembers
            .AsNoTracking()
            .Where(member => member.UserId == appUser.Id)
            .OrderBy(member => member.SortOrder)
            .Select(member => new
            {
                member.ServerId,
                Name = member.Server!.Name,
                IconUrl = member.Server!.IconUrl,
                Role = member.Role.ToString(),
                member.SortOrder
            })
            .ToListAsync();

        return Ok(servers);
    }

    /// <summary>
    /// Persists the user's custom server display order. Only affects the
    /// authenticated user's sidebar; other users are not impacted.
    /// </summary>
    [HttpPut("reorder")]
    public async Task<IActionResult> ReorderServers([FromBody] ReorderServersRequest request)
    {
        if (request.ServerIds.Count == 0)
        {
            return BadRequest(new { error = "Server list cannot be empty." });
        }

        if (request.ServerIds.Count > 1000)
        {
            return BadRequest(new { error = "Maximum 1000 servers allowed in reorder request." });
        }

        if (request.ServerIds.Count != request.ServerIds.Distinct().Count())
        {
            return BadRequest(new { error = "Duplicate server IDs are not allowed." });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);

        var memberships = await db.ServerMembers
            .Where(m => m.UserId == appUser.Id && request.ServerIds.Contains(m.ServerId))
            .ToListAsync();

        for (var i = 0; i < request.ServerIds.Count; i++)
        {
            var membership = memberships.FirstOrDefault(m => m.ServerId == request.ServerIds[i]);
            if (membership is not null)
            {
                membership.SortOrder = i;
            }
        }

        await db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Creates a new server. The authenticated user becomes the Owner and a
    /// default "general" channel is created automatically.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateServerRequest request)
    {
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
        var appUser = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var server = (await db.Servers.FindAsync(serverId))!;

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
        await userService.EnsureMemberAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

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
        var callerMembership = await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

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
    /// Changes a member's role within a server.
    /// Owner can promote to Admin or demote to Member.
    /// Admin can promote Members to Admin but cannot demote other Admins.
    /// Nobody can change the Owner's role or their own role.
    /// </summary>
    [HttpPatch("{serverId:guid}/members/{targetUserId:guid}/role")]
    public async Task<IActionResult> UpdateMemberRole(Guid serverId, Guid targetUserId, [FromBody] UpdateMemberRoleRequest request)
    {
        if (!Enum.TryParse<ServerRole>(request.Role, ignoreCase: true, out var newRole)
            || newRole is ServerRole.Owner)
        {
            return BadRequest(new { error = "Role must be 'Admin' or 'Member'." });
        }

        var appUser = await userService.GetOrCreateUserAsync(User);
        var callerMembership = await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        if (targetUserId == appUser.Id)
        {
            return BadRequest(new { error = "You cannot change your own role." });
        }

        var targetMembership = await db.ServerMembers
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == targetUserId);

        if (targetMembership is null)
        {
            return NotFound(new { error = "User is not a member of this server." });
        }

        if (targetMembership.Role is ServerRole.Owner)
        {
            return BadRequest(new { error = "Cannot change the server owner's role." });
        }

        // Admins cannot demote other Admins (only Owner/GlobalAdmin can).
        if (!appUser.IsGlobalAdmin
            && callerMembership.Role is ServerRole.Admin
            && targetMembership.Role is ServerRole.Admin
            && newRole is ServerRole.Member)
        {
            return Forbid();
        }

        if (targetMembership.Role == newRole)
        {
            return Ok(new
            {
                targetMembership.UserId,
                Role = targetMembership.Role.ToString(),
                targetMembership.JoinedAt
            });
        }

        targetMembership.Role = newRole;
        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}").SendAsync("MemberRoleChanged", new
        {
            serverId,
            userId = targetUserId,
            newRole = newRole.ToString()
        });

        return Ok(new
        {
            targetMembership.UserId,
            Role = targetMembership.Role.ToString(),
            targetMembership.JoinedAt
        });
    }

    /// <summary>
    /// Lists channels within a server. Requires membership or global admin.
    /// </summary>
    [HttpGet("{serverId:guid}/channels")]
    public async Task<IActionResult> GetChannels(Guid serverId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureMemberAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var channels = await db.Channels
            .AsNoTracking()
            .Where(channel => channel.ServerId == serverId)
            .Select(channel => new { channel.Id, channel.Name, channel.ServerId, Type = channel.Type.ToString().ToLowerInvariant() })
            .ToListAsync();

        return Ok(channels);
    }

    /// <summary>
    /// Creates a channel within a server. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpPost("{serverId:guid}/channels")]
    public async Task<IActionResult> CreateChannel(Guid serverId, [FromBody] CreateChannelRequest request)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        ChannelType channelType;
        if (string.IsNullOrEmpty(request.Type) || string.Equals(request.Type, "text", StringComparison.OrdinalIgnoreCase))
        {
            channelType = ChannelType.Text;
        }
        else if (string.Equals(request.Type, "voice", StringComparison.OrdinalIgnoreCase))
        {
            channelType = ChannelType.Voice;
        }
        else
        {
            return BadRequest(new { error = $"Unsupported channel type '{request.Type}'. Supported values are 'text' and 'voice'." });
        }

        var channel = new Channel
        {
            ServerId = serverId,
            Name = request.Name.Trim(),
            Type = channelType
        };

        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        return Created($"/servers/{serverId}/channels/{channel.Id}", new
        {
            channel.Id,
            channel.Name,
            channel.ServerId,
            Type = channel.Type.ToString().ToLowerInvariant()
        });
    }

    /// <summary>
    /// Updates a channel's name. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpPatch("{serverId:guid}/channels/{channelId:guid}")]
    public async Task<IActionResult> UpdateChannel(Guid serverId, Guid channelId, [FromBody] UpdateChannelRequest request)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

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
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

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
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

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
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

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
        await userService.EnsureOwnerAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        // Bulk-delete the server in a single SQL statement; PostgreSQL cascade
        // deletes handle channels, messages, reactions, link previews, members,
        // invites, custom emojis, and voice states automatically.
        var deleted = await db.Servers.Where(s => s.Id == serverId).ExecuteDeleteAsync();
        if (deleted == 0) return NotFound();

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
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var channel = await db.Channels
            .FirstOrDefaultAsync(c => c.Id == channelId && c.ServerId == serverId);

        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        // Clean up active voice sessions before deleting the channel.
        if (channel.Type == ChannelType.Voice)
        {
            var voiceStates = await db.VoiceStates
                .Where(vs => vs.ChannelId == channelId)
                .ToListAsync();

            if (voiceStates.Count > 0)
            {
                foreach (var vs in voiceStates)
                {
                    await hub.Clients.Group($"server-{serverId}").SendAsync("UserLeftVoice", new
                    {
                        channelId = channelId.ToString(),
                        userId = vs.UserId,
                        participantId = vs.ParticipantId
                    });
                }

                db.VoiceStates.RemoveRange(voiceStates);

                var sfuApiUrl = config["Voice:MediasoupApiUrl"] ?? "http://localhost:3001";
                try
                {
                    using var sfuClient = httpClientFactory.CreateClient("sfu");
                    await sfuClient.DeleteAsync($"{sfuApiUrl}/rooms/{channelId}");
                }
                catch
                {
                    // SFU cleanup is best-effort; channel deletion proceeds regardless.
                }
            }
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
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var server = (await db.Servers.FindAsync(serverId))!;

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
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var server = (await db.Servers.FindAsync(serverId))!;

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

    /* ═══════════════════ Custom Emojis ═══════════════════ */

    /// <summary>Lists all custom emojis for a server. Requires server membership.</summary>
    [HttpGet("{serverId:guid}/emojis")]
    public async Task<IActionResult> ListEmojis(Guid serverId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureMemberAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var emojis = await db.CustomEmojis
            .AsNoTracking()
            .Where(e => e.ServerId == serverId)
            .OrderBy(e => e.Name)
            .Select(e => new
            {
                e.Id, e.Name, e.ImageUrl, e.ContentType, e.IsAnimated,
                e.UploadedByUserId, e.CreatedAt
            })
            .ToListAsync();

        return Ok(emojis);
    }

    /// <summary>Uploads a new custom emoji. Requires Owner or Admin role.</summary>
    [HttpPost("{serverId:guid}/emojis")]
    public async Task<IActionResult> UploadEmoji(
        Guid serverId, [FromForm] string name, IFormFile file)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        // Validate name format.
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_]{2,32}$"))
            return BadRequest(new { error = "Name must be 2-32 alphanumeric/underscore characters." });

        // Validate file.
        var validationError = customEmojiService.Validate(file);
        if (validationError is not null)
            return BadRequest(new { error = validationError });

        // Check 50-emoji limit.
        var count = await db.CustomEmojis.CountAsync(e => e.ServerId == serverId);
        if (count >= 50)
            return BadRequest(new { error = "Server has reached the 50 custom emoji limit." });

        // Check name uniqueness.
        var nameTaken = await db.CustomEmojis.AnyAsync(
            e => e.ServerId == serverId && e.Name == name);
        if (nameTaken)
            return Conflict(new { error = $"An emoji named '{name}' already exists." });

        var imageUrl = await customEmojiService.SaveEmojiAsync(serverId, name, file);
        var isAnimated = file.ContentType is "image/gif";

        var emoji = new CustomEmoji
        {
            ServerId = serverId,
            Name = name,
            ImageUrl = imageUrl,
            ContentType = file.ContentType,
            IsAnimated = isAnimated,
            UploadedByUserId = appUser.Id
        };

        db.CustomEmojis.Add(emoji);
        await db.SaveChangesAsync();

        var payload = new
        {
            emoji.Id, emoji.Name, emoji.ImageUrl, emoji.ContentType,
            emoji.IsAnimated, emoji.UploadedByUserId, emoji.CreatedAt
        };

        await hub.Clients.Group($"server-{serverId}")
            .SendAsync("CustomEmojiAdded", new { serverId, emoji = payload });

        return Created($"/servers/{serverId}/emojis/{emoji.Id}", payload);
    }

    /// <summary>Renames a custom emoji. Requires Owner or Admin role.</summary>
    [HttpPatch("{serverId:guid}/emojis/{emojiId:guid}")]
    public async Task<IActionResult> RenameEmoji(
        Guid serverId, Guid emojiId, [FromBody] RenameEmojiRequest request)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var emoji = await db.CustomEmojis.FirstOrDefaultAsync(
            e => e.Id == emojiId && e.ServerId == serverId);
        if (emoji is null)
            return NotFound(new { error = "Emoji not found." });

        var nameTaken = await db.CustomEmojis.AnyAsync(
            e => e.ServerId == serverId && e.Name == request.Name && e.Id != emojiId);
        if (nameTaken)
            return Conflict(new { error = $"An emoji named '{request.Name}' already exists." });

        emoji.Name = request.Name;
        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}")
            .SendAsync("CustomEmojiUpdated", new { serverId, emojiId, name = request.Name });

        return Ok(new { emoji.Id, emoji.Name, emoji.ImageUrl });
    }

    /// <summary>Deletes a custom emoji. Requires Owner or Admin role.</summary>
    [HttpDelete("{serverId:guid}/emojis/{emojiId:guid}")]
    public async Task<IActionResult> DeleteEmoji(Guid serverId, Guid emojiId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var emoji = await db.CustomEmojis.FirstOrDefaultAsync(
            e => e.Id == emojiId && e.ServerId == serverId);
        if (emoji is null)
            return NotFound(new { error = "Emoji not found." });

        await customEmojiService.DeleteEmojiAsync(emoji.ImageUrl);
        db.CustomEmojis.Remove(emoji);
        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}")
            .SendAsync("CustomEmojiDeleted", new { serverId, emojiId });

        return NoContent();
    }

    /* ═══════════════════ Message Search ═══════════════════ */

    /// <summary>
    /// Searches messages across all channels in a server (or a specific channel).
    /// Supports filtering by author, date range, and content type.
    /// Requires server membership.
    /// </summary>
    [HttpGet("{serverId:guid}/search")]
    public async Task<IActionResult> SearchMessages(Guid serverId, [FromQuery] SearchMessagesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Q) || request.Q.Trim().Length < 2)
        {
            return BadRequest(new { error = "Search query must be at least 2 characters." });
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);

        var appUser = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureMemberAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        // Get all channel IDs in this server.
        var serverChannelIds = await db.Channels
            .AsNoTracking()
            .Where(c => c.ServerId == serverId)
            .Select(c => c.Id)
            .ToListAsync();

        // Determine which channels to search.
        IReadOnlyList<Guid> channelIds;
        if (request.ChannelId.HasValue)
        {
            if (!serverChannelIds.Contains(request.ChannelId.Value))
            {
                return NotFound(new { error = "Channel not found in this server." });
            }
            channelIds = [request.ChannelId.Value];
        }
        else
        {
            channelIds = serverChannelIds;
        }

        // Build base query.
        var searchTerm = $"%{request.Q.Trim()}%";
        var query = db.Messages
            .AsNoTracking()
            .Where(m => channelIds.Contains(m.ChannelId))
            .Where(m => EF.Functions.ILike(m.Body, searchTerm));

        // Apply optional filters.
        if (request.AuthorId.HasValue)
        {
            query = query.Where(m => m.AuthorUserId == request.AuthorId.Value);
        }

        if (request.Before.HasValue)
        {
            query = query.Where(m => m.CreatedAt < request.Before.Value);
        }

        if (request.After.HasValue)
        {
            query = query.Where(m => m.CreatedAt > request.After.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Has))
        {
            if (string.Equals(request.Has, "image", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(m => m.ImageUrl != null);
            }
            else if (string.Equals(request.Has, "link", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(m => db.LinkPreviews.Any(lp => lp.MessageId == m.Id));
            }
        }

        // Count total results.
        var totalCount = await query.CountAsync();

        // Fetch page.
        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.Id,
                m.AuthorName,
                m.AuthorUserId,
                m.Body,
                m.ImageUrl,
                m.CreatedAt,
                m.EditedAt,
                m.ChannelId,
                m.ReplyToMessageId,
                AuthorCustomAvatarPath = m.AuthorUser != null ? m.AuthorUser.CustomAvatarPath : null,
                AuthorGoogleAvatarUrl = m.AuthorUser != null ? m.AuthorUser.AvatarUrl : null
            })
            .ToListAsync();

        var messageIds = messages.Select(m => m.Id).ToArray();

        // Batch-load reactions.
        var reactionLookup = new Dictionary<Guid, IReadOnlyList<SearchReactionSummary>>();
        if (messageIds.Length > 0)
        {
            var reactions = await db.Reactions
                .AsNoTracking()
                .Where(r => r.MessageId != null && messageIds.Contains(r.MessageId.Value))
                .ToListAsync();

            reactionLookup = reactions
                .GroupBy(r => r.MessageId!.Value)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<SearchReactionSummary>)group
                        .GroupBy(r => r.Emoji)
                        .Select(emojiGroup => new SearchReactionSummary(
                            emojiGroup.Key,
                            emojiGroup.Count(),
                            emojiGroup.Select(r => r.UserId).ToList()))
                        .ToList());
        }

        // Batch-load link previews.
        var linkPreviewLookup = new Dictionary<Guid, IReadOnlyList<SearchLinkPreviewDto>>();
        if (messageIds.Length > 0)
        {
            var linkPreviews = await db.LinkPreviews
                .AsNoTracking()
                .Where(lp => lp.MessageId != null && messageIds.Contains(lp.MessageId.Value)
                    && lp.Status == LinkPreviewStatus.Success)
                .ToListAsync();

            linkPreviewLookup = linkPreviews
                .GroupBy(lp => lp.MessageId!.Value)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<SearchLinkPreviewDto>)group
                        .Select(lp => new SearchLinkPreviewDto(lp.Url, lp.Title, lp.Description, lp.ImageUrl, lp.SiteName, lp.CanonicalUrl))
                        .ToList());
        }

        // Resolve mentions for all messages.
        var mentionsByMessage = messages.ToDictionary(m => m.Id, m => ParseSearchMentionUserIds(m.Body));
        var allMentionedIds = mentionsByMessage.Values
            .SelectMany(ids => ids)
            .Distinct()
            .ToList();

        var mentionUserLookup = new Dictionary<Guid, SearchMentionDto>();
        if (allMentionedIds.Count > 0)
        {
            mentionUserLookup = await db.Users
                .AsNoTracking()
                .Where(u => allMentionedIds.Contains(u.Id))
                .ToDictionaryAsync(
                    u => u.Id,
                    u => new SearchMentionDto(u.Id, string.IsNullOrWhiteSpace(u.Nickname) ? u.DisplayName : u.Nickname));
        }

        // Batch-load reply context.
        var replyToIds = messages
            .Where(m => m.ReplyToMessageId.HasValue)
            .Select(m => m.ReplyToMessageId!.Value)
            .Distinct()
            .ToList();

        var replyContextLookup = new Dictionary<Guid, SearchReplyContextDto>();
        if (replyToIds.Count > 0)
        {
            var parentMessages = await db.Messages
                .AsNoTracking()
                .Where(m => replyToIds.Contains(m.Id))
                .Select(m => new
                {
                    m.Id,
                    m.AuthorName,
                    m.AuthorUserId,
                    AuthorCustomAvatarPath = m.AuthorUser != null ? m.AuthorUser.CustomAvatarPath : null,
                    AuthorGoogleAvatarUrl = m.AuthorUser != null ? m.AuthorUser.AvatarUrl : null,
                    m.Body
                })
                .ToListAsync();

            replyContextLookup = parentMessages.ToDictionary(
                p => p.Id,
                p => new SearchReplyContextDto(
                    p.Id,
                    p.AuthorName,
                    avatarService.ResolveUrl(p.AuthorCustomAvatarPath) ?? p.AuthorGoogleAvatarUrl,
                    p.AuthorUserId,
                    p.Body.Length > 100 ? p.Body[..100] : p.Body,
                    false));
        }

        // Load channel names for results.
        var resultChannelIds = messages.Select(m => m.ChannelId).Distinct().ToList();
        var channelNameLookup = await db.Channels
            .AsNoTracking()
            .Where(c => resultChannelIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        // Build enriched response.
        var results = messages.Select(message =>
        {
            var mentionIds = mentionsByMessage.TryGetValue(message.Id, out var cached) ? cached : [];
            var mentions = mentionIds
                .Where(id => mentionUserLookup.ContainsKey(id))
                .Select(id => mentionUserLookup[id])
                .ToList();

            return new
            {
                message.Id,
                message.ChannelId,
                ChannelName = channelNameLookup.TryGetValue(message.ChannelId, out var chName) ? chName : null,
                message.AuthorName,
                message.AuthorUserId,
                AuthorAvatarUrl = avatarService.ResolveUrl(message.AuthorCustomAvatarPath) ?? message.AuthorGoogleAvatarUrl,
                message.Body,
                message.ImageUrl,
                message.CreatedAt,
                message.EditedAt,
                Reactions = reactionLookup.TryGetValue(message.Id, out var reactions)
                    ? reactions
                    : Array.Empty<SearchReactionSummary>(),
                LinkPreviews = linkPreviewLookup.TryGetValue(message.Id, out var previews)
                    ? previews
                    : Array.Empty<SearchLinkPreviewDto>(),
                Mentions = (IReadOnlyList<SearchMentionDto>)mentions,
                ReplyContext = message.ReplyToMessageId.HasValue
                    ? replyContextLookup.TryGetValue(message.ReplyToMessageId.Value, out var ctx)
                        ? ctx
                        : new SearchReplyContextDto(message.ReplyToMessageId.Value, string.Empty, null, null, string.Empty, true)
                    : (SearchReplyContextDto?)null
            };
        });

        return Ok(new { totalCount, page, pageSize, results });
    }

    private static List<Guid> ParseSearchMentionUserIds(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];

        var matches = SearchMentionRegex().Matches(body);
        var ids = new HashSet<Guid>();
        foreach (Match match in matches)
        {
            if (Guid.TryParse(match.Groups[1].Value, out var userId))
            {
                ids.Add(userId);
            }
        }
        return [.. ids];
    }

    [GeneratedRegex(@"<@([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})>")]
    private static partial Regex SearchMentionRegex();

    private sealed record SearchReactionSummary(string Emoji, int Count, IReadOnlyList<Guid> UserIds);
    private sealed record SearchLinkPreviewDto(string Url, string? Title, string? Description, string? ImageUrl, string? SiteName, string? CanonicalUrl);
    private sealed record SearchMentionDto(Guid UserId, string DisplayName);
    private sealed record SearchReplyContextDto(Guid MessageId, string AuthorName, string? AuthorAvatarUrl, Guid? AuthorUserId, string BodyPreview, bool IsDeleted);

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
