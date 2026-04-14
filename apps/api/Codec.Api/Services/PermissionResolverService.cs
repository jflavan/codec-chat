using Codec.Api.Data;
using Codec.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Codec.Api.Services;

public class PermissionResolverService(CodecDbContext db) : IPermissionResolverService
{
    private readonly Dictionary<(Guid, Guid), List<ServerRoleEntity>> _roleCache = new();

    public async Task<Permission> ResolveServerPermissionsAsync(Guid serverId, Guid userId)
    {
        var roles = await GetRolesAsync(serverId, userId);

        if (roles.Any(r => r.IsSystemRole && r.Position == 0))
            return (Permission)~0L;

        var perms = Permission.None;
        foreach (var role in roles)
            perms |= role.Permissions;

        if ((perms & Permission.Administrator) != 0)
            return (Permission)~0L;

        return perms;
    }

    public async Task<Permission> ResolveChannelPermissionsAsync(Guid channelId, Guid userId)
    {
        var channel = await db.Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel is null) return Permission.None;

        var serverId = channel.ServerId;
        var roles = await GetRolesAsync(serverId, userId);

        if (roles.Any(r => r.IsSystemRole && r.Position == 0))
            return (Permission)~0L;
        var serverPerms = Permission.None;
        foreach (var role in roles)
            serverPerms |= role.Permissions;

        if ((serverPerms & Permission.Administrator) != 0)
            return (Permission)~0L;

        var roleIds = roles.Select(r => r.Id).ToList();
        var overrides = await db.ChannelPermissionOverrides.AsNoTracking()
            .Where(o => o.ChannelId == channelId && roleIds.Contains(o.RoleId))
            .ToListAsync();

        var channelAllow = Permission.None;
        var channelDeny = Permission.None;
        foreach (var o in overrides)
        {
            channelAllow |= o.Allow;
            channelDeny |= o.Deny;
        }

        var effective = (serverPerms | channelAllow) & ~channelDeny;

        if ((effective & Permission.ViewChannels) == 0)
            return Permission.None;

        return effective;
    }

    public async Task<bool> HasServerPermissionAsync(Guid serverId, Guid userId, Permission permission)
    {
        var perms = await ResolveServerPermissionsAsync(serverId, userId);
        return perms.Has(permission);
    }

    public async Task<bool> HasChannelPermissionAsync(Guid channelId, Guid userId, Permission permission)
    {
        var perms = await ResolveChannelPermissionsAsync(channelId, userId);
        return perms.Has(permission);
    }

    public async Task<int> GetHighestRolePositionAsync(Guid serverId, Guid userId)
    {
        var roles = await GetRolesAsync(serverId, userId);
        return roles.Count > 0 ? roles.Min(r => r.Position) : int.MaxValue;
    }

    public async Task<bool> IsOwnerAsync(Guid serverId, Guid userId)
    {
        var roles = await GetRolesAsync(serverId, userId);
        return roles.Any(r => r.IsSystemRole && r.Position == 0);
    }

    private async Task<List<ServerRoleEntity>> GetRolesAsync(Guid serverId, Guid userId)
    {
        if (_roleCache.TryGetValue((serverId, userId), out var cached))
            return cached;

        var roles = await db.ServerMemberRoles.AsNoTracking()
            .Where(mr => mr.Role!.ServerId == serverId && mr.UserId == userId)
            .Select(mr => mr.Role!)
            .ToListAsync();

        _roleCache[(serverId, userId)] = roles;
        return roles;
    }
}
