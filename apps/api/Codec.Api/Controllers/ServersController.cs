using Codec.Api.Data;
using Codec.Api.Models;
using Codec.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Controllers;

/// <summary>
/// Manages servers, memberships, and server-scoped channels.
/// </summary>
[ApiController]
[Authorize]
[Route("servers")]
public class ServersController(CodecDbContext db, IUserService userService) : ControllerBase
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
    /// Discovers all servers, indicating whether the user is already a member.
    /// </summary>
    [HttpGet("discover")]
    public async Task<IActionResult> Discover()
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var servers = await db.Servers
            .AsNoTracking()
            .Select(server => new
            {
                server.Id,
                server.Name,
                IsMember = db.ServerMembers.Any(member => member.ServerId == server.Id && member.UserId == appUser.Id)
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
    /// Joins an existing server as a Member.
    /// </summary>
    [HttpPost("{serverId:guid}/join")]
    public async Task<IActionResult> Join(Guid serverId)
    {
        var appUser = await userService.GetOrCreateUserAsync(User);
        var serverExists = await db.Servers.AsNoTracking().AnyAsync(server => server.Id == serverId);
        if (!serverExists)
        {
            return NotFound(new { error = "Server not found." });
        }

        var existing = await db.ServerMembers.FindAsync(serverId, appUser.Id);
        if (existing is not null)
        {
            return Ok(new { serverId, userId = appUser.Id, role = existing.Role.ToString() });
        }

        var membership = new ServerMember
        {
            ServerId = serverId,
            UserId = appUser.Id,
            Role = ServerRole.Member,
            JoinedAt = DateTimeOffset.UtcNow
        };

        db.ServerMembers.Add(membership);
        await db.SaveChangesAsync();

        return Created($"/servers/{serverId}/members/{appUser.Id}", new
        {
            serverId,
            userId = appUser.Id,
            role = membership.Role.ToString()
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
                member.User.Email,
                member.User.AvatarUrl
            })
            .OrderBy(member => member.DisplayName)
            .ToListAsync();

        return Ok(members);
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
}
