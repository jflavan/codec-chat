using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Codec.Api.Filters;
using Codec.Api.Hubs;

namespace Codec.Api.Controllers;

/// <summary>
/// Manages servers, memberships, and server-scoped channels.
/// </summary>
[ApiController]
[Authorize]
[RequireEmailVerified]
[Route("servers")]
public partial class ServersController(CodecDbContext db, IUserService userService, IAvatarService avatarService, ICustomEmojiService customEmojiService, IHubContext<ChatHub> hub, IHttpClientFactory httpClientFactory, IConfiguration config, MessageCacheService messageCache, WebhookService webhookService) : ControllerBase
{
    /// <summary>
    /// Lists servers the current user is a member of.
    /// Global admins see all servers.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMyServers()
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

        if (appUser.IsGlobalAdmin)
        {
            var allServers = await db.Servers
                .AsNoTracking()
                .Select(s => new { s.Id, s.Name, s.IconUrl, s.Description })
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
                s.Description,
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
                Description = member.Server!.Description,
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

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

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
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

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
    public async Task<IActionResult> UpdateServer(Guid serverId, [FromBody] UpdateServerRequest request, [FromServices] AuditService audit)
    {
        if (request.Name is null && request.Description is null)
        {
            return BadRequest(new { error = "At least one of Name or Description must be provided." });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var server = (await db.Servers.FindAsync(serverId))!;

        bool nameChanged = false;
        bool descriptionChanged = false;
        string? oldName = null;

        if (request.Name is not null && request.Name.Trim() != server.Name)
        {
            oldName = server.Name;
            server.Name = request.Name.Trim();
            nameChanged = true;
        }

        if (request.Description is not null)
        {
            server.Description = request.Description;
            descriptionChanged = true;
        }

        await db.SaveChangesAsync();

        if (nameChanged)
        {
            // Notify all server members of the name change via SignalR.
            await hub.Clients.Group($"server-{serverId}").SendAsync("ServerNameChanged", new
            {
                serverId,
                name = server.Name
            });
            audit.Log(serverId, appUser.Id, AuditAction.ServerRenamed,
                details: $"Renamed from \"{oldName}\" to \"{server.Name}\"");
        }

        if (descriptionChanged)
        {
            await hub.Clients.Group($"server-{serverId}").SendAsync("ServerDescriptionChanged", new
            {
                serverId,
                description = server.Description
            });
            audit.Log(serverId, appUser.Id, AuditAction.ServerDescriptionChanged);
        }

        if (nameChanged || descriptionChanged)
        {
            await db.SaveChangesAsync();
        }

        return Ok(new
        {
            server.Id,
            server.Name,
            server.IconUrl,
            server.Description
        });
    }

    /// <summary>
    /// Lists the members of a server. Requires membership or global admin.
    /// </summary>
    [HttpGet("{serverId:guid}/members")]
    public async Task<IActionResult> GetMembers(Guid serverId)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
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
                ServerCustomAvatarPath = member.CustomAvatarPath,
                member.User.StatusText,
                member.User.StatusEmoji
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
                         ?? member.AvatarUrl,
                member.StatusText,
                member.StatusEmoji
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
    public async Task<IActionResult> KickMember(Guid serverId, Guid targetUserId, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
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

        var kickedUserDisplayName = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == targetUserId)
            .Select(u => u.Nickname != null && u.Nickname != "" ? u.Nickname : u.DisplayName)
            .FirstOrDefaultAsync() ?? "Unknown";

        db.ServerMembers.Remove(targetMembership);
        audit.Log(serverId, appUser.Id, AuditAction.MemberKicked,
            targetType: "User", targetId: targetUserId.ToString(),
            details: kickedUserDisplayName);
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

        webhookService.DispatchEvent(serverId, WebhookEventType.MemberLeft, new
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
    public async Task<IActionResult> UpdateMemberRole(Guid serverId, Guid targetUserId, [FromBody] UpdateMemberRoleRequest request, [FromServices] AuditService audit)
    {
        if (!Enum.TryParse<ServerRole>(request.Role, ignoreCase: true, out var newRole)
            || newRole is ServerRole.Owner)
        {
            return BadRequest(new { error = "Role must be 'Admin' or 'Member'." });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
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

        var targetDisplayName = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == targetUserId)
            .Select(u => u.Nickname != null && u.Nickname != "" ? u.Nickname : u.DisplayName)
            .FirstOrDefaultAsync() ?? "Unknown";

        targetMembership.Role = newRole;
        audit.Log(serverId, appUser.Id, AuditAction.MemberRoleChanged,
            targetType: "User", targetId: targetUserId.ToString(),
            details: $"Changed @{targetDisplayName} role to {newRole}");
        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}").SendAsync("MemberRoleChanged", new
        {
            serverId,
            userId = targetUserId,
            newRole = newRole.ToString()
        });

        webhookService.DispatchEvent(serverId, WebhookEventType.MemberRoleChanged, new
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
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureMemberAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var channels = await db.Channels
            .AsNoTracking()
            .Where(channel => channel.ServerId == serverId)
            .Select(channel => new
            {
                channel.Id,
                channel.Name,
                channel.ServerId,
                Type = channel.Type.ToString().ToLowerInvariant(),
                channel.Description,
                channel.CategoryId,
                channel.Position
            })
            .ToListAsync();

        return Ok(channels);
    }

    /// <summary>
    /// Creates a channel within a server. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpPost("{serverId:guid}/channels")]
    public async Task<IActionResult> CreateChannel(Guid serverId, [FromBody] CreateChannelRequest request, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
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
        audit.Log(serverId, appUser.Id, AuditAction.ChannelCreated,
            targetType: "Channel", targetId: channel.Id.ToString(),
            details: channel.Name);
        await db.SaveChangesAsync();

        webhookService.DispatchEvent(serverId, WebhookEventType.ChannelCreated, new
        {
            channelId = channel.Id,
            serverId,
            name = channel.Name,
            type = channel.Type.ToString().ToLowerInvariant()
        });

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
    public async Task<IActionResult> UpdateChannel(Guid serverId, Guid channelId, [FromBody] UpdateChannelRequest request, [FromServices] AuditService audit)
    {
        if (request.Name is null && request.Description is null)
        {
            return BadRequest(new { error = "At least one of Name or Description must be provided." });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var channel = await db.Channels
            .FirstOrDefaultAsync(c => c.Id == channelId && c.ServerId == serverId);

        if (channel is null)
        {
            return NotFound(new { error = "Channel not found." });
        }

        bool nameChanged = false;
        bool descriptionChanged = false;
        string? oldName = null;

        if (request.Name is not null && request.Name.Trim() != channel.Name)
        {
            oldName = channel.Name;
            channel.Name = request.Name.Trim();
            nameChanged = true;
        }

        if (request.Description is not null)
        {
            channel.Description = request.Description;
            descriptionChanged = true;
        }

        await db.SaveChangesAsync();

        if (nameChanged)
        {
            // Notify all server members of the channel name change via SignalR.
            await hub.Clients.Group($"server-{serverId}").SendAsync("ChannelNameChanged", new
            {
                serverId,
                channelId,
                name = channel.Name
            });
            audit.Log(serverId, appUser.Id, AuditAction.ChannelRenamed,
                targetType: "Channel", targetId: channelId.ToString(),
                details: $"Renamed from \"{oldName}\" to \"{channel.Name}\"");
        }

        if (descriptionChanged)
        {
            await hub.Clients.Group($"server-{serverId}").SendAsync("ChannelDescriptionChanged", new
            {
                serverId,
                channelId,
                description = channel.Description
            });
            audit.Log(serverId, appUser.Id, AuditAction.ChannelDescriptionChanged,
                targetType: "Channel", targetId: channelId.ToString());
        }

        if (nameChanged || descriptionChanged)
        {
            webhookService.DispatchEvent(serverId, WebhookEventType.ChannelUpdated, new
            {
                channelId,
                serverId,
                name = channel.Name,
                description = channel.Description
            });
        }

        if (nameChanged || descriptionChanged)
        {
            await db.SaveChangesAsync();
        }

        return Ok(new
        {
            channel.Id,
            channel.Name,
            channel.ServerId,
            channel.Description,
            channel.CategoryId,
            channel.Position
        });
    }

    /* ═══════════════════ Channel Categories ═══════════════════ */

    /// <summary>
    /// Lists categories for a server. Requires membership or global admin.
    /// </summary>
    [HttpGet("{serverId:guid}/categories")]
    public async Task<IActionResult> GetCategories(Guid serverId)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureMemberAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var categories = await db.ChannelCategories
            .AsNoTracking()
            .Where(c => c.ServerId == serverId)
            .OrderBy(c => c.Position)
            .Select(c => new { c.Id, c.Name, c.Position })
            .ToListAsync();

        return Ok(categories);
    }

    /// <summary>
    /// Creates a category in a server. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpPost("{serverId:guid}/categories")]
    public async Task<IActionResult> CreateCategory(Guid serverId, [FromBody] CreateCategoryRequest request, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var maxPosition = await db.ChannelCategories
            .Where(c => c.ServerId == serverId)
            .Select(c => (int?)c.Position)
            .MaxAsync() ?? -1;

        var category = new ChannelCategory
        {
            ServerId = serverId,
            Name = request.Name.Trim(),
            Position = maxPosition + 1
        };

        db.ChannelCategories.Add(category);
        audit.Log(serverId, appUser.Id, AuditAction.CategoryCreated,
            targetType: "Category", targetId: category.Id.ToString(),
            details: $"Created category \"{category.Name}\"");
        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}").SendAsync("CategoryCreated", new
        {
            serverId,
            categoryId = category.Id,
            name = category.Name,
            position = category.Position
        });

        return Created($"/servers/{serverId}/categories/{category.Id}", new
        {
            category.Id,
            category.Name,
            category.ServerId,
            category.Position
        });
    }

    /// <summary>
    /// Renames a category. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpPatch("{serverId:guid}/categories/{categoryId:guid}")]
    public async Task<IActionResult> RenameCategory(Guid serverId, Guid categoryId, [FromBody] RenameCategoryRequest request, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var category = await db.ChannelCategories
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.ServerId == serverId);

        if (category is null)
        {
            return NotFound(new { error = "Category not found." });
        }

        var oldName = category.Name;
        category.Name = request.Name.Trim();
        audit.Log(serverId, appUser.Id, AuditAction.CategoryRenamed,
            targetType: "Category", targetId: categoryId.ToString(),
            details: $"Renamed from \"{oldName}\" to \"{category.Name}\"");
        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}").SendAsync("CategoryRenamed", new
        {
            serverId,
            categoryId,
            name = category.Name
        });

        return Ok(new { category.Id, category.Name, category.Position });
    }

    /// <summary>
    /// Deletes a category. Channels become uncategorized. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpDelete("{serverId:guid}/categories/{categoryId:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid serverId, Guid categoryId, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var category = await db.ChannelCategories
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.ServerId == serverId);

        if (category is null)
        {
            return NotFound(new { error = "Category not found." });
        }

        db.ChannelCategories.Remove(category);
        audit.Log(serverId, appUser.Id, AuditAction.CategoryDeleted,
            targetType: "Category", targetId: categoryId.ToString(),
            details: $"Deleted category \"{category.Name}\"");
        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}").SendAsync("CategoryDeleted", new
        {
            serverId,
            categoryId
        });

        return NoContent();
    }

    /// <summary>
    /// Updates the position and category assignment of all channels in a server.
    /// Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpPut("{serverId:guid}/channel-order")]
    public async Task<IActionResult> UpdateChannelOrder(Guid serverId, [FromBody] UpdateChannelOrderRequest request, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var channels = await db.Channels
            .Where(c => c.ServerId == serverId)
            .ToListAsync();

        if (request.Channels.Count != channels.Count)
        {
            return BadRequest(new { error = "Request must include all channels in the server." });
        }

        var validCategoryIds = await db.ChannelCategories
            .Where(c => c.ServerId == serverId)
            .Select(c => c.Id)
            .ToHashSetAsync();

        foreach (var item in request.Channels)
        {
            if (item.CategoryId is not null && !validCategoryIds.Contains(item.CategoryId.Value))
            {
                return BadRequest(new { error = $"CategoryId {item.CategoryId} does not belong to this server." });
            }
        }

        var channelLookup = channels.ToDictionary(c => c.Id);
        foreach (var item in request.Channels)
        {
            if (!channelLookup.TryGetValue(item.ChannelId, out var channel))
            {
                return BadRequest(new { error = $"Channel {item.ChannelId} not found in this server." });
            }
            channel.CategoryId = item.CategoryId;
            channel.Position = item.Position;
        }

        audit.Log(serverId, appUser.Id, AuditAction.ChannelMoved);
        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}").SendAsync("ChannelOrderChanged", new
        {
            serverId
        });

        return NoContent();
    }

    /// <summary>
    /// Updates the display order of categories in a server.
    /// Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpPut("{serverId:guid}/category-order")]
    public async Task<IActionResult> UpdateCategoryOrder(Guid serverId, [FromBody] UpdateCategoryOrderRequest request, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var categories = await db.ChannelCategories
            .Where(c => c.ServerId == serverId)
            .ToDictionaryAsync(c => c.Id);

        if (request.Categories.Count != categories.Count)
        {
            return BadRequest(new { error = "Request must include all categories in the server." });
        }

        foreach (var item in request.Categories)
        {
            if (!categories.TryGetValue(item.CategoryId, out var category))
            {
                return BadRequest(new { error = $"Category {item.CategoryId} not found in this server." });
            }
            category.Position = item.Position;
        }

        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}").SendAsync("CategoryOrderChanged", new
        {
            serverId
        });

        return NoContent();
    }

    /* ═══════════════════ Server Invites ═══════════════════ */

    /// <summary>
    /// Creates a new invite code for a server. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpPost("{serverId:guid}/invites")]
    public async Task<IActionResult> CreateInvite(Guid serverId, [FromBody] CreateInviteRequest request, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
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
        audit.Log(serverId, appUser.Id, AuditAction.InviteCreated,
            targetType: "Invite", targetId: invite.Id.ToString(),
            details: invite.Code);
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
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
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
    public async Task<IActionResult> RevokeInvite(Guid serverId, Guid inviteId, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var invite = await db.ServerInvites
            .FirstOrDefaultAsync(i => i.Id == inviteId && i.ServerId == serverId);

        if (invite is null)
        {
            return NotFound(new { error = "Invite not found." });
        }

        var inviteCode = invite.Code;
        db.ServerInvites.Remove(invite);
        audit.Log(serverId, appUser.Id, AuditAction.InviteRevoked,
            targetType: "Invite", targetId: inviteId.ToString(),
            details: inviteCode);
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
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
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

        webhookService.DispatchEvent(invite.ServerId, WebhookEventType.MemberJoined, new
        {
            serverId = invite.ServerId,
            userId = appUser.Id,
            displayName = appUser.EffectiveDisplayName
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
    public async Task<IActionResult> DeleteServer(Guid serverId, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureOwnerAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var serverName = await db.Servers
            .AsNoTracking()
            .Where(s => s.Id == serverId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync() ?? "Unknown";

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

        // Note: AuditLogEntry cascade-deletes with the server, so this is a best-effort log
        // that captures intent even though the entry won't persist after server deletion.
        // In practice, ServerId FK cascade will remove audit entries when the server is deleted.
        // We still call Log so future refactors (e.g. soft-delete) work correctly.
        try
        {
            audit.Log(serverId, appUser.Id, AuditAction.ServerDeleted, details: serverName);
            await db.SaveChangesAsync();
        }
        catch
        {
            // Audit log entry may fail if cascade already deleted the server record; ignore.
        }

        return NoContent();
    }

    /// <summary>
    /// Deletes a channel within a server. Requires Owner/Admin role or global admin privileges.
    /// Cascade-deletes all messages, reactions, and link previews in the channel.
    /// </summary>
    [HttpDelete("{serverId:guid}/channels/{channelId:guid}")]
    public async Task<IActionResult> DeleteChannel(Guid serverId, Guid channelId, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
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
        audit.Log(serverId, appUser.Id, AuditAction.ChannelDeleted,
            targetType: "Channel", targetId: channelId.ToString(),
            details: channel.Name);
        await db.SaveChangesAsync();

        // Clean up any cached message pages for this channel.
        await messageCache.InvalidateChannelAsync(channelId);

        // Notify all server members of the channel deletion via SignalR.
        await hub.Clients.Group($"server-{serverId}").SendAsync("ChannelDeleted", new
        {
            serverId,
            channelId
        });

        webhookService.DispatchEvent(serverId, WebhookEventType.ChannelDeleted, new
        {
            channelId,
            serverId,
            name = channel.Name
        });

        return NoContent();
    }

    /* ═══════════════════ Server Icon ═══════════════════ */

    /// <summary>
    /// Uploads or updates the server icon image. Requires Owner, Admin, or global admin role.
    /// Accepts JPG, JPEG, PNG, WebP, or GIF files up to 10 MB.
    /// </summary>
    [HttpPost("{serverId:guid}/icon")]
    public async Task<IActionResult> UploadServerIcon(Guid serverId, IFormFile file, [FromServices] AuditService audit)
    {
        var validationError = avatarService.Validate(file);
        if (validationError is not null)
        {
            return BadRequest(new { error = validationError });
        }

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var server = (await db.Servers.FindAsync(serverId))!;

        // Remove the previous icon file if one exists.
        if (!string.IsNullOrEmpty(server.IconUrl))
        {
            await avatarService.DeleteServerIconAsync(serverId);
        }

        var iconUrl = await avatarService.SaveServerIconAsync(serverId, file);
        server.IconUrl = iconUrl;
        audit.Log(serverId, appUser.Id, AuditAction.ServerIconChanged, details: "Icon uploaded");
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
    public async Task<IActionResult> DeleteServerIcon(Guid serverId, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var server = (await db.Servers.FindAsync(serverId))!;

        if (!string.IsNullOrEmpty(server.IconUrl))
        {
            await avatarService.DeleteServerIconAsync(serverId);
            server.IconUrl = null;
            audit.Log(serverId, appUser.Id, AuditAction.ServerIconChanged, details: "Icon removed");
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
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
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
        Guid serverId, [FromForm] string name, IFormFile file, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
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
        audit.Log(serverId, appUser.Id, AuditAction.EmojiUploaded,
            targetType: "Emoji", targetId: emoji.Id.ToString(),
            details: emoji.Name);
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
        Guid serverId, Guid emojiId, [FromBody] RenameEmojiRequest request, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var emoji = await db.CustomEmojis.FirstOrDefaultAsync(
            e => e.Id == emojiId && e.ServerId == serverId);
        if (emoji is null)
            return NotFound(new { error = "Emoji not found." });

        var nameTaken = await db.CustomEmojis.AnyAsync(
            e => e.ServerId == serverId && e.Name == request.Name && e.Id != emojiId);
        if (nameTaken)
            return Conflict(new { error = $"An emoji named '{request.Name}' already exists." });

        var oldEmojiName = emoji.Name;
        emoji.Name = request.Name;
        audit.Log(serverId, appUser.Id, AuditAction.EmojiRenamed,
            targetType: "Emoji", targetId: emojiId.ToString(),
            details: $"{oldEmojiName}→{emoji.Name}");
        await db.SaveChangesAsync();

        await hub.Clients.Group($"server-{serverId}")
            .SendAsync("CustomEmojiUpdated", new { serverId, emojiId, name = request.Name });

        return Ok(new { emoji.Id, emoji.Name, emoji.ImageUrl });
    }

    /// <summary>Deletes a custom emoji. Requires Owner or Admin role.</summary>
    [HttpDelete("{serverId:guid}/emojis/{emojiId:guid}")]
    public async Task<IActionResult> DeleteEmoji(Guid serverId, Guid emojiId, [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var emoji = await db.CustomEmojis.FirstOrDefaultAsync(
            e => e.Id == emojiId && e.ServerId == serverId);
        if (emoji is null)
            return NotFound(new { error = "Emoji not found." });

        var emojiName = emoji.Name;
        await customEmojiService.DeleteEmojiAsync(emoji.ImageUrl);
        db.CustomEmojis.Remove(emoji);
        audit.Log(serverId, appUser.Id, AuditAction.EmojiDeleted,
            targetType: "Emoji", targetId: emojiId.ToString(),
            details: emojiName);
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
        var trimmedQuery = request.Q?.Trim() ?? "";
        if (trimmedQuery.Length < 2)
        {
            return BadRequest(new { error = "Search query must be at least 2 characters." });
        }
        if (trimmedQuery.Length > 200)
        {
            return BadRequest(new { error = "Search query must be 200 characters or less." });
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
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

        // Build base query. Escape ILIKE metacharacters to prevent wildcard injection.
        var escaped = trimmedQuery.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var searchTerm = $"%{escaped}%";
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
                query = query.Where(m => m.ImageUrl != null || m.FileUrl != null);
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
                m.FileUrl,
                m.FileName,
                m.FileSize,
                m.FileContentType,
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
                message.FileUrl,
                message.FileName,
                message.FileSize,
                message.FileContentType,
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

    /* ═══════════════════ Audit Log ═══════════════════ */

    /// <summary>
    /// Returns paginated audit log entries for a server. Requires Owner, Admin, or global admin role.
    /// Supports cursor-based pagination via the <c>before</c> and <c>limit</c> query parameters.
    /// </summary>
    [HttpGet("{serverId:guid}/audit-log")]
    public async Task<IActionResult> GetAuditLog(Guid serverId, [FromQuery] DateTimeOffset? before, [FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 100);

        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var query = db.AuditLogEntries
            .AsNoTracking()
            .Where(e => e.ServerId == serverId);

        if (before.HasValue)
        {
            query = query.Where(e => e.CreatedAt < before.Value);
        }

        var rawEntries = await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit + 1)
            .Select(e => new
            {
                e.Id,
                e.ServerId,
                e.ActorUserId,
                e.Action,
                e.TargetType,
                e.TargetId,
                e.Details,
                e.CreatedAt,
                ActorDisplayName = e.ActorUser != null
                    ? (e.ActorUser.Nickname != null && e.ActorUser.Nickname != ""
                        ? e.ActorUser.Nickname
                        : e.ActorUser.DisplayName)
                    : "Deleted User",
                ActorAvatarUrl = e.ActorUser != null ? e.ActorUser.AvatarUrl : null,
                ActorCustomAvatarPath = e.ActorUser != null ? e.ActorUser.CustomAvatarPath : null
            })
            .ToListAsync();

        var hasMore = rawEntries.Count > limit;
        var entries = rawEntries
            .Take(limit)
            .Select(e => new
            {
                e.Id,
                e.ServerId,
                e.ActorUserId,
                Action = e.Action.ToString(),
                e.TargetType,
                e.TargetId,
                e.Details,
                e.CreatedAt,
                e.ActorDisplayName,
                ActorAvatarUrl = avatarService.ResolveUrl(e.ActorCustomAvatarPath) ?? e.ActorAvatarUrl
            })
            .ToList();

        return Ok(new { hasMore, entries });
    }

    /* ═══════════════════ Notification Preferences ═══════════════════ */

    /// <summary>
    /// Mutes or unmutes a server for the current user. Requires membership.
    /// </summary>
    [HttpPut("{serverId:guid}/mute")]
    public async Task<IActionResult> MuteServer(Guid serverId, [FromBody] MuteRequest request)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

        var member = await db.ServerMembers
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

        if (member is null)
        {
            return NotFound(new { error = "Server not found or you are not a member." });
        }

        member.IsMuted = request.IsMuted;
        await db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Mutes or unmutes a specific channel for the current user. Requires membership.
    /// If unmuting and no override exists, this is a no-op.
    /// </summary>
    [HttpPut("{serverId:guid}/channels/{channelId:guid}/mute")]
    public async Task<IActionResult> MuteChannel(Guid serverId, Guid channelId, [FromBody] MuteRequest request)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

        var isMember = await db.ServerMembers
            .AnyAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

        if (!isMember && !appUser.IsGlobalAdmin)
        {
            return NotFound(new { error = "Server not found or you are not a member." });
        }

        var channelExists = await db.Channels
            .AnyAsync(c => c.Id == channelId && c.ServerId == serverId);

        if (!channelExists)
        {
            return NotFound(new { error = "Channel not found." });
        }

        var existing = await db.ChannelNotificationOverrides
            .FirstOrDefaultAsync(o => o.UserId == appUser.Id && o.ChannelId == channelId);

        if (request.IsMuted)
        {
            if (existing is null)
            {
                db.ChannelNotificationOverrides.Add(new ChannelNotificationOverride
                {
                    UserId = appUser.Id,
                    ChannelId = channelId,
                    IsMuted = true
                });
            }
            else
            {
                existing.IsMuted = true;
            }
        }
        else
        {
            if (existing is not null)
            {
                db.ChannelNotificationOverrides.Remove(existing);
            }
        }

        await db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Returns the current user's notification preferences for a server,
    /// including server-level mute and per-channel overrides.
    /// </summary>
    [HttpGet("{serverId:guid}/notification-preferences")]
    public async Task<IActionResult> GetNotificationPreferences(Guid serverId)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);

        var member = await db.ServerMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ServerId == serverId && m.UserId == appUser.Id);

        if (member is null && !appUser.IsGlobalAdmin)
        {
            return NotFound(new { error = "Server not found or you are not a member." });
        }

        var serverChannelIds = await db.Channels
            .AsNoTracking()
            .Where(c => c.ServerId == serverId)
            .Select(c => c.Id)
            .ToListAsync();

        var channelOverrides = await db.ChannelNotificationOverrides
            .AsNoTracking()
            .Where(o => o.UserId == appUser.Id && serverChannelIds.Contains(o.ChannelId))
            .Select(o => new { channelId = o.ChannelId, isMuted = o.IsMuted })
            .ToListAsync();

        return Ok(new
        {
            serverMuted = member?.IsMuted ?? false,
            channelOverrides
        });
    }

    /* ═══════════════════ Webhooks ═══════════════════ */

    private static readonly HashSet<string> ValidEventTypes = Enum.GetValues<WebhookEventType>()
        .Select(e => e.ToString())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new webhook for a server. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpPost("{serverId:guid}/webhooks")]
    public async Task<IActionResult> CreateWebhook(
        Guid serverId,
        [FromBody] CreateWebhookRequest request,
        [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var invalidTypes = request.EventTypes.Where(t => !ValidEventTypes.Contains(t)).ToList();
        if (invalidTypes.Count > 0)
        {
            return BadRequest(new { error = $"Invalid event types: {string.Join(", ", invalidTypes)}" });
        }

        var webhook = new Webhook
        {
            ServerId = serverId,
            Name = request.Name.Trim(),
            Url = request.Url.Trim(),
            Secret = request.Secret,
            EventTypes = string.Join(",", request.EventTypes),
            IsActive = true,
            CreatedByUserId = appUser.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Webhooks.Add(webhook);
        audit.Log(serverId, appUser.Id, AuditAction.WebhookCreated,
            targetType: "Webhook", targetId: webhook.Id.ToString(),
            details: webhook.Name);
        await db.SaveChangesAsync();

        return Created($"/servers/{serverId}/webhooks/{webhook.Id}", new
        {
            webhook.Id,
            webhook.ServerId,
            webhook.Name,
            webhook.Url,
            EventTypes = webhook.EventTypes.Split(','),
            webhook.IsActive,
            webhook.CreatedByUserId,
            webhook.CreatedAt,
            HasSecret = webhook.Secret is not null
        });
    }

    /// <summary>
    /// Lists webhooks for a server. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpGet("{serverId:guid}/webhooks")]
    public async Task<IActionResult> GetWebhooks(Guid serverId)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var webhooks = await db.Webhooks
            .AsNoTracking()
            .Where(w => w.ServerId == serverId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new
            {
                w.Id,
                w.ServerId,
                w.Name,
                w.Url,
                EventTypes = w.EventTypes,
                w.IsActive,
                w.CreatedByUserId,
                w.CreatedAt,
                HasSecret = w.Secret != null
            })
            .ToListAsync();

        // Split EventTypes string into array for each webhook
        var result = webhooks.Select(w => new
        {
            w.Id,
            w.ServerId,
            w.Name,
            w.Url,
            EventTypes = w.EventTypes.Split(',', StringSplitOptions.RemoveEmptyEntries),
            w.IsActive,
            w.CreatedByUserId,
            w.CreatedAt,
            w.HasSecret
        });

        return Ok(result);
    }

    /// <summary>
    /// Updates a webhook. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpPatch("{serverId:guid}/webhooks/{webhookId:guid}")]
    public async Task<IActionResult> UpdateWebhook(
        Guid serverId,
        Guid webhookId,
        [FromBody] UpdateWebhookRequest request,
        [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var webhook = await db.Webhooks
            .FirstOrDefaultAsync(w => w.Id == webhookId && w.ServerId == serverId);

        if (webhook is null)
        {
            return NotFound(new { error = "Webhook not found." });
        }

        if (request.Name is not null) webhook.Name = request.Name.Trim();
        if (request.Url is not null) webhook.Url = request.Url.Trim();
        if (request.Secret is not null) webhook.Secret = request.Secret;
        if (request.IsActive.HasValue) webhook.IsActive = request.IsActive.Value;

        if (request.EventTypes is not null)
        {
            var invalidTypes = request.EventTypes.Where(t => !ValidEventTypes.Contains(t)).ToList();
            if (invalidTypes.Count > 0)
            {
                return BadRequest(new { error = $"Invalid event types: {string.Join(", ", invalidTypes)}" });
            }
            webhook.EventTypes = string.Join(",", request.EventTypes);
        }

        audit.Log(serverId, appUser.Id, AuditAction.WebhookUpdated,
            targetType: "Webhook", targetId: webhookId.ToString(),
            details: webhook.Name);
        await db.SaveChangesAsync();

        return Ok(new
        {
            webhook.Id,
            webhook.ServerId,
            webhook.Name,
            webhook.Url,
            EventTypes = webhook.EventTypes.Split(','),
            webhook.IsActive,
            webhook.CreatedByUserId,
            webhook.CreatedAt,
            HasSecret = webhook.Secret is not null
        });
    }

    /// <summary>
    /// Deletes a webhook. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpDelete("{serverId:guid}/webhooks/{webhookId:guid}")]
    public async Task<IActionResult> DeleteWebhook(
        Guid serverId,
        Guid webhookId,
        [FromServices] AuditService audit)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var webhook = await db.Webhooks
            .FirstOrDefaultAsync(w => w.Id == webhookId && w.ServerId == serverId);

        if (webhook is null)
        {
            return NotFound(new { error = "Webhook not found." });
        }

        var webhookName = webhook.Name;
        db.Webhooks.Remove(webhook);
        audit.Log(serverId, appUser.Id, AuditAction.WebhookDeleted,
            targetType: "Webhook", targetId: webhookId.ToString(),
            details: webhookName);
        await db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Gets recent delivery logs for a webhook. Requires Owner, Admin, or global admin role.
    /// </summary>
    [HttpGet("{serverId:guid}/webhooks/{webhookId:guid}/deliveries")]
    public async Task<IActionResult> GetWebhookDeliveries(
        Guid serverId,
        Guid webhookId,
        [FromQuery] int limit = 25)
    {
        var (appUser, _) = await userService.GetOrCreateUserAsync(User);
        await userService.EnsureAdminAsync(serverId, appUser.Id, appUser.IsGlobalAdmin);

        var webhookExists = await db.Webhooks
            .AnyAsync(w => w.Id == webhookId && w.ServerId == serverId);

        if (!webhookExists)
        {
            return NotFound(new { error = "Webhook not found." });
        }

        var deliveries = await db.WebhookDeliveryLogs
            .AsNoTracking()
            .Where(l => l.WebhookId == webhookId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(Math.Min(limit, 100))
            .Select(l => new
            {
                l.Id,
                l.EventType,
                l.StatusCode,
                l.ErrorMessage,
                l.Success,
                l.Attempt,
                l.CreatedAt
            })
            .ToListAsync();

        return Ok(deliveries);
    }
}
