namespace Codec.Api.Services;

using Codec.Api.Models;

public interface IPermissionResolverService
{
    Task<Permission> ResolveServerPermissionsAsync(Guid serverId, Guid userId);
    Task<Permission> ResolveChannelPermissionsAsync(Guid channelId, Guid userId);
    Task<bool> HasServerPermissionAsync(Guid serverId, Guid userId, Permission permission);
    Task<bool> HasChannelPermissionAsync(Guid channelId, Guid userId, Permission permission);
    Task<int> GetHighestRolePositionAsync(Guid serverId, Guid userId);
    Task<bool> IsOwnerAsync(Guid serverId, Guid userId);
}
